using FluentAssertions;
using PDK.Core.ErrorHandling;
using PDK.Core.Models;
using PDK.Providers.AzureDevOps;
using PDK.Providers.GitHub;
using PDK.Providers.GitLab;
using Xunit;

namespace PDK.Tests.Unit.Providers.GitLab;

/// <summary>
/// Structure-level tests for the GitLab CI parser: stages, needs, rules/only/except, extends, includes, references,
/// parallel jobs, variables, workflow rules, CanParse routing and errors. Every test that depends on git facts uses a
/// temporary, non-git workspace so the results do not depend on the branch the tests run on.
/// </summary>
public sealed class GitLabCiParserTests : IDisposable
{
    private readonly GitLabCiParser _parser = new();
    private readonly string _workspace;

    public GitLabCiParserTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "pdk-gitlab-parser-tests", Guid.NewGuid().ToString("N"));
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

    private PipelineParseOptions Options(
        string eventName = "push",
        Dictionary<string, string>? parameters = null,
        Dictionary<string, string>? variables = null) => new()
    {
        WorkspacePath = _workspace,
        EventName = eventName,
        Parameters = parameters ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        Variables = variables ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    };

    private Pipeline Parse(string yaml, PipelineParseOptions? options = null) => _parser.Parse(yaml, options ?? Options());

    private static string SkipReason(Job job) => job.Condition?.Description ?? string.Empty;

    private static bool IsSkipped(Job job) => job.Condition is { Expression: "false", Description: not null };

    // ------------------------------------------------------------------ basics

    [Fact]
    public void Parse_MinimalJob_UsesTestStageAndGitLabProvider()
    {
        var pipeline = Parse("""
            hello:
              script: echo hello
            """);

        pipeline.Provider.Should().Be(PipelineProvider.GitLab);
        pipeline.Name.Should().Be(".gitlab-ci.yml");
        pipeline.Jobs.Should().ContainKey("hello");

        var job = pipeline.Jobs["hello"];
        job.Id.Should().Be("hello");
        job.Name.Should().Be("hello");
        job.Stage.Should().Be("test");
        job.RunsOn.Should().Be("ubuntu-latest");
        job.Container.Should().BeNull();
        job.DependsOn.Should().BeEmpty();
        job.Condition.Should().BeNull();
        job.Steps.Should().ContainSingle().Which.Script.Should().Be("echo hello");
    }

    [Fact]
    public void Parse_EmptyContent_Throws()
    {
        var act = () => _parser.Parse("   ");

        act.Should().Throw<PipelineParseException>().WithMessage("*empty*");
    }

    [Fact]
    public void Parse_NoJobs_Throws()
    {
        var act = () => Parse("""
            stages: [build]
            variables:
              A: b
            """);

        act.Should().Throw<PipelineParseException>().WithMessage("*does not define any job*");
    }

    [Fact]
    public void Parse_YamlSyntaxError_ReportsLineAndColumn()
    {
        var act = () => Parse("""
            build:
              script:
                - "unterminated
            """);

        var ex = act.Should().Throw<PipelineParseException>().Which;
        ex.ErrorCode.Should().Be(ErrorCodes.InvalidYamlSyntax);
        ex.Context!.LineNumber.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Parse_MissingScript_ThrowsMissingRequiredField()
    {
        var act = () => Parse("""
            build:
              stage: build
              image: alpine
            """);

        act.Should().Throw<PipelineParseException>()
            .Which.ErrorCode.Should().Be(ErrorCodes.MissingRequiredField);
    }

    [Fact]
    public void Parse_EmptyJob_Throws()
    {
        var act = () => Parse("""
            build:
            test:
              script: echo
            """);

        act.Should().Throw<PipelineParseException>().WithMessage("*'build' is empty*");
    }

    [Fact]
    public void Parse_UnknownTopLevelScalar_IsWarningNotError()
    {
        var pipeline = Parse("""
            imgae: alpine
            build:
              script: echo
            """);

        pipeline.Jobs.Should().ContainSingle();
        _parser.Warnings.Should().ContainSingle(w => w.Contains("'imgae'") && w.Contains("ignored"));
    }

    [Fact]
    public void Parse_UnknownJobKeyword_IsWarning()
    {
        Parse("""
            build:
              script: echo
              scirpt: typo
            """);

        _parser.Warnings.Should().ContainSingle(w => w.Contains("'scirpt'"));
    }

    // ------------------------------------------------------------------ stages

    [Fact]
    public void Parse_Stages_JobsDependOnEveryJobOfEarlierStages()
    {
        var pipeline = Parse("""
            stages: [build, test, deploy]
            compile:
              stage: build
              script: echo
            lint:
              stage: build
              script: echo
            unit:
              stage: test
              script: echo
            release:
              stage: deploy
              script: echo
            """);

        pipeline.Jobs["compile"].DependsOn.Should().BeEmpty();
        pipeline.Jobs["lint"].DependsOn.Should().BeEmpty();
        pipeline.Jobs["unit"].DependsOn.Should().BeEquivalentTo("compile", "lint");
        pipeline.Jobs["release"].DependsOn.Should().BeEquivalentTo("compile", "lint", "unit");
    }

    [Fact]
    public void Parse_DefaultStages_WhenAbsent()
    {
        var pipeline = Parse("""
            compile:
              stage: build
              script: echo
            unit:
              script: echo
            ship:
              stage: deploy
              script: echo
            prep:
              stage: .pre
              script: echo
            cleanup:
              stage: .post
              script: echo
            """);

        pipeline.Jobs["unit"].DependsOn.Should().BeEquivalentTo("prep", "compile");
        pipeline.Jobs["ship"].DependsOn.Should().BeEquivalentTo("prep", "compile", "unit");
        pipeline.Jobs["cleanup"].DependsOn.Should().BeEquivalentTo("prep", "compile", "unit", "ship");
        pipeline.Jobs["prep"].DependsOn.Should().BeEmpty();
    }

    [Fact]
    public void Parse_PreAndPostStages_AreAlwaysFirstAndLast()
    {
        var pipeline = Parse("""
            stages: [.post, one, .pre]
            a:
              stage: one
              script: echo
            b:
              stage: .pre
              script: echo
            c:
              stage: .post
              script: echo
            """);

        pipeline.Jobs["a"].DependsOn.Should().Equal("b");
        pipeline.Jobs["c"].DependsOn.Should().BeEquivalentTo("a", "b");
    }

    [Fact]
    public void Parse_UndeclaredStage_ThrowsWithDeclaredStages()
    {
        var act = () => Parse("""
            stages: [build, test]
            ship:
              stage: deploy
              script: echo
            """);

        var ex = act.Should().Throw<PipelineParseException>().Which;
        ex.ErrorCode.Should().Be(ErrorCodes.InvalidPipelineStructure);
        ex.Message.Should().Contain("'deploy'");
        ex.Suggestions.Should().Contain(s => s.Contains(".pre, build, test, .post"));
    }

    [Fact]
    public void Parse_StagesNotAList_Throws()
    {
        var act = () => Parse("""
            stages: build
            a:
              script: echo
            """);

        act.Should().Throw<PipelineParseException>().WithMessage("*'stages' must be a list*");
    }

    // ------------------------------------------------------------------ needs

    [Fact]
    public void Parse_Needs_ReplacesStageDependencies()
    {
        var pipeline = Parse("""
            stages: [build, test, deploy]
            compile:
              stage: build
              script: echo
            unit:
              stage: test
              script: echo
            release:
              stage: deploy
              needs: [compile]
              script: echo
            """);

        pipeline.Jobs["release"].DependsOn.Should().Equal("compile");
    }

    [Fact]
    public void Parse_EmptyNeeds_MeansNoDependencies()
    {
        var pipeline = Parse("""
            stages: [build, test]
            compile:
              stage: build
              script: echo
            docs:
              stage: test
              needs: []
              script: echo
            """);

        pipeline.Jobs["docs"].DependsOn.Should().BeEmpty();
        pipeline.Jobs["docs"].Steps.Should().NotContain(s => s.Type == StepType.DownloadArtifact);
    }

    [Fact]
    public void Parse_NeedsObjects_SupportArtifactsAndOptional()
    {
        var pipeline = Parse("""
            stages: [build, test]
            compile:
              stage: build
              script: echo
              artifacts:
                paths: [out/]
            unit:
              stage: test
              needs:
                - job: compile
                  artifacts: false
                - job: does-not-exist
                  optional: true
              script: echo
            """);

        var unit = pipeline.Jobs["unit"];
        unit.DependsOn.Should().Equal("compile");
        unit.Steps.Should().NotContain(s => s.Type == StepType.DownloadArtifact);
    }

    [Fact]
    public void Parse_NeedsWithArtifacts_AddsDownloadStep()
    {
        var pipeline = Parse("""
            compile:
              stage: build
              script: echo
              artifacts:
                paths: [out/]
            unit:
              needs: [{ job: compile }]
              script: echo
            """);

        var download = pipeline.Jobs["unit"].Steps[0];
        download.Type.Should().Be(StepType.DownloadArtifact);
        download.Artifact!.Name.Should().Be("compile");
        download.ContinueOnError.Should().BeTrue();
    }

    [Fact]
    public void Parse_UnknownNeeds_ThrowsMissingDependency()
    {
        var act = () => Parse("""
            unit:
              needs: [compile]
              script: echo
            """);

        var ex = act.Should().Throw<PipelineParseException>().Which;
        ex.ErrorCode.Should().Be(ErrorCodes.MissingDependency);
        ex.Message.Should().Contain("'unit'").And.Contain("'compile'");
    }

    [Fact]
    public void Parse_NeedsHiddenJob_ExplainsHiddenJobs()
    {
        var act = () => Parse("""
            .compile:
              script: echo
            unit:
              needs: [.compile]
              script: echo
            """);

        act.Should().Throw<PipelineParseException>().WithMessage("*hidden job*");
    }

    [Fact]
    public void Parse_CircularNeeds_ThrowsCircularDependency()
    {
        var act = () => Parse("""
            a:
              needs: [b]
              script: echo
            b:
              needs: [a]
              script: echo
            """);

        act.Should().Throw<PipelineParseException>()
            .Which.ErrorCode.Should().Be(ErrorCodes.CircularDependency);
    }

    [Fact]
    public void Parse_SelfNeeds_Throws()
    {
        var act = () => Parse("""
            a:
              needs: [a]
              script: echo
            """);

        act.Should().Throw<PipelineParseException>().Which.ErrorCode.Should().Be(ErrorCodes.SelfDependency);
    }

    [Fact]
    public void Parse_NeedsOnSkippedJob_CascadesSkip()
    {
        var pipeline = Parse("""
            manual_job:
              script: echo
              when: manual
            follower:
              needs: [manual_job]
              script: echo
            optional_follower:
              needs: [{ job: manual_job, optional: true }]
              script: echo
            """);

        IsSkipped(pipeline.Jobs["manual_job"]).Should().BeTrue();
        IsSkipped(pipeline.Jobs["follower"]).Should().BeTrue();
        SkipReason(pipeline.Jobs["follower"]).Should().Contain("needs 'manual_job'");
        IsSkipped(pipeline.Jobs["optional_follower"]).Should().BeFalse();
        pipeline.Jobs["optional_follower"].DependsOn.Should().BeEmpty();
    }

    [Fact]
    public void Parse_SkippedJobs_AreNotStageDependencies()
    {
        var pipeline = Parse("""
            stages: [build, test]
            compile:
              stage: build
              script: echo
            deploy_prep:
              stage: build
              script: echo
              when: manual
            unit:
              stage: test
              script: echo
            """);

        pipeline.Jobs["unit"].DependsOn.Should().Equal("compile");
    }

    [Fact]
    public void Parse_NeedsParallelJob_DependsOnEveryInstance()
    {
        var pipeline = Parse("""
            compile:
              stage: build
              parallel: 2
              script: echo
            unit:
              needs: [compile]
              script: echo
            """);

        pipeline.Jobs["unit"].DependsOn.Should().Equal("compile 1/2", "compile 2/2");
    }

    // ------------------------------------------------------------------ scripts

    [Fact]
    public void Parse_BeforeScript_RunsInTheSameStepAsScript()
    {
        var pipeline = Parse("""
            build:
              before_script:
                - export FOO=1
              script:
                - echo $FOO
                - echo done
              after_script:
                - echo cleanup
            """);

        var steps = pipeline.Jobs["build"].Steps;
        steps.Should().HaveCount(2);
        steps[0].Name.Should().Be("script");
        steps[0].Id.Should().Be("script");
        steps[0].Type.Should().Be(StepType.Script);
        steps[0].Shell.Should().Be("bash");
        steps[0].Script.Should().Be("export FOO=1\necho $FOO\necho done");
        steps[0].Condition.Should().BeNull();
        steps[0].ContinueOnError.Should().BeFalse();

        steps[1].Name.Should().Be("after_script");
        steps[1].Script.Should().Be("echo cleanup");
        steps[1].Condition!.Expression.Should().Be("always()");
        steps[1].ContinueOnError.Should().BeTrue();
    }

    [Fact]
    public void Parse_ScriptAsString_IsOneCommand()
    {
        var pipeline = Parse("""
            build:
              script: |
                echo one
                echo two
            """);

        pipeline.Jobs["build"].Steps[0].Script.Should().Be("echo one\necho two");
    }

    [Fact]
    public void Parse_NestedScriptLists_AreFlattened()
    {
        var pipeline = Parse("""
            build:
              script:
                - [echo a, echo b]
                - echo c
            """);

        pipeline.Jobs["build"].Steps[0].Script.Should().Be("echo a\necho b\necho c");
    }

    [Fact]
    public void Parse_DefaultBeforeScript_AppliesUnlessJobOverrides()
    {
        var pipeline = Parse("""
            default:
              before_script:
                - echo default-before
              after_script:
                - echo default-after
            a:
              script: echo a
            b:
              before_script: [echo own-before]
              after_script: []
              script: echo b
            """);

        pipeline.Jobs["a"].Steps[0].Script.Should().Be("echo default-before\necho a");
        pipeline.Jobs["a"].Steps[1].Script.Should().Be("echo default-after");
        pipeline.Jobs["b"].Steps[0].Script.Should().Be("echo own-before\necho b");
        pipeline.Jobs["b"].Steps.Should().HaveCount(1);
    }

    [Fact]
    public void Parse_DeprecatedGlobalBeforeScriptAndImage_ActAsDefaults()
    {
        var pipeline = Parse("""
            image: node:20
            before_script:
              - npm ci
            test:
              script: npm test
            """);

        pipeline.Jobs["test"].Container.Should().Be("node:20");
        pipeline.Jobs["test"].Steps[0].Script.Should().Be("npm ci\nnpm test");
    }

    // ------------------------------------------------------------------ allow_failure / when

    [Fact]
    public void Parse_AllowFailure_MakesEveryStepContinueOnError()
    {
        var pipeline = Parse("""
            lint:
              script: eslint .
              allow_failure: true
              artifacts:
                paths: [report/]
            """);

        pipeline.Jobs["lint"].Steps.Should().OnlyContain(s => s.ContinueOnError);
    }

    [Fact]
    public void Parse_AllowFailureExitCodes_TreatedAsTrueWithWarning()
    {
        var pipeline = Parse("""
            lint:
              script: eslint .
              allow_failure:
                exit_codes: [137]
            """);

        pipeline.Jobs["lint"].Steps[0].ContinueOnError.Should().BeTrue();
        _parser.Warnings.Should().Contain(w => w.Contains("exit_codes"));
    }

    [Fact]
    public void Parse_WhenManual_IsSkippedWithReason()
    {
        var job = Parse("""
            deploy:
              script: echo
              when: manual
            """).Jobs["deploy"];

        job.Condition!.Expression.Should().Be("false");
        job.Condition.Type.Should().Be(ConditionType.Expression);
        job.Condition.Description.Should().Contain("manual job");
    }

    [Fact]
    public void Parse_WhenNever_IsSkipped()
    {
        var job = Parse("""
            deploy:
              script: echo
              when: never
            """).Jobs["deploy"];

        SkipReason(job).Should().Be("when: never");
    }

    [Fact]
    public void Parse_WhenAlways_UsesAlwaysCondition()
    {
        var job = Parse("""
            cleanup:
              script: echo
              when: always
            """).Jobs["cleanup"];

        job.Condition!.Expression.Should().Be("always()");
        job.Condition.Type.Should().Be(ConditionType.Always);
    }

    [Fact]
    public void Parse_WhenOnFailure_UsesFailureCondition()
    {
        var job = Parse("""
            notify:
              script: echo
              when: on_failure
            """).Jobs["notify"];

        job.Condition!.Expression.Should().Be("failure()");
        job.Condition.Type.Should().Be(ConditionType.Failure);
    }

    [Fact]
    public void Parse_WhenDelayed_RunsImmediatelyWithWarning()
    {
        var job = Parse("""
            rollout:
              script: echo
              when: delayed
              start_in: 30 minutes
            """).Jobs["rollout"];

        job.Condition.Should().BeNull();
        _parser.Warnings.Should().Contain(w => w.Contains("delayed") && w.Contains("immediately"));
    }

    [Fact]
    public void Parse_InvalidWhen_Throws()
    {
        var act = () => Parse("""
            a:
              script: echo
              when: sometimes
            """);

        act.Should().Throw<PipelineParseException>().WithMessage("*'when' must be one of*");
    }

    [Fact]
    public void Parse_BlockingManualJob_Warns()
    {
        Parse("""
            gate:
              script: echo
              when: manual
              allow_failure: false
            """);

        _parser.Warnings.Should().Contain(w => w.Contains("blocking manual job"));
    }

    // ------------------------------------------------------------------ rules

    [Fact]
    public void Parse_Rules_FirstMatchingRuleWins()
    {
        var pipeline = Parse("""
            variables:
              MODE: fast
            build:
              script: echo
              rules:
                - if: $MODE == "slow"
                  when: never
                - if: $MODE == "fast"
                  when: always
                - when: never
            """);

        pipeline.Jobs["build"].Condition!.Expression.Should().Be("always()");
    }

    [Fact]
    public void Parse_Rules_WhenNever_SkipsWithRuleText()
    {
        var job = Parse("""
            build:
              script: echo
              rules:
                - if: $CI_PIPELINE_SOURCE == "push"
                  when: never
                - when: on_success
            """).Jobs["build"];

        SkipReason(job).Should().Be("when: never (if: $CI_PIPELINE_SOURCE == \"push\")");
    }

    [Fact]
    public void Parse_Rules_NoMatch_SkipsJob()
    {
        var job = Parse("""
            build:
              script: echo
              rules:
                - if: $CI_PIPELINE_SOURCE == "schedule"
            """).Jobs["build"];

        SkipReason(job).Should().Be("rules: no rule matched");
    }

    [Fact]
    public void Parse_Rules_MatchDependsOnEvent()
    {
        const string yaml = """
            build:
              script: echo
              rules:
                - if: $CI_PIPELINE_SOURCE == "merge_request_event"
            """;

        IsSkipped(Parse(yaml, Options(eventName: "push")).Jobs["build"]).Should().BeTrue();
        IsSkipped(Parse(yaml, Options(eventName: "pull_request")).Jobs["build"]).Should().BeFalse();
    }

    [Fact]
    public void Parse_Rules_WhenManual_SkipsAndDefaultsAllowFailure()
    {
        var job = Parse("""
            deploy:
              script: echo
              rules:
                - if: $CI_PIPELINE_SOURCE == "push"
                  when: manual
            """).Jobs["deploy"];

        SkipReason(job).Should().StartWith("manual job");
    }

    [Fact]
    public void Parse_Rules_VariablesAndAllowFailure_ApplyToJob()
    {
        var job = Parse("""
            variables:
              TARGET: staging
            deploy:
              script: echo
              rules:
                - if: $CI_PIPELINE_SOURCE == "push"
                  variables:
                    TARGET: production
                    EXTRA: "$TARGET-extra"
                  allow_failure: true
            """).Jobs["deploy"];

        job.Variables["TARGET"].Should().Be("production");
        job.Variables["EXTRA"].Should().Be("production-extra", "rule variables are expanded with the rule's own values in scope");
        job.Steps[0].ContinueOnError.Should().BeTrue();
    }

    [Fact]
    public void Parse_Rules_Exists_ChecksWorkspace()
    {
        File.WriteAllText(Path.Combine(_workspace, "Dockerfile"), "FROM scratch");
        Directory.CreateDirectory(Path.Combine(_workspace, "src"));
        File.WriteAllText(Path.Combine(_workspace, "src", "app.rb"), "puts 1");

        var pipeline = Parse("""
            docker:
              script: echo
              rules:
                - exists: [Dockerfile]
            ruby:
              script: echo
              rules:
                - exists:
                    - "**/*.rb"
            python:
              script: echo
              rules:
                - exists: ["**/*.py"]
            missing:
              script: echo
              rules:
                - exists: [Missingfile]
            """);

        IsSkipped(pipeline.Jobs["docker"]).Should().BeFalse();
        IsSkipped(pipeline.Jobs["ruby"]).Should().BeFalse();
        IsSkipped(pipeline.Jobs["python"]).Should().BeTrue();
        IsSkipped(pipeline.Jobs["missing"]).Should().BeTrue();
    }

    [Fact]
    public void Parse_Rules_Changes_AlwaysMatch()
    {
        var pipeline = Parse("""
            docs:
              script: echo
              rules:
                - changes: [docs/**/*]
            skipped:
              script: echo
              rules:
                - changes: [docs/**/*]
                  when: never
            """);

        IsSkipped(pipeline.Jobs["docs"]).Should().BeFalse();
        IsSkipped(pipeline.Jobs["skipped"]).Should().BeTrue();
    }

    [Fact]
    public void Parse_Rules_UseJobVariables()
    {
        var pipeline = Parse("""
            build:
              variables:
                ENABLED: "yes"
              script: echo
              rules:
                - if: $ENABLED == "yes"
            """);

        IsSkipped(pipeline.Jobs["build"]).Should().BeFalse();
    }

    [Fact]
    public void Parse_Rules_InvalidExpression_Throws()
    {
        var act = () => Parse("""
            build:
              script: echo
              rules:
                - if: $CI_COMMIT_BRANCH ==
            """);

        var ex = act.Should().Throw<PipelineParseException>().Which;
        ex.Message.Should().Contain("invalid rules expression");
        ex.Context!.JobName.Should().Be("build");
    }

    [Fact]
    public void Parse_RulesWithOnly_Throws()
    {
        var act = () => Parse("""
            build:
              script: echo
              only: [main]
              rules:
                - when: always
            """);

        act.Should().Throw<PipelineParseException>().WithMessage("*'rules' together with 'only'/'except'*");
    }

    // ------------------------------------------------------------------ only / except

    [Fact]
    public void Parse_OnlyBranches_RunsForPushOutsideGit()
    {
        var pipeline = Parse("""
            build:
              script: echo
              only: [branches]
            tagged:
              script: echo
              only: [tags]
            """);

        IsSkipped(pipeline.Jobs["build"]).Should().BeFalse();
        IsSkipped(pipeline.Jobs["tagged"]).Should().BeTrue();
        SkipReason(pipeline.Jobs["tagged"]).Should().Contain("only:");
    }

    [Fact]
    public void Parse_OnlyRefName_ComparesWithCurrentRef()
    {
        var job = Parse("""
            build:
              script: echo
              only:
                - main
                - /^release-.*$/
            """).Jobs["build"];

        IsSkipped(job).Should().BeTrue("the temporary workspace has no branch");
        SkipReason(job).Should().Contain("is not selected by only: [main, /^release-.*$/]");
    }

    [Fact]
    public void Parse_OnlyMergeRequests_DependsOnEvent()
    {
        const string yaml = """
            review:
              script: echo
              only: [merge_requests]
            """;

        IsSkipped(Parse(yaml, Options(eventName: "push")).Jobs["review"]).Should().BeTrue();
        IsSkipped(Parse(yaml, Options(eventName: "pull_request")).Jobs["review"]).Should().BeFalse();
    }

    [Fact]
    public void Parse_ExceptSchedules_ExcludesScheduledRuns()
    {
        const string yaml = """
            build:
              script: echo
              except: [schedules]
            """;

        IsSkipped(Parse(yaml, Options(eventName: "push")).Jobs["build"]).Should().BeFalse();
        var scheduled = Parse(yaml, Options(eventName: "schedule")).Jobs["build"];
        IsSkipped(scheduled).Should().BeTrue();
        SkipReason(scheduled).Should().Contain("except:");
    }

    [Fact]
    public void Parse_OnlyVariables_EvaluatesExpressions()
    {
        const string yaml = """
            deploy:
              script: echo
              only:
                variables:
                  - $DEPLOY == "yes"
            """;

        IsSkipped(Parse(yaml).Jobs["deploy"]).Should().BeTrue();
        var enabled = Parse(yaml, Options(parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["DEPLOY"] = "yes" }));
        IsSkipped(enabled.Jobs["deploy"]).Should().BeFalse();
    }

    [Fact]
    public void Parse_OnlyRefsAndVariables_MustBothMatch()
    {
        var pipeline = Parse("""
            variables:
              FLAG: "on"
            deploy:
              script: echo
              only:
                refs: [branches]
                variables: ["$FLAG == \"on\""]
            blocked:
              script: echo
              only:
                refs: [branches]
                variables: ["$FLAG == \"off\""]
            """);

        IsSkipped(pipeline.Jobs["deploy"]).Should().BeFalse();
        IsSkipped(pipeline.Jobs["blocked"]).Should().BeTrue();
    }

    [Fact]
    public void Parse_ExceptVariables_ExcludeWhenAnyMatches()
    {
        var job = Parse("""
            variables:
              SKIP: "1"
            build:
              script: echo
              except:
                variables: ["$SKIP == \"1\"", "$OTHER"]
            """).Jobs["build"];

        IsSkipped(job).Should().BeTrue();
    }

    // ------------------------------------------------------------------ extends

    [Fact]
    public void Parse_Extends_DeepMergesTemplates()
    {
        var pipeline = Parse("""
            .base:
              image: alpine
              variables:
                A: base
                B: base
              script: [echo base]
              artifacts:
                paths: [out/]
                expire_in: 1 day
            build:
              extends: .base
              variables:
                B: job
              script: [echo job]
              artifacts:
                expire_in: 2 days
            """);

        var job = pipeline.Jobs["build"];
        job.Container.Should().Be("alpine");
        job.Variables["A"].Should().Be("base");
        job.Variables["B"].Should().Be("job");
        job.Steps[0].Script.Should().Be("echo job", "arrays are replaced, not merged");
        job.Steps.Last().Artifact!.Options.RetentionDays.Should().Be(2);
        job.Steps.Last().Artifact!.Patterns.Should().Equal("out");
        pipeline.Jobs.Should().NotContainKey(".base");
    }

    [Fact]
    public void Parse_Extends_MultipleParents_LaterWins()
    {
        var job = Parse("""
            .a:
              variables: { URL: a, ONLY_A: a }
              tags: [a]
            .b:
              variables: { URL: b }
            job:
              extends: [.a, .b]
              script: echo
            """).Jobs["job"];

        job.Variables["URL"].Should().Be("b");
        job.Variables["ONLY_A"].Should().Be("a");
    }

    [Fact]
    public void Parse_Extends_Chains()
    {
        var job = Parse("""
            .root:
              image: alpine
              variables: { LEVEL: root }
            .middle:
              extends: .root
              variables: { LEVEL: middle, MID: "yes" }
            leaf:
              extends: .middle
              script: echo
            """).Jobs["leaf"];

        job.Container.Should().Be("alpine");
        job.Variables["LEVEL"].Should().Be("middle");
        job.Variables["MID"].Should().Be("yes");
    }

    [Fact]
    public void Parse_Extends_VisibleJob()
    {
        var pipeline = Parse("""
            build:
              image: alpine
              script: echo build
            build-debug:
              extends: build
              variables: { DEBUG: "1" }
            """);

        pipeline.Jobs["build-debug"].Container.Should().Be("alpine");
        pipeline.Jobs["build-debug"].Steps[0].Script.Should().Be("echo build");
    }

    [Fact]
    public void Parse_Extends_Unknown_Throws()
    {
        var act = () => Parse("""
            build:
              extends: .nope
              script: echo
            """);

        act.Should().Throw<PipelineParseException>().WithMessage("*extends '.nope', which is not defined*");
    }

    [Fact]
    public void Parse_Extends_Cycle_Throws()
    {
        var act = () => Parse("""
            .a:
              extends: .b
            .b:
              extends: .a
            job:
              extends: .a
              script: echo
            """);

        act.Should().Throw<PipelineParseException>().Which.ErrorCode.Should().Be(ErrorCodes.CircularDependency);
    }

    // ------------------------------------------------------------------ !reference and anchors

    [Fact]
    public void Parse_ReferenceTags_SpliceScriptsAndResolveNested()
    {
        var pipeline = Parse("""
            .setup:
              script:
                - echo setup
            .more:
              script:
                - !reference [.setup, script]
                - echo more
              variables:
                SHARED: "1"
            build:
              variables: !reference [.more, variables]
              script:
                - !reference [.more, script]
                - echo build
            """);

        var job = pipeline.Jobs["build"];
        job.Steps[0].Script.Should().Be("echo setup\necho more\necho build");
        job.Variables["SHARED"].Should().Be("1");
    }

    [Fact]
    public void Parse_ReferenceTag_ForRules()
    {
        var pipeline = Parse("""
            .never-on-push:
              rules:
                - if: $CI_PIPELINE_SOURCE == "push"
                  when: never
                - when: always
            build:
              script: echo
              rules: !reference [.never-on-push, rules]
            """);

        IsSkipped(pipeline.Jobs["build"]).Should().BeTrue();
    }

    [Fact]
    public void Parse_ReferenceTag_Unknown_Throws()
    {
        var act = () => Parse("""
            build:
              script: !reference [.missing, script]
            """);

        act.Should().Throw<PipelineParseException>().WithMessage("*!reference [.missing, script]*not defined*");
    }

    [Fact]
    public void Parse_ReferenceTag_Cycle_Throws()
    {
        var act = () => Parse("""
            .a:
              script: !reference [.b, script]
            .b:
              script: !reference [.a, script]
            job:
              script: !reference [.a, script]
            """);

        act.Should().Throw<PipelineParseException>().WithMessage("*Circular !reference*");
    }

    [Fact]
    public void Parse_AnchorsAndMergeKeys_Work()
    {
        var pipeline = Parse("""
            .defaults: &defaults
              image: alpine
              variables:
                FROM_ANCHOR: "1"
            build:
              <<: *defaults
              script: echo
              variables:
                OWN: "2"
            test:
              <<: *defaults
              image: node:20
              script: echo
            """);

        pipeline.Jobs["build"].Container.Should().Be("alpine");
        pipeline.Jobs["build"].Variables.Should().ContainKey("OWN").And.NotContainKey("FROM_ANCHOR", "merge keys are shallow, as in YAML");
        pipeline.Jobs["test"].Container.Should().Be("node:20", "explicit keys win over merged ones");
        pipeline.Jobs["test"].Variables["FROM_ANCHOR"].Should().Be("1");
    }

    // ------------------------------------------------------------------ include

    [Fact]
    public async Task ParseFile_IncludeLocal_MergesWithIncludingFileWinning()
    {
        Directory.CreateDirectory(Path.Combine(_workspace, "ci"));
        await File.WriteAllTextAsync(Path.Combine(_workspace, "ci", "common.yml"), """
            variables:
              FROM_INCLUDE: "1"
              SHARED: include
            .shared:
              script: [echo shared]
            included_job:
              stage: test
              script: echo included
              image: alpine
            """);
        var main = Path.Combine(_workspace, ".gitlab-ci.yml");
        await File.WriteAllTextAsync(main, """
            include:
              - local: ci/common.yml
              - ci/common.yml
              - local: /ci/common.yml
            variables:
              SHARED: main
            included_job:
              image: node:20
            build:
              script: !reference [.shared, script]
            """);

        var pipeline = await _parser.ParseFile(main, Options());

        pipeline.Variables["FROM_INCLUDE"].Should().Be("1");
        pipeline.Variables["SHARED"].Should().Be("main");
        pipeline.Jobs["included_job"].Container.Should().Be("node:20");
        pipeline.Jobs["included_job"].Steps[0].Script.Should().Be("echo included");
        pipeline.Jobs["build"].Steps[0].Script.Should().Be("echo shared");
    }

    [Fact]
    public async Task ParseFile_IncludeRelativeToIncludingFile_IsFound()
    {
        var dir = Path.Combine(_workspace, "pipelines");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "part.yml"), "part:\n  script: echo part\n");
        var main = Path.Combine(dir, "main.yml");
        await File.WriteAllTextAsync(main, "include: part.yml\nmain:\n  script: echo main\n");

        var pipeline = await _parser.ParseFile(main, Options());

        pipeline.Jobs.Keys.Should().BeEquivalentTo("part", "main");
    }

    [Fact]
    public void Parse_IncludeMissingLocal_Throws()
    {
        var act = () => Parse("""
            include:
              - local: missing.yml
            build:
              script: echo
            """);

        act.Should().Throw<PipelineParseException>().Which.ErrorCode.Should().Be(ErrorCodes.FileNotFound);
    }

    [Fact]
    public void Parse_IncludeRemoteOrTemplate_IsWarning()
    {
        var pipeline = Parse("""
            include:
              - remote: https://example.com/ci.yml
              - template: Security/SAST.gitlab-ci.yml
              - project: group/repo
                file: ci.yml
            build:
              script: echo
            """);

        pipeline.Jobs.Should().ContainKey("build");
        _parser.Warnings.Should().HaveCount(3).And.OnlyContain(w => w.Contains("include:"));
    }

    [Fact]
    public async Task ParseFile_IncludeRules_SkipUnmatchedIncludes()
    {
        await File.WriteAllTextAsync(Path.Combine(_workspace, "sched.yml"), "sched_job:\n  script: echo\n");
        await File.WriteAllTextAsync(Path.Combine(_workspace, "push.yml"), "push_job:\n  script: echo\n");
        var main = Path.Combine(_workspace, ".gitlab-ci.yml");
        await File.WriteAllTextAsync(main, """
            include:
              - local: sched.yml
                rules:
                  - if: $CI_PIPELINE_SOURCE == "schedule"
              - local: push.yml
                rules:
                  - if: $CI_PIPELINE_SOURCE == "push"
            build:
              script: echo
            """);

        var pipeline = await _parser.ParseFile(main, Options());

        pipeline.Jobs.Keys.Should().BeEquivalentTo("push_job", "build");
    }

    // ------------------------------------------------------------------ image / default

    [Fact]
    public void Parse_Image_Forms()
    {
        var pipeline = Parse("""
            variables:
              TAG: "8.0"
            default:
              image: mcr.microsoft.com/dotnet/sdk:$TAG
            plain:
              script: echo
            mapping:
              image:
                name: node:20
                entrypoint: [""]
              script: echo
            none:
              image: ""
              script: echo
            """);

        pipeline.Jobs["plain"].Container.Should().Be("mcr.microsoft.com/dotnet/sdk:8.0");
        pipeline.Jobs["mapping"].Container.Should().Be("node:20");
        pipeline.Jobs["none"].Container.Should().BeNull();
        pipeline.Jobs.Values.Should().OnlyContain(j => j.RunsOn == "ubuntu-latest");
    }

    [Fact]
    public void Parse_Default_TimeoutAndArtifacts_Apply()
    {
        var pipeline = Parse("""
            default:
              timeout: 1h 30m
              artifacts:
                paths: [dist/]
            build:
              script: echo
            quick:
              timeout: 10 minutes
              script: echo
              inherit:
                default: false
            """);

        pipeline.Jobs["build"].Timeout.Should().Be(TimeSpan.FromMinutes(90));
        pipeline.Jobs["build"].Steps.Should().Contain(s => s.Type == StepType.UploadArtifact);
        pipeline.Jobs["quick"].Timeout.Should().Be(TimeSpan.FromMinutes(10));
        pipeline.Jobs["quick"].Steps.Should().NotContain(s => s.Type == StepType.UploadArtifact);
    }

    [Fact]
    public void Parse_InheritDefaultList_LimitsInheritedKeys()
    {
        var job = Parse("""
            default:
              image: alpine
              timeout: 2h
            build:
              script: echo
              inherit:
                default: [image]
            """).Jobs["build"];

        job.Container.Should().Be("alpine");
        job.Timeout.Should().BeNull();
    }

    [Fact]
    public void Parse_Services_Warn()
    {
        Parse("""
            default:
              services: [postgres:16]
            build:
              services: [redis]
              script: echo
            """);

        _parser.Warnings.Should().Contain(w => w.Contains("default:services"));
        _parser.Warnings.Should().Contain(w => w.Contains("Job 'build'") && w.Contains("services"));
    }

    // ------------------------------------------------------------------ parallel

    [Fact]
    public void Parse_ParallelCount_ClonesJobWithNodeVariables()
    {
        var pipeline = Parse("""
            test:
              parallel: 3
              script: echo
            """);

        pipeline.Jobs.Keys.Should().Equal("test 1/3", "test 2/3", "test 3/3");
        pipeline.Jobs["test 2/3"].Variables["CI_NODE_INDEX"].Should().Be("2");
        pipeline.Jobs["test 2/3"].Variables["CI_NODE_TOTAL"].Should().Be("3");
        pipeline.Jobs["test 2/3"].Name.Should().Be("test 2/3");
        pipeline.Jobs["test 2/3"].Matrix.Should().BeNull();
    }

    [Fact]
    public void Parse_ParallelMatrix_ProducesCartesianProduct()
    {
        var pipeline = Parse("""
            deploy:
              script: echo
              parallel:
                matrix:
                  - PROVIDER: aws
                    STACK: [monitoring, app]
                  - PROVIDER: [gcp, azure]
                    STACK: data
            """);

        pipeline.Jobs.Keys.Should().Equal(
            "deploy: [aws, monitoring]",
            "deploy: [aws, app]",
            "deploy: [gcp, data]",
            "deploy: [azure, data]");

        var job = pipeline.Jobs["deploy: [aws, app]"];
        job.Matrix.Should().Equal(new Dictionary<string, string> { ["PROVIDER"] = "aws", ["STACK"] = "app" });
        job.Variables["PROVIDER"].Should().Be("aws");
        job.Variables["STACK"].Should().Be("app");
        job.Variables["CI_NODE_INDEX"].Should().Be("2");
        job.Variables["CI_NODE_TOTAL"].Should().Be("4");
    }

    [Fact]
    public void Parse_ParallelMatrix_RulesSeeMatrixVariables()
    {
        var pipeline = Parse("""
            deploy:
              script: echo
              parallel:
                matrix:
                  - TARGET: [dev, prod]
              rules:
                - if: $TARGET == "prod"
                  when: manual
                - when: on_success
            """);

        IsSkipped(pipeline.Jobs["deploy: [dev]"]).Should().BeFalse();
        IsSkipped(pipeline.Jobs["deploy: [prod]"]).Should().BeTrue();
    }

    [Fact]
    public void Parse_InvalidParallel_Throws()
    {
        var act = () => Parse("""
            test:
              parallel: many
              script: echo
            """);

        act.Should().Throw<PipelineParseException>().WithMessage("*'parallel' must be a number*");
    }

    // ------------------------------------------------------------------ artifacts / dependencies

    [Fact]
    public void Parse_Artifacts_BecomeUploadStep()
    {
        var job = Parse("""
            variables:
              BUILD_NAME: nightly
            build:
              script: echo
              artifacts:
                name: "$BUILD_NAME-$CI_JOB_NAME"
                paths:
                  - dist/
                  - ./bin/app
                exclude:
                  - dist/**/*.map
                expire_in: 1 week
                when: always
                reports:
                  junit: results.xml
            """).Jobs["build"];

        var upload = job.Steps.Last();
        upload.Type.Should().Be(StepType.UploadArtifact);
        upload.Name.Should().Be("artifacts");
        upload.Condition!.Expression.Should().Be("always()");
        upload.Artifact!.Name.Should().Be("nightly-build");
        upload.Artifact.Patterns.Should().Equal("dist", "bin/app", "!dist/**/*.map");
        upload.Artifact.Options.IfNoFilesFound.Should().Be(PDK.Core.Artifacts.IfNoFilesFound.Warn);
        upload.Artifact.Options.RetentionDays.Should().Be(7);
    }

    [Fact]
    public void Parse_ArtifactsWithoutPaths_NoUploadStep()
    {
        var job = Parse("""
            test:
              script: echo
              artifacts:
                reports:
                  junit: results.xml
            """).Jobs["test"];

        job.Steps.Should().NotContain(s => s.Type == StepType.UploadArtifact);
    }

    [Fact]
    public void Parse_ArtifactsWhenOnFailure_UsesFailureCondition()
    {
        var upload = Parse("""
            test:
              script: echo
              artifacts:
                paths: [logs/]
                when: on_failure
            """).Jobs["test"].Steps.Last();

        upload.Condition!.Expression.Should().Be("failure()");
    }

    [Fact]
    public void Parse_Dependencies_DownloadOnlyListedArtifacts()
    {
        var pipeline = Parse("""
            stages: [build, test]
            compile:
              stage: build
              script: echo
              artifacts:
                paths: [out/]
            assets:
              stage: build
              script: echo
              artifacts:
                name: static
                paths: [public/]
            all:
              stage: test
              script: echo
            some:
              stage: test
              dependencies: [assets]
              script: echo
            none:
              stage: test
              dependencies: []
              script: echo
            """);

        pipeline.Jobs["all"].Steps.Where(s => s.Type == StepType.DownloadArtifact).Select(s => s.Artifact!.Name)
            .Should().Equal("compile", "static");
        pipeline.Jobs["some"].Steps.Where(s => s.Type == StepType.DownloadArtifact).Select(s => s.Artifact!.Name)
            .Should().Equal("static");
        pipeline.Jobs["some"].DependsOn.Should().BeEquivalentTo("compile", "assets");
        pipeline.Jobs["none"].Steps.Should().NotContain(s => s.Type == StepType.DownloadArtifact);
    }

    [Fact]
    public void Parse_UnknownDependencies_Throws()
    {
        var act = () => Parse("""
            test:
              dependencies: [nope]
              script: echo
            """);

        act.Should().Throw<PipelineParseException>().Which.ErrorCode.Should().Be(ErrorCodes.MissingDependency);
    }

    // ------------------------------------------------------------------ variables

    [Fact]
    public void Parse_Variables_ExpandNestedReferencesAndForms()
    {
        var pipeline = Parse("""
            variables:
              BASE: /opt
              FULL: "$BASE/bin"
              DESCRIBED:
                value: described
                description: "A documented variable"
                options: [described, other]
              RAW:
                value: "$BASE/raw"
                expand: false
              ESCAPED: "cost $$5"
            build:
              variables:
                LOCAL: "${FULL}/local"
                BASE: /override
              script: echo
            """);

        pipeline.Variables["FULL"].Should().Be("/opt/bin");
        pipeline.Variables["DESCRIBED"].Should().Be("described");
        pipeline.Variables["RAW"].Should().Be("$BASE/raw");
        pipeline.Variables["ESCAPED"].Should().Be("cost $5");

        var job = pipeline.Jobs["build"];
        job.Variables["LOCAL"].Should().Be("/opt/bin/local");
        job.Variables["BASE"].Should().Be("/override");
    }

    [Fact]
    public void Parse_Variables_ReferencingJobScope_ExpandPerJob()
    {
        var pipeline = Parse("""
            variables:
              ARTIFACT: "$CI_JOB_NAME-$CI_JOB_STAGE"
            build:
              stage: build
              script: echo
            test:
              script: echo
            """);

        pipeline.Variables["ARTIFACT"].Should().Be("$CI_JOB_NAME-$CI_JOB_STAGE", "job-scoped names are expanded per job");
        pipeline.Jobs["build"].Variables["ARTIFACT"].Should().Be("build-build");
        pipeline.Jobs["test"].Variables["ARTIFACT"].Should().Be("test-test");
    }

    [Fact]
    public void Parse_Parameters_OverrideAndAddPipelineVariables()
    {
        var options = Options(parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["MODE"] = "release",
            ["NEW_VAR"] = "added"
        });

        var pipeline = Parse("""
            variables:
              MODE: debug
              DERIVED: "$MODE-build"
            build:
              script: echo
              rules:
                - if: $MODE == "release" && $NEW_VAR == "added"
            """, options);

        pipeline.Variables["MODE"].Should().Be("release");
        pipeline.Variables["DERIVED"].Should().Be("release-build");
        pipeline.Variables["NEW_VAR"].Should().Be("added");
        IsSkipped(pipeline.Jobs["build"]).Should().BeFalse();
    }

    [Fact]
    public void Parse_CliVariables_AreVisibleToRulesAndOverrideDeclared()
    {
        var options = Options(variables: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["MODE"] = "release" });

        var pipeline = Parse("""
            variables:
              MODE: debug
            build:
              script: echo
              rules:
                - if: $MODE == "release"
            """, options);

        pipeline.Variables["MODE"].Should().Be("release");
        IsSkipped(pipeline.Jobs["build"]).Should().BeFalse();
    }

    [Fact]
    public void Parse_Variables_NotAMapping_Throws()
    {
        var act = () => Parse("""
            variables:
              - A=1
            build:
              script: echo
            """);

        act.Should().Throw<PipelineParseException>().WithMessage("*'variables' must be a mapping*");
    }

    [Fact]
    public void Parse_ScriptsAreNotRewritten()
    {
        var job = Parse("""
            variables:
              NAME: world
            build:
              script:
                - echo "hello $NAME ${NAME} $(date)"
            """).Jobs["build"];

        job.Steps[0].Script.Should().Be("echo \"hello $NAME ${NAME} $(date)\"");
    }

    // ------------------------------------------------------------------ workflow

    [Fact]
    public void Parse_WorkflowRules_NoMatch_SkipsEveryJob()
    {
        var pipeline = Parse("""
            workflow:
              rules:
                - if: $CI_PIPELINE_SOURCE == "schedule"
            a:
              script: echo
            b:
              script: echo
              when: always
            """);

        pipeline.Jobs.Values.Should().OnlyContain(j => IsSkipped(j));
        SkipReason(pipeline.Jobs["b"]).Should().StartWith("workflow rules");
    }

    [Fact]
    public void Parse_WorkflowRules_WhenNever_SkipsEveryJob()
    {
        var pipeline = Parse("""
            workflow:
              rules:
                - if: $CI_PIPELINE_SOURCE == "push"
                  when: never
                - when: always
            a:
              script: echo
            """);

        SkipReason(pipeline.Jobs["a"]).Should().Contain("workflow rules").And.Contain("when: never");
    }

    [Fact]
    public void Parse_WorkflowRules_MatchRunsAndSetsVariables()
    {
        var pipeline = Parse("""
            workflow:
              name: "Pipeline for $CI_PIPELINE_SOURCE"
              rules:
                - if: $CI_PIPELINE_SOURCE == "push"
                  variables:
                    DEPLOY_ENV: staging
                - when: always
            variables:
              DEPLOY_ENV: none
            a:
              script: echo
            """);

        pipeline.Name.Should().Be("Pipeline for push");
        pipeline.Variables["DEPLOY_ENV"].Should().Be("staging");
        IsSkipped(pipeline.Jobs["a"]).Should().BeFalse();
    }

    // ------------------------------------------------------------------ trigger / hidden / misc

    [Fact]
    public void Parse_TriggerJob_BecomesUnknownStepWithWarning()
    {
        var pipeline = Parse("""
            stages: [build, deploy]
            build:
              stage: build
              script: echo
            downstream:
              stage: deploy
              trigger:
                project: group/other
                branch: main
            child:
              stage: deploy
              trigger:
                include: child.yml
            """);

        var downstream = pipeline.Jobs["downstream"];
        downstream.Steps.Should().ContainSingle();
        downstream.Steps[0].Type.Should().Be(StepType.Unknown);
        downstream.Steps[0].Name.Should().Be("Trigger downstream pipeline");
        downstream.Steps[0].ActionReference.Should().Be("trigger");
        downstream.Steps[0].With["project"].Should().Be("group/other");
        downstream.DependsOn.Should().Equal("build");
        pipeline.Jobs["child"].Steps[0].Type.Should().Be(StepType.Unknown);
        _parser.Warnings.Should().Contain(w => w.Contains("'downstream' triggers 'group/other'"));
    }

    [Fact]
    public void Parse_HiddenJobs_AreNeverExecuted()
    {
        var pipeline = Parse("""
            .hidden:
              script: echo hidden
            .also-hidden: plain scalar
            visible:
              script: echo
            """);

        pipeline.Jobs.Keys.Should().Equal("visible");
        _parser.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Parse_Timeout_Forms()
    {
        var pipeline = Parse("""
            a:
              script: echo
              timeout: 1h 30m
            b:
              script: echo
              timeout: 90 minutes
            c:
              script: echo
              timeout: 2h
            d:
              script: echo
              timeout: forever
            """);

        pipeline.Jobs["a"].Timeout.Should().Be(TimeSpan.FromMinutes(90));
        pipeline.Jobs["b"].Timeout.Should().Be(TimeSpan.FromMinutes(90));
        pipeline.Jobs["c"].Timeout.Should().Be(TimeSpan.FromHours(2));
        pipeline.Jobs["d"].Timeout.Should().BeNull();
        _parser.Warnings.Should().Contain(w => w.Contains("'forever'"));
    }

    [Fact]
    public void Parse_IgnoredKeywords_WarnOnlyWhenTheyChangeBehaviour()
    {
        Parse("""
            build:
              script: echo
              retry: 2
              tags: [docker]
              cache:
                paths: [node_modules/]
              interruptible: true
              resource_group: prod
              environment: production
              coverage: '/Total: \d+/'
              release:
                tag_name: v1
                description: release
              secrets:
                DB:
                  vault: db/password
              id_tokens:
                TOKEN:
                  aud: https://example.com
            """);

        _parser.Warnings.Should().HaveCount(3);
        _parser.Warnings.Should().Contain(w => w.Contains("'release'"));
        _parser.Warnings.Should().Contain(w => w.Contains("'secrets'"));
        _parser.Warnings.Should().Contain(w => w.Contains("'id_tokens'"));
    }

    [Fact]
    public void Parse_WarningsResetBetweenParses()
    {
        Parse("build:\n  script: echo\n  services: [redis]\n");
        _parser.Warnings.Should().NotBeEmpty();

        Parse("build:\n  script: echo\n");
        _parser.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Parse_SpecHeaderDocument_IsSkipped()
    {
        var pipeline = Parse("""
            spec:
              inputs:
                environment:
                  default: staging
            ---
            build:
              script: echo
            """);

        pipeline.Jobs.Keys.Should().Equal("build");
    }

    // ------------------------------------------------------------------ ParseFile / CanParse

    [Fact]
    public async Task ParseFile_MissingFile_Throws()
    {
        var act = async () => await _parser.ParseFile(Path.Combine(_workspace, "nope.yml"));

        await act.Should().ThrowAsync<PipelineParseException>().WithMessage("*not found*");
    }

    [Fact]
    public async Task ParseFile_UsesFileNameAsPipelineName()
    {
        var path = Path.Combine(_workspace, ".gitlab-ci.yml");
        await File.WriteAllTextAsync(path, "build:\n  script: echo\n");

        var pipeline = await _parser.ParseFile(path);

        pipeline.Name.Should().Be(".gitlab-ci.yml");
    }

    [Theory]
    [InlineData(".gitlab-ci.yml", "not: even valid gitlab\n", true)]
    [InlineData(".gitlab-ci.yaml", "build:\n  script: echo\n", true)]
    [InlineData("pipeline.yml", "stages: [build, test]\nbuild:\n  stage: build\n  script: echo\n", true)]
    [InlineData("pipeline.yml", "include:\n  - local: other.yml\n", true)]
    [InlineData("pipeline.yml", "build:\n  script: echo\n", true)]
    [InlineData("pipeline.yml", "variables:\n  A: b\nrun:\n  trigger:\n    project: x\n", true)]
    [InlineData("pipeline.yml", "name: CI\non: push\njobs:\n  build:\n    runs-on: ubuntu-latest\n    steps:\n      - run: echo\n", false)]
    [InlineData("pipeline.yml", "jobs:\n  build:\n    runs-on: ubuntu-latest\n    steps:\n      - run: echo\n", false)]
    [InlineData("pipeline.yml", "trigger: [main]\npool:\n  vmImage: ubuntu-latest\nsteps:\n  - script: echo\n", false)]
    [InlineData("pipeline.yml", "stages:\n  - stage: Build\n    jobs:\n      - job: A\n        steps:\n          - script: echo\n", false)]
    [InlineData("pipeline.yml", "jobs:\n  - job: A\n    steps:\n      - script: echo\n", false)]
    [InlineData("pipeline.yml", "variables:\n  A: b\n", false)]
    [InlineData("pipeline.txt", "build:\n  script: echo\n", false)]
    [InlineData("pipeline.yml", "- just\n- a list\n", false)]
    [InlineData("pipeline.yml", "build: [unbalanced\n", false)]
    public void CanParse_IsPrecise(string fileName, string content, bool expected)
    {
        var path = Path.Combine(_workspace, fileName);
        File.WriteAllText(path, content);

        _parser.CanParse(path).Should().Be(expected);
    }

    [Fact]
    public void CanParse_MissingOrEmptyPath_ReturnsFalse()
    {
        _parser.CanParse(string.Empty).Should().BeFalse();
        _parser.CanParse(Path.Combine(_workspace, "missing.yml")).Should().BeFalse();
    }

    [Fact]
    public void CanParse_GitLabFile_IsNotClaimedByOtherParsers()
    {
        var path = Path.Combine(_workspace, "ci.yml");
        File.WriteAllText(path, """
            variables:
              CONFIG: Release
            build:
              script: dotnet build -c $CONFIG
            """);

        _parser.CanParse(path).Should().BeTrue();
        new GitHubActionsParser().CanParse(path).Should().BeFalse();
        new AzureDevOpsParser().CanParse(path).Should().BeFalse();
    }
}
