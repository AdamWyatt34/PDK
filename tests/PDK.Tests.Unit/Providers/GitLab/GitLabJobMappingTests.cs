using FluentAssertions;
using PDK.Core.Artifacts;
using PDK.Core.Expressions;
using PDK.Core.Models;
using PDK.Providers.GitLab;
using PDK.Runners;
using Xunit;

namespace PDK.Tests.Unit.Providers.GitLab;

/// <summary>
/// Tests for the pieces between the YAML and the runners: step shapes, variable expansion, durations, the
/// predefined variables, the exported environment and how the runtime honours parse-time skip decisions.
/// </summary>
public sealed class GitLabJobMappingTests : IDisposable
{
    private readonly GitLabCiParser _parser = new();
    private readonly string _workspace;

    public GitLabJobMappingTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "pdk-gitlab-mapping-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_workspace, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private Pipeline Parse(string yaml, string eventName = "push") =>
        _parser.Parse(yaml, new PipelineParseOptions { WorkspacePath = _workspace, EventName = eventName });

    // ------------------------------------------------------------------ steps

    [Fact]
    public void Steps_AreOrderedDownloadsScriptAfterScriptArtifacts()
    {
        var pipeline = Parse("""
            stages: [build, test]
            compile:
              stage: build
              script: echo
              artifacts:
                paths: [out/]
            test:
              stage: test
              before_script: [echo before]
              script: [echo test]
              after_script: [echo after]
              artifacts:
                paths: [results/]
            """);

        pipeline.Jobs["test"].Steps.Select(s => s.Id).Should().Equal("download:compile", "script", "after_script", "artifacts");
        pipeline.Jobs["test"].Steps.Select(s => s.Type).Should().Equal(
            StepType.DownloadArtifact, StepType.Script, StepType.Script, StepType.UploadArtifact);
    }

    [Fact]
    public void DownloadStep_TargetsWorkspaceRootAndNeverFailsTheJob()
    {
        var pipeline = Parse("""
            stages: [build, test]
            compile:
              stage: build
              script: echo
              artifacts:
                name: compiled-$CI_COMMIT_REF_SLUG
                paths: [out/]
            test:
              stage: test
              script: echo
            """);

        var download = pipeline.Jobs["test"].Steps[0];
        download.Name.Should().Be("Download artifacts from compile");
        download.Artifact!.Operation.Should().Be(ArtifactOperation.Download);
        download.Artifact.Name.Should().Be("compiled-", "the temporary workspace has no ref name");
        download.Artifact.TargetPath.Should().BeNull();
        download.Artifact.Patterns.Should().BeEmpty();
        download.ContinueOnError.Should().BeTrue();
    }

    [Fact]
    public void UploadStep_UsesJobNameWhenArtifactsHaveNoName()
    {
        var pipeline = Parse("""
            compile:
              script: echo
              parallel: 2
              artifacts:
                paths: [out/]
            """);

        pipeline.Jobs["compile 1/2"].Steps.Last().Artifact!.Name.Should().Be("compile 1/2");
        pipeline.Jobs["compile 2/2"].Steps.Last().Artifact!.Name.Should().Be("compile 2/2");
        pipeline.Jobs["compile 1/2"].Steps.Last().Artifact!.Options.OverwriteExisting.Should().BeTrue();
        pipeline.Jobs["compile 1/2"].Steps.Last().Artifact!.Options.Compression.Should().Be(CompressionType.Gzip);
    }

    [Fact]
    public void DownloadsFromParallelProducer_OnePerArtifactName()
    {
        var pipeline = Parse("""
            stages: [build, test]
            compile:
              stage: build
              parallel: 2
              script: echo
              artifacts:
                paths: [out/]
            test:
              stage: test
              script: echo
            """);

        pipeline.Jobs["test"].Steps.Where(s => s.Type == StepType.DownloadArtifact).Select(s => s.Artifact!.Name)
            .Should().Equal("compile 1/2", "compile 2/2");
        pipeline.Jobs["test"].DependsOn.Should().Equal("compile 1/2", "compile 2/2");
    }

