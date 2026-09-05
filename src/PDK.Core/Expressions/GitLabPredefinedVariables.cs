using System.Globalization;
using System.Text;
using PDK.Core.Models;

namespace PDK.Core.Expressions;

/// <summary>
/// Facts about a run used to compute the GitLab CI/CD predefined variables (<c>CI_*</c>).
/// </summary>
public sealed record GitLabVariableContext
{
    /// <summary>Git metadata for the workspace.</summary>
    public required GitInfo Git { get; init; }

    /// <summary>Workspace path as seen by the steps (<c>CI_PROJECT_DIR</c>).</summary>
    public required string Workspace { get; init; }

    /// <summary>Event that "triggered" the run (push, pull_request, schedule, ...). Mapped to <c>CI_PIPELINE_SOURCE</c>.</summary>
    public string EventName { get; init; } = "push";

    /// <summary>Default branch override (<see cref="Pipeline.DefaultBranch"/>); falls back to git, then <c>main</c>.</summary>
    public string? DefaultBranch { get; init; }

    /// <summary>Pipeline name (<c>CI_PIPELINE_NAME</c>).</summary>
    public string PipelineName { get; init; } = string.Empty;

    /// <summary>Run identifier (<c>CI_PIPELINE_ID</c>).</summary>
    public string RunId { get; init; } = "1";

    /// <summary>User running the pipeline (<c>GITLAB_USER_LOGIN</c>).</summary>
    public string Actor { get; init; } = Environment.UserName;

    /// <summary>The job, when job-level variables (<c>CI_JOB_*</c>) are wanted; null for pipeline-level values only.</summary>
    public Job? Job { get; init; }

    /// <summary>Ordinal of the job within the run (<c>CI_JOB_ID</c>).</summary>
    public int JobNumber { get; init; } = 1;
}

/// <summary>
/// Computes the GitLab CI/CD predefined variables PDK exposes to <c>rules</c> at parse time and exports to every
/// step at run time. Variables GitLab leaves undefined in a given situation (<c>CI_COMMIT_TAG</c> on a branch,
/// <c>CI_COMMIT_BRANCH</c> in a merge request pipeline, <c>CI_MERGE_REQUEST_*</c> outside one) are absent rather
/// than empty so that <c>$VAR == null</c> behaves as on GitLab.
/// </summary>
public static class GitLabPredefinedVariables
{
    /// <summary>The GitLab instance URL PDK pretends to run on.</summary>
    public const string ServerUrl = "https://gitlab.com";

    /// <summary>Host part of <see cref="ServerUrl"/>.</summary>
    public const string ServerHost = "gitlab.com";

    /// <summary>
    /// Maps a PDK event name (GitHub vocabulary: push, pull_request, schedule, workflow_dispatch, ...) to the
    /// <c>CI_PIPELINE_SOURCE</c> value GitLab would report.
    /// </summary>
    /// <param name="eventName">The event name; null or empty means push.</param>
    /// <returns>The pipeline source.</returns>
    public static string PipelineSource(string? eventName)
    {
        return (eventName ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "" or "push" => "push",
            "pull_request" or "pull_request_target" or "merge_request" or "merge_request_event" => "merge_request_event",
            "schedule" => "schedule",
            "web" or "workflow_dispatch" or "manual" => "web",
            "api" or "repository_dispatch" => "api",
            "trigger" => "trigger",
            "pipeline" or "parent_pipeline" or "external" or "chat" or "webide" or "external_pull_request_event"
                or "ondemand_dast_scan" or "ondemand_dast_validation" or "security_orchestration_policy" => eventName!.Trim().ToLowerInvariant(),
            _ => "push"
        };
    }

    /// <summary>
    /// Produces the <c>*_SLUG</c> form of a value: lowercase, characters other than <c>0-9</c> and <c>a-z</c>
    /// replaced with <c>-</c>, shortened to 63 characters, leading and trailing dashes removed.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The slug.</returns>
    public static string Slug(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var sb = new StringBuilder(value.Length);
        foreach (var c in value.ToLowerInvariant())
        {
            sb.Append(c is (>= 'a' and <= 'z') or (>= '0' and <= '9') ? c : '-');
        }

        var slug = sb.ToString();
        if (slug.Length > 63)
        {
            slug = slug[..63];
        }

        return slug.Trim('-');
    }

