using System.Globalization;
using PDK.Core.Models;

namespace PDK.Core.Expressions;

/// <summary>
/// Builds <see cref="ExpressionContext"/> instances and the environment variables exported to steps
/// for GitHub Actions and Azure Pipelines jobs.
/// </summary>
public static class PipelineContextBuilder
{
    /// <summary>Maps a provider to its expression dialect.</summary>
    public static ExpressionSyntax SyntaxFor(PipelineProvider provider) =>
        provider == PipelineProvider.AzureDevOps ? ExpressionSyntax.Azure : ExpressionSyntax.GitHub;

    /// <summary>
    /// Builds the job-level context. Step-level data (<c>steps</c>, step <c>env</c>) is layered on with
    /// <see cref="ForStep"/>.
    /// </summary>
    public static ExpressionContext BuildJobContext(Pipeline pipeline, Job job, JobRuntimeInfo info)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(info);

        var syntax = SyntaxFor(info.Provider);
        var context = new ExpressionContext(syntax) { Workspace = info.Workspace };

        if (syntax == ExpressionSyntax.Azure)
        {
            BuildAzureRoots(context, pipeline, job, info);
        }
        else
        {
            BuildGitHubRoots(context, pipeline, job, info);
        }

        if (info.Provider == PipelineProvider.GitLab)
        {
            // GitLab conditions are literal booleans decided at parse time (rules/only/except), so the GitHub
            // roots are only a vehicle for always()/failure(); `env` must show the GitLab environment.
            context.SetRoot("env", ExpressionValue.FromStrings(GitLabEnvironment(pipeline, job, info)));
        }

        context.Status = StatusFromNeeds(info.NeedsResults, syntax, info.Provider);