    [Fact]
    public void ScriptStep_KeepsScriptTextVerbatim()
    {
        var job = Parse("""
            build:
              script:
                - 'echo "quoted: value"'
                - |
                  if [ -f x ]; then
                    echo yes
                  fi
                - echo $VAR
            """).Jobs["build"];

        job.Steps[0].Script.Should().Be("echo \"quoted: value\"\nif [ -f x ]; then\n  echo yes\nfi\n\necho $VAR");
    }

    [Fact]
    public void JobId_EqualsJobName_EvenWithSpacesAndColons()
    {
        var pipeline = Parse("""
            "build and test":
              script: echo
            deploy:
              script: echo
              parallel:
                matrix:
                  - REGION: [eu, us]
            """);

        pipeline.Jobs["build and test"].Id.Should().Be("build and test");
        pipeline.Jobs["deploy: [eu]"].Name.Should().Be("deploy: [eu]");
        JobGraph.ResolveId(pipeline, "DEPLOY: [EU]").Should().Be("deploy: [eu]");
        JobGraph.Order(pipeline).Select(p => p.Key).Should().Equal("build and test", "deploy: [eu]", "deploy: [us]");
    }

    [Fact]
    public void Image_IsOptionalOnTheHost_LikeGitLabShellExecutors()
    {
        var job = Parse("""
            build:
              image: mcr.microsoft.com/dotnet/sdk:8.0
              script: echo
            """).Jobs["build"];

        job.Container.Should().Be("mcr.microsoft.com/dotnet/sdk:8.0");
        job.ContainerOptional.Should().BeTrue();
        PDK.Core.Runners.RunnerCapabilities.RequiresCustomImage(job).Should().BeFalse();
        PDK.Core.Runners.RunnerCapabilities.ValidateJobRequirements(job, PDK.Core.Runners.RunnerType.Host).Should().BeEmpty();

        var gitHubStyle = new Job { Id = "x", Name = "x", Container = "node:20" };
        PDK.Core.Runners.RunnerCapabilities.RequiresCustomImage(gitHubStyle).Should().BeTrue("GitHub/Azure containers stay a hard requirement");
    }

    [Fact]
    public void Environment_IsLeftEmpty_VariablesCarryValues()
    {
        var job = Parse("""
            build:
              variables:
                A: "1"
              script: echo
            """).Jobs["build"];

        job.Environment.Should().BeEmpty();
        job.Variables.Should().Contain("A", "1");
    }

    // ------------------------------------------------------------------ durations

    [Theory]
    [InlineData("1h 30m", 90)]
    [InlineData("1h30m", 90)]
    [InlineData("90 minutes", 90)]
    [InlineData("2h", 120)]
    [InlineData("1 day", 1440)]
    [InlineData("1 hour 30 minutes", 90)]
    [InlineData("3600", 60)]
    [InlineData("2 hrs, 5 mins", 125)]
    [InlineData("1 week", 7 * 1440)]
    [InlineData("1.5h", 90)]
    public void Duration_Parses(string text, int expectedMinutes)
    {
        GitLabDuration.TryParse(text, out var duration).Should().BeTrue();
        duration.Should().Be(TimeSpan.FromMinutes(expectedMinutes));
    }

    [Theory]
    [InlineData("")]
    [InlineData("never")]
    [InlineData("1 fortnight")]
    [InlineData("h")]
    [InlineData("1h and then some")]
    public void Duration_RejectsInvalidText(string text)
    {
        GitLabDuration.TryParse(text, out _).Should().BeFalse();
    }

    // ------------------------------------------------------------------ variable expansion

    [Theory]
    [InlineData("$A", "1")]
    [InlineData("${A}", "1")]
    [InlineData("%A%", "1")]
    [InlineData("x$A-y", "x1-y")]
    [InlineData("$$A", "$A")]
    [InlineData("$UNDEFINED", "")]
    [InlineData("$A$B", "12")]
    [InlineData("no references", "no references")]
    [InlineData("$1", "$1")]
    public void Expander_ReplacesReferences(string text, string expected)
    {
        var variables = new Dictionary<string, string> { ["A"] = "1", ["B"] = "2" };

        GitLabVariableExpander.Expand(text, name => variables.TryGetValue(name, out var v) ? v : null).Should().Be(expected);
    }