    /// <summary>
    /// Builds the predefined variables for <paramref name="context"/>.
    /// </summary>
    /// <param name="context">Run facts.</param>
    /// <returns>Variable name → value (ordinal keys).</returns>
    public static Dictionary<string, string> Build(GitLabVariableContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var git = context.Git;
        var workspace = context.Workspace;
        var source = PipelineSource(context.EventName);
        var isMergeRequest = source == "merge_request_event";

        var branch = git.Branch;
        var refName = branch.Length > 0 ? branch : git.ShortSha;
        var defaultBranch = FirstNonEmpty(context.DefaultBranch, git.DefaultBranch, "main");

        var projectName = FirstNonEmpty(git.Name, Path.GetFileName(workspace.TrimEnd('/', '\\')), "project");
        var projectNamespace = FirstNonEmpty(git.Owner, "local");
        var projectPath = $"{projectNamespace}/{projectName}";
        var projectUrl = $"{ServerUrl}/{projectPath}";
        var buildsDir = ParentDirectory(workspace) ?? workspace;

        var env = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CI"] = "true",
            ["GITLAB_CI"] = "true",
            ["CI_SERVER"] = "yes",
            ["CI_SERVER_URL"] = ServerUrl,
            ["CI_SERVER_HOST"] = ServerHost,
            ["CI_SERVER_NAME"] = "GitLab",
            ["CI_SERVER_PROTOCOL"] = "https",
            ["CI_API_V4_URL"] = $"{ServerUrl}/api/v4",
            ["CI_PIPELINE_SOURCE"] = source,
            ["CI_PIPELINE_ID"] = context.RunId,
            ["CI_PIPELINE_IID"] = "1",
            ["CI_PIPELINE_URL"] = $"{projectUrl}/-/pipelines/{context.RunId}",
            ["CI_PIPELINE_NAME"] = context.PipelineName,
            ["CI_COMMIT_SHA"] = git.Sha,
            ["CI_COMMIT_SHORT_SHA"] = git.ShortSha,
            ["CI_COMMIT_REF_NAME"] = refName,
            ["CI_COMMIT_REF_SLUG"] = Slug(refName),
            ["CI_COMMIT_REF_PROTECTED"] = "false",
            ["CI_DEFAULT_BRANCH"] = defaultBranch,
            ["CI_PROJECT_DIR"] = workspace,
            ["CI_BUILDS_DIR"] = buildsDir,
            ["CI_PROJECT_ID"] = "1",
            ["CI_PROJECT_NAME"] = projectName,
            ["CI_PROJECT_TITLE"] = projectName,
            ["CI_PROJECT_NAMESPACE"] = projectNamespace,
            ["CI_PROJECT_ROOT_NAMESPACE"] = projectNamespace,
            ["CI_PROJECT_PATH"] = projectPath,
            ["CI_PROJECT_PATH_SLUG"] = Slug(projectPath),
            ["CI_PROJECT_URL"] = projectUrl,
            ["CI_PROJECT_VISIBILITY"] = "private",
            ["CI_REPOSITORY_URL"] = FirstNonEmpty(git.RemoteUrl, projectUrl + ".git"),
            ["CI_RUNNER_ID"] = "1",
            ["CI_RUNNER_DESCRIPTION"] = "pdk",
            ["CI_RUNNER_TAGS"] = "[\"pdk\"]",
            ["CI_CONCURRENT_ID"] = "0",
            ["CI_CONCURRENT_PROJECT_ID"] = "0",
            ["GITLAB_USER_ID"] = "1",
            ["GITLAB_USER_LOGIN"] = context.Actor,
            ["GITLAB_USER_NAME"] = context.Actor,
            ["GITLAB_USER_EMAIL"] = string.Empty
        };

        if (!isMergeRequest)
        {
            env["CI_COMMIT_BRANCH"] = branch;
        }
        else
        {
            env["CI_MERGE_REQUEST_ID"] = "1";
            env["CI_MERGE_REQUEST_IID"] = "1";
            env["CI_MERGE_REQUEST_REF_PATH"] = "refs/merge-requests/1/head";
            env["CI_MERGE_REQUEST_EVENT_TYPE"] = "detached";
            env["CI_MERGE_REQUEST_SOURCE_BRANCH_NAME"] = refName;
            env["CI_MERGE_REQUEST_SOURCE_BRANCH_SHA"] = git.Sha;
            env["CI_MERGE_REQUEST_TARGET_BRANCH_NAME"] = defaultBranch;
            env["CI_MERGE_REQUEST_PROJECT_ID"] = "1";
            env["CI_MERGE_REQUEST_PROJECT_PATH"] = projectPath;
            env["CI_MERGE_REQUEST_PROJECT_URL"] = projectUrl;
            env["CI_MERGE_REQUEST_SOURCE_PROJECT_ID"] = "1";
            env["CI_MERGE_REQUEST_SOURCE_PROJECT_PATH"] = projectPath;
            env["CI_MERGE_REQUEST_SOURCE_PROJECT_URL"] = projectUrl;
            env["CI_MERGE_REQUEST_TITLE"] = string.Empty;
            env["CI_MERGE_REQUEST_LABELS"] = string.Empty;
        }

        if (context.Job is { } job)
        {
            var jobId = context.JobNumber.ToString(CultureInfo.InvariantCulture);
            env["CI_JOB_ID"] = jobId;
            env["CI_JOB_NAME"] = job.Name;
            env["CI_JOB_NAME_SLUG"] = Slug(job.Name);
            env["CI_JOB_STAGE"] = job.Stage ?? "test";
            env["CI_JOB_STATUS"] = "running";
            env["CI_JOB_URL"] = $"{projectUrl}/-/jobs/{jobId}";
            env["CI_JOB_TOKEN"] = string.Empty;
            if (!string.IsNullOrEmpty(job.Container))
            {
                env["CI_JOB_IMAGE"] = job.Container;
            }
        }

        return env;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static string? ParentDirectory(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        var trimmed = path.TrimEnd('/', '\\');
        var separator = Math.Max(trimmed.LastIndexOf('/'), trimmed.LastIndexOf('\\'));
        if (separator <= 0)
        {
            return separator == 0 ? trimmed[..1] : null;
        }

        return trimmed[..separator];
    }
}