        return context;
    }

    /// <summary>
    /// Derives the status seen by a job's condition from the results of the jobs it depends on.
    /// GitHub skips a job whose dependency was skipped (unless it uses <c>always()</c>); Azure treats
    /// a skipped dependency as succeeded.
    /// </summary>
    public static ExpressionJobStatus StatusFromNeeds(IReadOnlyDictionary<string, string> needsResults, ExpressionSyntax syntax) =>
        StatusFromNeeds(needsResults, syntax, syntax == ExpressionSyntax.Azure ? PipelineProvider.AzureDevOps : PipelineProvider.GitHub);

    /// <summary>
    /// Derives the status seen by a job's condition from the results of the jobs it depends on, for a provider.
    /// GitHub skips a job whose dependency was skipped (unless it uses <c>always()</c>); Azure and GitLab treat
    /// a skipped dependency as succeeded (a GitLab job that was not created, was manual or did not match its rules
    /// never blocks later stages).
    /// </summary>
    public static ExpressionJobStatus StatusFromNeeds(IReadOnlyDictionary<string, string> needsResults, ExpressionSyntax syntax, PipelineProvider provider)
    {
        ArgumentNullException.ThrowIfNull(needsResults);

        if (needsResults.Values.Any(r => string.Equals(r, "cancelled", StringComparison.OrdinalIgnoreCase)))
        {
            return ExpressionJobStatus.Cancelled;
        }

        if (needsResults.Values.Any(r => string.Equals(r, "failure", StringComparison.OrdinalIgnoreCase)))
        {
            return ExpressionJobStatus.Failure;
        }

        if (syntax == ExpressionSyntax.GitHub &&
            provider != PipelineProvider.GitLab &&
            needsResults.Values.Any(r => string.Equals(r, "skipped", StringComparison.OrdinalIgnoreCase)))
        {
            return ExpressionJobStatus.Skipped;
        }

        return ExpressionJobStatus.Success;
    }

    /// <summary>
    /// The GitLab environment of a job, in precedence order: predefined <c>CI_*</c> variables, pipeline
    /// <c>variables:</c>, PDK variables, secrets, then job <c>variables:</c> (later entries win).
    /// </summary>
    /// <param name="pipeline">The pipeline.</param>
    /// <param name="job">The job.</param>
    /// <param name="info">Run facts.</param>
    /// <returns>Variable name → value.</returns>
    public static Dictionary<string, string> GitLabEnvironment(Pipeline pipeline, Job job, JobRuntimeInfo info)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(info);

        var env = GitLabPredefinedVariables.Build(new GitLabVariableContext
        {
            Git = info.Git,
            Workspace = info.StepWorkspace ?? info.Workspace,
            EventName = info.EventName,
            DefaultBranch = pipeline.DefaultBranch,
            PipelineName = info.PipelineName,
            RunId = info.RunId,
            Actor = info.Actor,
            Job = job,
            JobNumber = info.RunNumber
        });

        foreach (var (k, v) in pipeline.Variables)
        {
            env[k] = v;
        }

        foreach (var (k, v) in info.Variables)
        {
            env[k] = v;
        }

        foreach (var (k, v) in info.Secrets)
        {
            env[k] = v;
        }

        foreach (var (k, v) in job.Variables)
        {
            env[k] = v;
        }

        return env;
    }

    /// <summary>
    /// Layers step-scoped data onto a job context: the step's <c>env</c>, values added through
    /// <c>$GITHUB_ENV</c>, and the outcomes of the steps executed so far.
    /// </summary>
    public static ExpressionContext ForStep(
        ExpressionContext jobContext,
        Step step,
        IReadOnlyDictionary<string, string>? dynamicEnv,
        IReadOnlyList<StepOutcome> completedSteps,
        ExpressionJobStatus status)
    {
        ArgumentNullException.ThrowIfNull(jobContext);
        ArgumentNullException.ThrowIfNull(step);

        var context = jobContext.Clone();
        context.Status = status;

        var env = ExpressionValue.NewObject();
        if (jobContext.GetRoot("env") is IReadOnlyDictionary<string, object?> baseEnv)
        {
            foreach (var (k, v) in baseEnv)
            {
                env[k] = v;
            }
        }

        if (dynamicEnv != null)
        {
            foreach (var (k, v) in dynamicEnv)
            {
                env[k] = v;
            }
        }

        foreach (var (k, v) in step.Environment)
        {
            env[k] = v;
        }

        context.SetRoot("env", env);

        var steps = ExpressionValue.NewObject();
        foreach (var outcome in completedSteps)
        {
            if (string.IsNullOrEmpty(outcome.Id))
            {
                continue;
            }

            var entry = ExpressionValue.NewObject();
            entry["outcome"] = outcome.Outcome;
            entry["conclusion"] = outcome.Conclusion;
            entry["outputs"] = ExpressionValue.FromStrings(outcome.Outputs);
            steps[outcome.Id] = entry;
        }

        context.SetRoot("steps", steps);

        if (context.Syntax == ExpressionSyntax.Azure)
        {
            // Azure exposes step outputs as variables: <stepName>.<output>
            if (context.GetRoot("variables") is Dictionary<string, object?> variables)
            {
                foreach (var outcome in completedSteps)
                {
                    if (string.IsNullOrEmpty(outcome.Id))
                    {
                        continue;
                    }

                    foreach (var (name, value) in outcome.Outputs)
                    {
                        variables[$"{outcome.Id}.{name}"] = value;
                    }
                }
            }
        }

        return context;
    }

    /// <summary>
    /// Environment variables to export to every step of the job: platform variables
    /// (<c>GITHUB_*</c>/<c>RUNNER_*</c> or <c>BUILD_*</c>/<c>SYSTEM_*</c>/<c>AGENT_*</c>), pipeline and job
    /// variables, and PDK variables and secrets by name.
    /// </summary>
    public static Dictionary<string, string> BuildStepEnvironment(Pipeline pipeline, Job job, JobRuntimeInfo info)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(info);

        var env = new Dictionary<string, string>(StringComparer.Ordinal);
        var stepWorkspace = info.StepWorkspace ?? info.Workspace;
        var temp = info.StepTempDirectory ?? Path.Combine(stepWorkspace, ".pdk", "tmp");

        env["CI"] = "true";
        env["PDK"] = "true";
        env["PDK_WORKSPACE"] = stepWorkspace;
        env["PDK_JOB"] = job.Name;
        env["PDK_RUNNER"] = info.ContainerImage != null ? "docker" : "host";

        // Variables and secrets by name (so `$NAME` works in scripts)
        foreach (var (k, v) in info.Variables)
        {
            env[k] = v;
        }

        foreach (var (k, v) in info.Secrets)
        {
            env[k] = v;
        }

        // Job-level variables (Azure pipeline/stage/job variables are merged into Job.Variables by the
        // parser); exported here for every provider so a job run without its pipeline keeps them
        foreach (var (k, v) in job.Variables)
        {
            env[k] = v;
        }

        if (info.Provider == PipelineProvider.GitLab)
        {
            // CI, GITLAB_CI and the CI_* predefined variables, then pipeline variables, PDK variables,
            // secrets and job variables; no GITHUB_* / RUNNER_* values.
            foreach (var (k, v) in GitLabEnvironment(pipeline, job, info))
            {
                env[k] = v;
            }
        }
        else if (SyntaxFor(info.Provider) == ExpressionSyntax.Azure)
        {
            var git = info.Git;
            var predefined = AzurePredefinedVariables(info, job, stepWorkspace, temp);
            foreach (var (k, v) in predefined)
            {
                env[AzureEnvName(k)] = v;
            }

            env["TF_BUILD"] = "True";

            // Pipeline, stage and job variables are exported uppercased with dots replaced by underscores
            foreach (var (k, v) in pipeline.Variables)
            {
                env[AzureEnvName(k)] = v;
            }

            foreach (var (k, v) in job.Variables)
            {
                env[AzureEnvName(k)] = v;
            }

            _ = git;
        }
        else
        {
            var git = info.Git;
            env["GITHUB_ACTIONS"] = "true";
            env["GITHUB_WORKSPACE"] = stepWorkspace;
            env["GITHUB_SHA"] = git.Sha;
            env["GITHUB_REF"] = git.Ref;
            env["GITHUB_REF_NAME"] = git.Branch.Length > 0 ? git.Branch : git.ShortSha;
            env["GITHUB_REF_TYPE"] = "branch";
            env["GITHUB_HEAD_REF"] = string.Empty;
            env["GITHUB_BASE_REF"] = string.Empty;
            env["GITHUB_REPOSITORY"] = git.Repository;
            env["GITHUB_REPOSITORY_OWNER"] = git.Owner;
            env["GITHUB_ACTOR"] = info.Actor;
            env["GITHUB_TRIGGERING_ACTOR"] = info.Actor;
            env["GITHUB_EVENT_NAME"] = info.EventName;
            env["GITHUB_RUN_ID"] = info.RunId;
            env["GITHUB_RUN_NUMBER"] = info.RunNumber.ToString(CultureInfo.InvariantCulture);
            env["GITHUB_RUN_ATTEMPT"] = "1";
            env["GITHUB_JOB"] = job.Id;
            env["GITHUB_WORKFLOW"] = info.PipelineName;
            env["GITHUB_ACTION"] = "__run";
            env["GITHUB_SERVER_URL"] = "https://github.com";
            env["GITHUB_API_URL"] = "https://api.github.com";
            env["GITHUB_GRAPHQL_URL"] = "https://api.github.com/graphql";
            env["RUNNER_OS"] = info.RunnerOs;
            env["RUNNER_ARCH"] = info.RunnerArch;
            env["RUNNER_NAME"] = "pdk";
            env["RUNNER_TEMP"] = temp;
            env["RUNNER_TOOL_CACHE"] = Path.Combine(temp, "tool-cache");
            env["RUNNER_WORKSPACE"] = stepWorkspace;

            // Workflow-level env (stored as pipeline variables by the GitHub parser)
            foreach (var (k, v) in pipeline.Variables)
            {
                env[k] = v;
            }
        }

        // Job-level env always wins over platform defaults
        foreach (var (k, v) in job.Environment)
        {
            env[k] = v;
        }

        return env;
    }

    /// <summary>Converts an Azure variable name to its environment variable form (<c>Build.SourceBranch</c> → <c>BUILD_SOURCEBRANCH</c>).</summary>
    public static string AzureEnvName(string name) =>
        name.Replace('.', '_').Replace('-', '_').ToUpperInvariant();

    private static void BuildGitHubRoots(ExpressionContext context, Pipeline pipeline, Job job, JobRuntimeInfo info)
    {
        var git = info.Git;
        var github = ExpressionValue.NewObject();
        github["workspace"] = info.StepWorkspace ?? info.Workspace;
        github["sha"] = git.Sha;
        github["ref"] = git.Ref;
        github["ref_name"] = git.Branch.Length > 0 ? git.Branch : git.ShortSha;
        github["ref_type"] = "branch";
        github["ref_protected"] = false;
        github["head_ref"] = string.Empty;
        github["base_ref"] = string.Empty;
        github["repository"] = git.Repository;
        github["repository_owner"] = git.Owner;
        github["repositoryUrl"] = git.RemoteUrl;
        github["actor"] = info.Actor;
        github["triggering_actor"] = info.Actor;
        github["event_name"] = info.EventName;
        github["run_id"] = info.RunId;
        github["run_number"] = info.RunNumber.ToString(CultureInfo.InvariantCulture);
        github["run_attempt"] = "1";
        github["job"] = job.Id;
        github["workflow"] = info.PipelineName;
        github["action"] = "__run";
        github["action_path"] = string.Empty;
        github["server_url"] = "https://github.com";
        github["api_url"] = "https://api.github.com";
        github["graphql_url"] = "https://api.github.com/graphql";
        github["token"] = Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? string.Empty;
        github["retention_days"] = "90";

        var repository = ExpressionValue.NewObject();
        repository["full_name"] = git.Repository;
        repository["name"] = git.Name;
        repository["default_branch"] = pipeline.DefaultBranch ?? "main";
        var owner = ExpressionValue.NewObject();
        owner["login"] = git.Owner;
        repository["owner"] = owner;

        var evt = ExpressionValue.NewObject();
        evt["ref"] = git.Ref;
        evt["after"] = git.Sha;
        evt["repository"] = repository;
        var headCommit = ExpressionValue.NewObject();
        headCommit["id"] = git.Sha;
        headCommit["message"] = string.Empty;
        evt["head_commit"] = headCommit;
        if (info.Inputs.Count > 0)
        {
            evt["inputs"] = ExpressionValue.FromStrings(info.Inputs);
        }
        github["event"] = evt;

        context.SetRoot("github", github);

        var env = ExpressionValue.FromStrings(pipeline.Variables);
        foreach (var (k, v) in job.Environment)
        {
            env[k] = v;
        }
        context.SetRoot("env", env);

        context.SetRoot("secrets", ExpressionValue.FromStrings(info.Secrets));
        context.SetRoot("vars", ExpressionValue.FromStrings(info.Variables));
        context.SetRoot("inputs", ExpressionValue.FromStrings(info.Inputs));
        context.SetRoot("matrix", job.Matrix != null ? ExpressionValue.FromStrings(job.Matrix) : ExpressionValue.NewObject());

        var runner = ExpressionValue.NewObject();
        runner["os"] = info.RunnerOs;
        runner["arch"] = info.RunnerArch;
        runner["name"] = "pdk";
        runner["temp"] = info.StepTempDirectory ?? Path.Combine(info.StepWorkspace ?? info.Workspace, ".pdk", "tmp");
        runner["tool_cache"] = Path.Combine((string)runner["temp"]!, "tool-cache");
        runner["debug"] = "0";
        context.SetRoot("runner", runner);

        var jobRoot = ExpressionValue.NewObject();
        jobRoot["status"] = "success";
        var container = ExpressionValue.NewObject();
        container["id"] = string.Empty;
        container["image"] = info.ContainerImage ?? job.Container ?? string.Empty;
        jobRoot["container"] = container;
        jobRoot["services"] = ExpressionValue.NewObject();
        context.SetRoot("job", jobRoot);

        var needs = ExpressionValue.NewObject();
        foreach (var dependency in job.DependsOn)
        {
            var entry = ExpressionValue.NewObject();
            entry["result"] = info.NeedsResults.TryGetValue(dependency, out var r) ? r : "success";
            entry["outputs"] = info.NeedsOutputs.TryGetValue(dependency, out var outputs)
                ? ExpressionValue.FromStrings(outputs)
                : ExpressionValue.NewObject();
            needs[dependency] = entry;
        }
        context.SetRoot("needs", needs);

        var strategy = ExpressionValue.NewObject();
        strategy["fail-fast"] = true;
        strategy["job-index"] = 0d;
        strategy["job-total"] = 1d;
        strategy["max-parallel"] = 1d;
        context.SetRoot("strategy", strategy);

        context.SetRoot("steps", ExpressionValue.NewObject());
    }

    private static void BuildAzureRoots(ExpressionContext context, Pipeline pipeline, Job job, JobRuntimeInfo info)
    {
        var stepWorkspace = info.StepWorkspace ?? info.Workspace;
        var temp = info.StepTempDirectory ?? Path.Combine(stepWorkspace, ".pdk", "tmp");

        var variables = ExpressionValue.NewObject();
        foreach (var (k, v) in AzurePredefinedVariables(info, job, stepWorkspace, temp))
        {
            variables[k] = v;
        }

        foreach (var (k, v) in info.Variables)
        {
            variables[k] = v;
        }

        foreach (var (k, v) in pipeline.Variables)
        {
            variables[k] = v;
        }

        foreach (var (k, v) in job.Variables)
        {
            variables[k] = v;
        }

        foreach (var (k, v) in info.Secrets)
        {
            variables[k] = v;
        }

        context.SetRoot("variables", variables);
        context.SetRoot("parameters", ExpressionValue.FromStrings(info.Inputs));
        context.SetRoot("env", ExpressionValue.FromStrings(job.Environment));
        context.SetRoot("secrets", ExpressionValue.FromStrings(info.Secrets));
        context.SetRoot("matrix", job.Matrix != null ? ExpressionValue.FromStrings(job.Matrix) : ExpressionValue.NewObject());

        var dependencies = ExpressionValue.NewObject();
        foreach (var dependency in job.DependsOn)
        {
            var entry = ExpressionValue.NewObject();
            entry["result"] = info.NeedsResults.TryGetValue(dependency, out var r) ? ToAzureResult(r) : "Succeeded";
            var outputs = ExpressionValue.NewObject();
            if (info.NeedsOutputs.TryGetValue(dependency, out var outs))
            {
                foreach (var (k, v) in outs)
                {
                    outputs[k] = v;
                }
            }
            entry["outputs"] = outputs;
            dependencies[dependency] = entry;
            var shortName = dependency.Contains('_') ? dependency[(dependency.LastIndexOf('_') + 1)..] : dependency;
            if (!dependencies.ContainsKey(shortName))
            {
                dependencies[shortName] = entry;
            }
        }
        context.SetRoot("dependencies", dependencies);
        context.SetRoot("stageDependencies", dependencies);
        context.SetRoot("steps", ExpressionValue.NewObject());
    }

    private static string ToAzureResult(string result) => result switch
    {
        "success" => "Succeeded",
        "failure" => "Failed",
        "cancelled" => "Canceled",
        "skipped" => "Skipped",
        _ => result
    };

    /// <summary>
    /// The predefined Azure variables (<c>Build.*</c>, <c>System.*</c>, <c>Agent.*</c>, <c>Pipeline.Workspace</c>) of a job,
    /// or of the pipeline as a whole when <paramref name="job"/> is null (compile-time template expressions, where the
    /// job- and stage-specific values are empty).
    /// </summary>
    /// <param name="info">The run information.</param>
    /// <param name="job">The job, or null for pipeline-level values.</param>
    /// <param name="stepWorkspace">The workspace as seen by the steps; defaults to the run's step workspace.</param>
    /// <param name="temp">The temp directory as seen by the steps; defaults to the run's step temp directory.</param>
    public static Dictionary<string, string> AzurePredefinedVariables(JobRuntimeInfo info, Job? job, string? stepWorkspace = null, string? temp = null)
    {
        ArgumentNullException.ThrowIfNull(info);

        stepWorkspace ??= info.StepWorkspace ?? info.Workspace;
        temp ??= info.StepTempDirectory ?? Path.Combine(stepWorkspace, ".pdk", "tmp");

        var git = info.Git;
        var pdkDir = Path.Combine(stepWorkspace, ".pdk");
        var branch = git.Branch.Length > 0 ? git.Branch : "main";
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Build.SourcesDirectory"] = stepWorkspace,
            ["Build.Repository.LocalPath"] = stepWorkspace,
            ["Build.Repository.Name"] = git.Repository.Length > 0 ? git.Repository : Path.GetFileName(info.Workspace.TrimEnd(Path.DirectorySeparatorChar)),
            ["Build.Repository.Uri"] = git.RemoteUrl,
            ["Build.ArtifactStagingDirectory"] = Path.Combine(pdkDir, "staging"),
            ["Build.StagingDirectory"] = Path.Combine(pdkDir, "staging"),
            ["Build.BinariesDirectory"] = Path.Combine(pdkDir, "binaries"),
            ["Build.BuildId"] = info.RunId,
            ["Build.BuildNumber"] = DateTime.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + "." + info.RunNumber.ToString(CultureInfo.InvariantCulture),
            ["Build.DefinitionName"] = info.PipelineName,
            ["Build.SourceBranch"] = git.Ref.Length > 0 ? git.Ref : $"refs/heads/{branch}",
            ["Build.SourceBranchName"] = branch,
            ["Build.SourceVersion"] = git.Sha,
            ["Build.Reason"] = info.EventName?.ToLowerInvariant() switch
            {
                "push" => "IndividualCI",
                "pull_request" or "pull_request_target" => "PullRequest",
                "schedule" => "Schedule",
                "workflow_dispatch" or "manual" or null or "" => "Manual",
                _ => "Manual"
            },
            ["Build.RequestedFor"] = info.Actor,
            ["System.DefaultWorkingDirectory"] = stepWorkspace,
            ["System.TeamProject"] = "local",
            ["System.JobName"] = job?.Name ?? string.Empty,
            ["System.JobDisplayName"] = job?.Name ?? string.Empty,
            ["System.StageName"] = job?.Stage ?? string.Empty,
            ["System.PullRequest.SourceBranch"] = string.Empty,
            ["Agent.BuildDirectory"] = pdkDir,
            ["Agent.TempDirectory"] = temp,
            ["Agent.OS"] = info.RunnerOs switch { "Windows" => "Windows_NT", "macOS" => "Darwin", _ => "Linux" },
            ["Agent.Name"] = "pdk",
            ["Agent.JobStatus"] = "Succeeded",
            ["Pipeline.Workspace"] = pdkDir
        };
    }
}