    [Fact]
    public void Expander_KeepUndefined_LeavesReferencesAsWritten()
    {
        GitLabVariableExpander.Expand("$A/${B}/$$", _ => null, keepUndefined: true).Should().Be("$A/${B}/$$");
        GitLabVariableExpander.Expand("$A/${B}/$$", name => name == "A" ? "a" : null, keepUndefined: true).Should().Be("a/${B}/$$");
    }

    [Fact]
    public void Expander_ExpandAll_ResolvesSiblingsInAnyOrder()
    {
        var variables = new List<KeyValuePair<string, string>>
        {
            new("FULL", "$BASE/bin:$OUTER"),
            new("BASE", "/opt"),
            new("SELF", "$SELF:/extra"),
            new("RAW", "$BASE"),
            new("LOOP_A", "$LOOP_B"),
            new("LOOP_B", "$LOOP_A")
        };

        var result = GitLabVariableExpander.ExpandAll(
            variables,
            name => name switch { "OUTER" => "o", "SELF" => "outer-self", _ => null },
            new HashSet<string> { "RAW" });

        result["FULL"].Should().Be("/opt/bin:o");
        result["SELF"].Should().Be("outer-self:/extra");
        result["RAW"].Should().Be("$BASE");
        result["LOOP_A"].Should().Be(string.Empty);
    }

    [Fact]
    public void Expander_ContainsReference_IgnoresEscapedDollar()
    {
        GitLabVariableExpander.ContainsReference("cost $$5").Should().BeFalse();
        GitLabVariableExpander.ContainsReference("cost $FIVE").Should().BeTrue();
        GitLabVariableExpander.ContainsReference(null).Should().BeFalse();
    }

    // ------------------------------------------------------------------ predefined variables

    [Theory]
    [InlineData("push", "push")]
    [InlineData("", "push")]
    [InlineData("pull_request", "merge_request_event")]
    [InlineData("pull_request_target", "merge_request_event")]
    [InlineData("merge_request_event", "merge_request_event")]
    [InlineData("schedule", "schedule")]
    [InlineData("workflow_dispatch", "web")]
    [InlineData("web", "web")]
    [InlineData("api", "api")]
    [InlineData("trigger", "trigger")]
    [InlineData("pipeline", "pipeline")]
    [InlineData("something-else", "push")]
    public void PipelineSource_MapsEventNames(string eventName, string expected)
    {
        GitLabPredefinedVariables.PipelineSource(eventName).Should().Be(expected);
    }

    [Theory]
    [InlineData("main", "main")]
    [InlineData("feature/Login_Page", "feature-login-page")]
    [InlineData("--weird--", "weird")]
    [InlineData("release/1.2.3", "release-1-2-3")]
    public void Slug_FollowsGitLabRules(string value, string expected)
    {
        GitLabPredefinedVariables.Slug(value).Should().Be(expected);
    }

    [Fact]
    public void Slug_IsLimitedTo63Characters()
    {
        GitLabPredefinedVariables.Slug(new string('a', 70) + "-b").Should().HaveLength(63);
    }

    [Fact]
    public void PredefinedVariables_ForPushOnBranch()
    {
        var git = new GitInfo
        {
            IsRepository = true,
            Sha = "0123456789abcdef0123456789abcdef01234567",
            Branch = "feature/x",
            Ref = "refs/heads/feature/x",
            RemoteUrl = "git@gitlab.com:group/project.git",
            Repository = "group/project",
            DefaultBranch = "develop"
        };

        var env = GitLabPredefinedVariables.Build(new GitLabVariableContext
        {
            Git = git,
            Workspace = "/work/project",
            EventName = "push",
            PipelineName = "CI",
            RunId = "42",
            Actor = "alice",
            Job = new Job { Name = "build: [a]", Stage = "build", Container = "alpine" },
            JobNumber = 7
        });

        env["CI"].Should().Be("true");
        env["GITLAB_CI"].Should().Be("true");
        env["CI_PIPELINE_SOURCE"].Should().Be("push");
        env["CI_COMMIT_BRANCH"].Should().Be("feature/x");
        env["CI_COMMIT_REF_NAME"].Should().Be("feature/x");
        env["CI_COMMIT_REF_SLUG"].Should().Be("feature-x");
        env["CI_COMMIT_SHA"].Should().Be(git.Sha);
        env["CI_COMMIT_SHORT_SHA"].Should().Be("0123456");
        env["CI_DEFAULT_BRANCH"].Should().Be("develop");
        env["CI_PROJECT_DIR"].Should().Be("/work/project");
        env["CI_BUILDS_DIR"].Should().Be("/work");
        env["CI_PROJECT_NAME"].Should().Be("project");
        env["CI_PROJECT_NAMESPACE"].Should().Be("group");
        env["CI_PROJECT_PATH"].Should().Be("group/project");
        env["CI_PROJECT_URL"].Should().Be("https://gitlab.com/group/project");
        env["CI_REPOSITORY_URL"].Should().Be("git@gitlab.com:group/project.git");
        env["CI_SERVER_URL"].Should().Be("https://gitlab.com");
        env["CI_PIPELINE_ID"].Should().Be("42");
        env["CI_PIPELINE_NAME"].Should().Be("CI");
        env["CI_JOB_ID"].Should().Be("7");
        env["CI_JOB_NAME"].Should().Be("build: [a]");
        env["CI_JOB_NAME_SLUG"].Should().Be("build---a", "GitLab replaces every non-alphanumeric character with '-' without collapsing runs");
        env["CI_JOB_STAGE"].Should().Be("build");
        env["CI_JOB_STATUS"].Should().Be("running");
        env["CI_JOB_IMAGE"].Should().Be("alpine");
        env["CI_RUNNER_TAGS"].Should().Be("[\"pdk\"]");
        env["GITLAB_USER_LOGIN"].Should().Be("alice");
        env.Should().NotContainKey("CI_COMMIT_TAG");
        env.Should().NotContainKey("CI_MERGE_REQUEST_IID");
    }

    [Fact]
    public void PredefinedVariables_ForMergeRequestEvent()
    {
        var git = new GitInfo { IsRepository = true, Sha = "abcdef1234567", Branch = "topic", Ref = "refs/heads/topic" };

        var env = GitLabPredefinedVariables.Build(new GitLabVariableContext
        {
            Git = git,
            Workspace = "/work",
            EventName = "pull_request",
            DefaultBranch = "main"
        });

        env["CI_PIPELINE_SOURCE"].Should().Be("merge_request_event");
        env.Should().NotContainKey("CI_COMMIT_BRANCH");
        env["CI_COMMIT_REF_NAME"].Should().Be("topic");
        env["CI_MERGE_REQUEST_IID"].Should().Be("1");
        env["CI_MERGE_REQUEST_SOURCE_BRANCH_NAME"].Should().Be("topic");
        env["CI_MERGE_REQUEST_TARGET_BRANCH_NAME"].Should().Be("main");
        env.Should().NotContainKey("CI_JOB_NAME");
    }

    [Fact]
    public void PredefinedVariables_OutsideGit_FallBackToWorkspaceName()
    {
        var env = GitLabPredefinedVariables.Build(new GitLabVariableContext
        {
            Git = GitInfo.Empty,
            Workspace = Path.Combine("/tmp", "my-app")
        });

        env["CI_PROJECT_NAME"].Should().Be("my-app");
        env["CI_PROJECT_PATH"].Should().Be("local/my-app");
        env["CI_DEFAULT_BRANCH"].Should().Be("main");
        env["CI_COMMIT_BRANCH"].Should().BeEmpty();
        env["CI_COMMIT_SHA"].Should().BeEmpty();
    }

    // ------------------------------------------------------------------ runtime environment

    private static JobRuntimeInfo RuntimeInfo(string workspace, string eventName = "push") => new()
    {
        Workspace = workspace,
        StepWorkspace = workspace,
        Provider = PipelineProvider.GitLab,
        PipelineName = "CI",
        EventName = eventName,
        RunId = "99",
        Git = GitInfo.Empty,
        Variables = new Dictionary<string, string> { ["CLI_VAR"] = "cli", ["SHARED"] = "cli" },
        Secrets = new Dictionary<string, string> { ["TOKEN"] = "s3cret" }
    };

    [Fact]
    public void StepEnvironment_ExportsGitLabVariablesWithoutGitHubOnes()
    {
        var pipeline = Parse("""
            variables:
              SHARED: pipeline
              PIPE: "1"
            build:
              stage: build
              variables:
                JOBVAR: "2"
              script: echo
            """);
        var job = pipeline.Jobs["build"];

        var env = PipelineContextBuilder.BuildStepEnvironment(pipeline, job, RuntimeInfo(_workspace));

        env["CI"].Should().Be("true");
        env["GITLAB_CI"].Should().Be("true");
        env["CI_JOB_NAME"].Should().Be("build");
        env["CI_JOB_STAGE"].Should().Be("build");
        env["CI_PIPELINE_ID"].Should().Be("99");
        env["CI_PROJECT_DIR"].Should().Be(_workspace);
        env["PIPE"].Should().Be("1");
        env["JOBVAR"].Should().Be("2");
        env["CLI_VAR"].Should().Be("cli");
        env["SHARED"].Should().Be("cli", "PDK variables override pipeline variables");
        env["TOKEN"].Should().Be("s3cret");
        env["PDK_JOB"].Should().Be("build");
        env.Keys.Should().NotContain(k => k.StartsWith("GITHUB_", StringComparison.Ordinal));
        env.Keys.Should().NotContain(k => k.StartsWith("RUNNER_", StringComparison.Ordinal));
        env.Should().NotContainKey("TF_BUILD");
    }

    [Fact]
    public void StepEnvironment_JobVariablesWinOverEverything()
    {
        var pipeline = Parse("""
            variables:
              SHARED: pipeline
            build:
              variables:
                SHARED: job
                CI_JOB_STAGE: custom
              script: echo
            """);

        var env = PipelineContextBuilder.BuildStepEnvironment(pipeline, pipeline.Jobs["build"], RuntimeInfo(_workspace));

        env["SHARED"].Should().Be("job");
        env["CI_JOB_STAGE"].Should().Be("custom");
    }

    [Fact]
    public void JobContext_ExposesGitLabEnvAndKeepsGitHubSyntax()
    {
        var pipeline = Parse("""
            variables:
              PIPE: "1"
            build:
              script: echo
            """);

        var context = PipelineContextBuilder.BuildJobContext(pipeline, pipeline.Jobs["build"], RuntimeInfo(_workspace));

        context.Syntax.Should().Be(ExpressionSyntax.GitHub);
        PipelineContextBuilder.SyntaxFor(PipelineProvider.GitLab).Should().Be(ExpressionSyntax.GitHub);
        ExpressionEvaluator.EvaluateCondition("always()", context).Should().BeTrue();
        ExpressionEvaluator.EvaluateCondition("env.CI_JOB_NAME == 'build' && env.PIPE == '1'", context).Should().BeTrue();
    }

    [Fact]
    public void StatusFromNeeds_SkippedDependency_DoesNotBlockGitLabJobs()
    {
        var needs = new Dictionary<string, string> { ["manual"] = "skipped", ["build"] = "success" };

        PipelineContextBuilder.StatusFromNeeds(needs, ExpressionSyntax.GitHub, PipelineProvider.GitLab).Should().Be(ExpressionJobStatus.Success);
        PipelineContextBuilder.StatusFromNeeds(needs, ExpressionSyntax.GitHub, PipelineProvider.GitHub).Should().Be(ExpressionJobStatus.Skipped);
        PipelineContextBuilder.StatusFromNeeds(needs, ExpressionSyntax.GitHub).Should().Be(ExpressionJobStatus.Skipped);

        var failed = new Dictionary<string, string> { ["build"] = "failure" };
        PipelineContextBuilder.StatusFromNeeds(failed, ExpressionSyntax.GitHub, PipelineProvider.GitLab).Should().Be(ExpressionJobStatus.Failure);
    }

    [Fact]
    public void JobConditionEvaluator_UsesParseTimeSkipReason()
    {
        var pipeline = Parse("""
            deploy:
              script: echo
              when: manual
            build:
              script: echo
            """);

        var run = new JobRunContext { WorkspacePath = _workspace, Pipeline = pipeline };

        var manual = JobConditionEvaluator.Evaluate(pipeline.Jobs["deploy"], run);
        manual.Run.Should().BeFalse();
        manual.Failed.Should().BeFalse();
        manual.Reason.Should().Be("manual job (when: manual)");

        JobConditionEvaluator.Evaluate(pipeline.Jobs["build"], run).Run.Should().BeTrue();
    }

    [Fact]
    public void JobConditionEvaluator_SkippedEarlierStageJob_DoesNotBlockLaterStage()
    {
        var pipeline = Parse("""
            stages: [build, test]
            compile:
              stage: build
              script: echo
            unit:
              stage: test
              script: echo
            """);

        var run = new JobRunContext
        {
            WorkspacePath = _workspace,
            Pipeline = pipeline,
            NeedsResults = new Dictionary<string, string> { ["compile"] = "skipped" }
        };

        JobConditionEvaluator.Evaluate(pipeline.Jobs["unit"], run).Run.Should().BeTrue();

        var failedRun = run with { NeedsResults = new Dictionary<string, string> { ["compile"] = "failure" } };
        JobConditionEvaluator.Evaluate(pipeline.Jobs["unit"], failedRun).Run.Should().BeFalse();
    }

    [Fact]
    public void JobConditionEvaluator_OnFailureAndAlwaysJobs()
    {
        var pipeline = Parse("""
            stages: [build, notify]
            compile:
              stage: build
              script: echo
            alert:
              stage: notify
              script: echo
              when: on_failure
            cleanup:
              stage: notify
              script: echo
              when: always
            """);

        var success = new JobRunContext { WorkspacePath = _workspace, Pipeline = pipeline, NeedsResults = new Dictionary<string, string> { ["compile"] = "success" } };
        var failure = success with { NeedsResults = new Dictionary<string, string> { ["compile"] = "failure" } };

        JobConditionEvaluator.Evaluate(pipeline.Jobs["alert"], success).Run.Should().BeFalse();
        JobConditionEvaluator.Evaluate(pipeline.Jobs["alert"], failure).Run.Should().BeTrue();
        JobConditionEvaluator.Evaluate(pipeline.Jobs["cleanup"], success).Run.Should().BeTrue();
        JobConditionEvaluator.Evaluate(pipeline.Jobs["cleanup"], failure).Run.Should().BeTrue();
    }

    [Fact]
    public void ExecutionSession_GitLabJob_GetsNoGitHubRuntimeFiles()
    {
        var pipeline = Parse("""
            build:
              script: echo
              after_script: [echo bye]
            """);
        var job = pipeline.Jobs["build"];
        var run = new JobRunContext { WorkspacePath = _workspace, Pipeline = pipeline };
        var session = new JobExecutionSession(job, run, _workspace, containerImage: null);

        try
        {
            session.IsGitLab.Should().BeTrue();
            session.IsGitHub.Should().BeTrue("GitLab jobs use the GitHub expression dialect");

            var script = session.PrepareStep(job.Steps[0], 0);
            script.Skip.Should().BeFalse();
            script.Environment.Should().NotContainKey("GITHUB_OUTPUT");
            script.Environment.Should().NotContainKey("GITHUB_ENV");
            script.Environment.Should().NotContainKey("GITHUB_EVENT_PATH");
            script.Environment["CI_JOB_STATUS"].Should().Be("running");
            script.Environment["GITLAB_CI"].Should().Be("true");
            File.Exists(Path.Combine(session.HostRuntimeDirectory, "event.json")).Should().BeFalse();

            session.Record(job.Steps[0], 0, JobExecutionSession.FailedResult("script", "boom", allowedFailure: false, exitCode: 1));

            var after = session.PrepareStep(job.Steps[1], 1);
            after.Skip.Should().BeFalse("after_script uses always()");
            after.Environment["CI_JOB_STATUS"].Should().Be("failed");
        }
        finally
        {
            session.Cleanup();
        }
    }

    [Fact]
    public void ExecutionSession_GitLabJob_ReportsSuccessToAfterScript()
    {
        var pipeline = Parse("""
            build:
              script: echo
              after_script: [echo bye]
            """);
        var job = pipeline.Jobs["build"];
        var session = new JobExecutionSession(job, new JobRunContext { WorkspacePath = _workspace, Pipeline = pipeline }, _workspace, null);

        try
        {
            session.Record(job.Steps[0], 0, JobExecutionSession.SkippedResult("script", "n/a") with { Skipped = false, Success = true });
            session.PrepareStep(job.Steps[1], 1).Environment["CI_JOB_STATUS"].Should().Be("success");
        }
        finally
        {
            session.Cleanup();
        }
    }
}
