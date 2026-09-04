using System.Text.RegularExpressions;
using PDK.Core.Artifacts;
using PDK.Core.Models;
using PDK.Providers.Common;
using PDK.Providers.GitHub.Models;

namespace PDK.Providers.GitHub;

/// <summary>
/// Maps GitHub Actions steps to PDK step types.
/// Handles action reference parsing and shell detection for run commands.
/// </summary>
public static class ActionMapper
{
    private static readonly Regex ActionReferenceRegex = new(
        @"^(?<owner>[^/@\s]+)/(?<repo>[^/@\s]+)(?:/(?<path>[^@]+))?@(?<version>\S+)$",
        RegexOptions.Compiled);

    private static readonly HashSet<string> SetupActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "actions/setup-dotnet",
        "actions/setup-node",
        "actions/setup-python",
        "actions/setup-java",
        "actions/setup-go",
        "actions/cache",
        "actions/cache/restore",
        "actions/cache/save",
        "codecov/codecov-action",
        "docker/setup-buildx-action",
        "docker/setup-qemu-action",
        "docker/login-action",
        "gradle/actions/setup-gradle",
        "gradle/gradle-build-action"
    };

    /// <summary>
    /// Maps a GitHub step to a PDK Step model.
    /// </summary>
    /// <param name="gitHubStep">The GitHub step to map.</param>
    /// <param name="stepIndex">The index of the step (used for auto-generating names).</param>
    /// <returns>A PDK Step object.</returns>
    public static Step MapStep(GitHubStep gitHubStep, int stepIndex) => MapStep(gitHubStep, stepIndex, null);

    /// <summary>
    /// Maps a GitHub step to a PDK Step model, applying <c>defaults.run</c> to run steps.
    /// </summary>
    /// <param name="gitHubStep">The GitHub step to map.</param>
    /// <param name="stepIndex">The index of the step (used for auto-generating names).</param>
    /// <param name="runDefaults">The effective <c>defaults.run</c> (workflow merged with job), or null.</param>
    /// <returns>A PDK Step object.</returns>
    public static Step MapStep(GitHubStep gitHubStep, int stepIndex, GitHubRunDefaults? runDefaults)
    {
        ArgumentNullException.ThrowIfNull(gitHubStep);

        var step = new Step
        {
            Id = string.IsNullOrWhiteSpace(gitHubStep.Id) ? null : gitHubStep.Id,
            Name = GenerateStepName(gitHubStep, stepIndex),
            Environment = MergeEnvironmentVariables(null, null, gitHubStep.Env),
            ContinueOnError = gitHubStep.ContinueOnErrorValue,
            WorkingDirectory = gitHubStep.WorkingDirectory,
            TimeoutMinutes = gitHubStep.TimeoutMinutesValue
        };

        if (!string.IsNullOrWhiteSpace(gitHubStep.Uses))
        {
            MapActionStep(gitHubStep, step);
        }
        else if (!string.IsNullOrWhiteSpace(gitHubStep.Run))
        {
            MapScriptStep(gitHubStep, step, runDefaults);
        }
        else
        {
            step.Type = StepType.Unknown;
        }

        if (!string.IsNullOrWhiteSpace(gitHubStep.If))
        {
            step.Condition = new Condition
            {
                Expression = gitHubStep.If,
                Type = ConditionType.Expression
            };
        }

        return step;
    }

    /// <summary>
    /// Reduces a GitHub <c>shell:</c> value to its base shell name: the first token of a template such as
    /// <c>bash --noprofile --norc -eo pipefail {0}</c> becomes <c>bash</c>; paths and <c>.exe</c> suffixes are dropped.
    /// An unset shell defaults to <c>bash</c>.
    /// </summary>
    public static string NormalizeShell(string? shell)
    {
        if (string.IsNullOrWhiteSpace(shell))
        {
            return "bash";
        }

        var trimmed = shell.Trim();

        // An explicit executable path may contain spaces ("C:/Program Files/Git/bin/bash.exe {0}"): cut at ".exe" first
        var exeIndex = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        string token;
        if (exeIndex > 0 && (exeIndex + 4 == trimmed.Length || char.IsWhiteSpace(trimmed[exeIndex + 4])))
        {
            token = trimmed[..exeIndex];
        }
        else
        {
            var tokens = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            token = tokens.Length > 0 ? tokens[0] : "bash";
        }

        if (token.Contains('/') || token.Contains('\\'))
        {
            token = Path.GetFileName(token.Replace('\\', '/'));
        }

        return token.Length == 0 ? "bash" : token.ToLowerInvariant();
    }

    /// <summary>
    /// Maps an action step (uses) to a PDK Step.
    /// </summary>
    private static void MapActionStep(GitHubStep gitHubStep, Step step)
    {
        var actionRef = gitHubStep.Uses!.Trim();
        step.ActionReference = actionRef;
        step.With = gitHubStep.With is null
            ? new Dictionary<string, string>()
            : gitHubStep.With.ToDictionary(pair => pair.Key, pair => pair.Value ?? string.Empty);

        // Store original action reference for reference
        step.With["_action"] = actionRef;

        var match = ActionReferenceRegex.Match(actionRef);
        if (!match.Success)
        {
            // Local actions (./path), docker://image or malformed references: skipped with a warning by the runners
            step.Type = StepType.Unknown;
            return;
        }

        var owner = match.Groups["owner"].Value;
        var repo = match.Groups["repo"].Value;
        var path = match.Groups["path"].Value.Trim('/');
        var version = match.Groups["version"].Value;

        step.With["_version"] = version;

        var actionKey = string.IsNullOrEmpty(path)
            ? $"{owner}/{repo}"
            : $"{owner}/{repo}/{path}";

        step.Type = MapActionToStepType(actionKey);

        switch (step.Type)
        {
            case StepType.UploadArtifact:
                step.Artifact = ParseUploadArtifact(gitHubStep.With);
                break;
            case StepType.DownloadArtifact:
                step.Artifact = ParseDownloadArtifact(gitHubStep.With);
                break;
            case StepType.Docker:
                MapDockerBuildPushInputs(step);
                break;
        }
    }

    /// <summary>
    /// Maps docker/build-push-action inputs onto the keys the Docker executor understands.
    /// </summary>
    private static void MapDockerBuildPushInputs(Step step)
    {
        step.With["command"] = "build";

        if (step.With.TryGetValue("file", out var file) && !string.IsNullOrWhiteSpace(file))
        {
            step.With["Dockerfile"] = file;
        }

        if (!step.With.TryGetValue("context", out var context) || string.IsNullOrWhiteSpace(context))
        {
            step.With["context"] = ".";
        }

        if (step.With.TryGetValue("build-args", out var buildArgs) && !string.IsNullOrWhiteSpace(buildArgs))
        {
            step.With["buildArgs"] = buildArgs;
        }
    }

    /// <summary>
    /// Maps a script step (run) to a PDK Step.
    /// </summary>
    private static void MapScriptStep(GitHubStep gitHubStep, Step step, GitHubRunDefaults? runDefaults)
    {
        step.Script = gitHubStep.Run;

        var shellSource = !string.IsNullOrWhiteSpace(gitHubStep.Shell) ? gitHubStep.Shell : runDefaults?.Shell;
        var shell = NormalizeShell(shellSource);

        step.Shell = shell;
        step.Type = shell switch
        {
            "pwsh" or "powershell" => StepType.PowerShell,
            _ => StepType.Script
        };

        if (string.IsNullOrWhiteSpace(step.WorkingDirectory) && !string.IsNullOrWhiteSpace(runDefaults?.WorkingDirectory))
        {
            step.WorkingDirectory = runDefaults.WorkingDirectory;
        }
    }

    /// <summary>
    /// Maps a GitHub action reference to a PDK StepType.
    /// </summary>
    /// <param name="actionKey">The action key in format "owner/repo" or "owner/repo/path".</param>
    /// <returns>The corresponding StepType.</returns>
    private static StepType MapActionToStepType(string actionKey)
    {
        var key = actionKey.ToLowerInvariant();

        return key switch
        {
            "actions/checkout" => StepType.Checkout,
            "actions/upload-artifact" => StepType.UploadArtifact,
            "actions/download-artifact" => StepType.DownloadArtifact,
            "docker/build-push-action" => StepType.Docker,
            _ when SetupActions.Contains(key) || key.StartsWith("actions/setup-", StringComparison.Ordinal) => StepType.Setup,
            _ => StepType.Unknown
        };
    }

    /// <summary>
    /// Generates a step name if one is not provided.
    /// </summary>
    private static string GenerateStepName(GitHubStep gitHubStep, int stepIndex)
    {
        // If name is provided, use it
        if (!string.IsNullOrWhiteSpace(gitHubStep.Name))
        {
            return gitHubStep.Name;
        }

        // Generate name from action reference
        if (!string.IsNullOrWhiteSpace(gitHubStep.Uses))
        {
            var uses = gitHubStep.Uses.Trim();
            var match = ActionReferenceRegex.Match(uses);
            if (!match.Success)
            {
                return uses;
            }

            var owner = match.Groups["owner"].Value;
            var repo = match.Groups["repo"].Value;
            var path = match.Groups["path"].Value.Trim('/');
            var actionKey = string.IsNullOrEmpty(path) ? $"{owner}/{repo}" : $"{owner}/{repo}/{path}";

            return KnownActionDisplayName(actionKey) ?? actionKey;
        }

        // Generate name from run command
        if (!string.IsNullOrWhiteSpace(gitHubStep.Run))
        {
            var command = gitHubStep.Run.Trim();

            // Take first line if multi-line
            var firstLine = command.Split('\n')[0].Trim();

            // Limit length for readability (accounting for "Run " prefix + "..." suffix)
            const int maxTotalLength = 50;
            const string prefix = "Run ";
            const string ellipsis = "...";
            var maxCommandLength = maxTotalLength - prefix.Length - ellipsis.Length;

            if (firstLine.Length > maxCommandLength)
            {
                firstLine = firstLine[..maxCommandLength] + ellipsis;
            }

            return $"{prefix}{firstLine}";
        }

        // Fallback to step index
        return $"Step {stepIndex + 1}";
    }

    /// <summary>
    /// Returns the display name of a well-known action, or null for actions PDK does not recognise.
    /// </summary>
    private static string? KnownActionDisplayName(string actionKey)
    {
        var key = actionKey.ToLowerInvariant();

        return key switch
        {
            "actions/checkout" => "Checkout",
            "actions/setup-dotnet" => "Setup .NET",
            "actions/setup-node" => "Setup Node.js",
            "actions/setup-python" => "Setup Python",
            "actions/setup-java" => "Setup Java",
            "actions/setup-go" => "Setup Go",
            "actions/upload-artifact" => "Upload Artifact",
            "actions/download-artifact" => "Download Artifact",
            "actions/cache" => "Cache",
            "actions/cache/restore" => "Cache Restore",
            "actions/cache/save" => "Cache Save",
            "codecov/codecov-action" => "Codecov",
            "docker/setup-buildx-action" => "Set up Docker Buildx",
            "docker/setup-qemu-action" => "Set up QEMU",
            "docker/login-action" => "Docker Login",
            "docker/build-push-action" => "Docker Build and Push",
            "gradle/actions/setup-gradle" => "Setup Gradle",
            "gradle/gradle-build-action" => "Gradle Build",
            _ when key.StartsWith("actions/setup-", StringComparison.Ordinal) => FormatActionName(key["actions/".Length..]),
            _ => null
        };
    }

    /// <summary>
    /// Formats an action name into a human-readable format.
    /// Example: "setup-dotnet" -> "Setup .NET", "cache/restore" -> "Cache Restore". Empty segments are ignored.
    /// </summary>
    public static string FormatActionName(string actionName)
    {
        if (string.IsNullOrWhiteSpace(actionName))
        {
            return "Action";
        }

        // Handle special cases
        var formatted = actionName switch
        {
            "checkout" => "Checkout",
            "setup-dotnet" => "Setup .NET",
            "setup-node" => "Setup Node.js",
            "setup-python" => "Setup Python",
            "setup-java" => "Setup Java",
            "setup-go" => "Setup Go",
            "upload-artifact" => "Upload Artifact",
            "download-artifact" => "Download Artifact",
            _ => null
        };

        if (formatted is not null)
        {
            return formatted;
        }

        // Replace hyphens/slashes/underscores with spaces and title case, skipping empty segments ("foo--bar")
        var words = actionName
            .Split(new[] { '-', '/', '_' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..]);

        var joined = string.Join(' ', words);
        return joined.Length == 0 ? actionName : joined;
    }

    /// <summary>
    /// Parses job dependencies from the needs field.
    /// The needs field can be a single string or an array of strings.
    /// </summary>
    /// <param name="needs">The needs field value.</param>
    /// <returns>A list of job IDs this job depends on.</returns>
    public static List<string> ParseJobDependencies(object? needs) => YamlValues.ToStringList(needs);

    /// <summary>
    /// Merges environment variables from workflow, job, and step levels.
    /// Later values override earlier ones (step overrides job overrides workflow).
    /// </summary>
    /// <param name="workflowEnv">Workflow-level environment variables.</param>
    /// <param name="jobEnv">Job-level environment variables.</param>
    /// <param name="stepEnv">Step-level environment variables.</param>
    /// <returns>Merged environment variables.</returns>
    public static Dictionary<string, string> MergeEnvironmentVariables(
        Dictionary<string, string>? workflowEnv,
        Dictionary<string, string>? jobEnv,
        Dictionary<string, string>? stepEnv)
    {
        var merged = new Dictionary<string, string>();

        // Apply in order: workflow -> job -> step
        foreach (var level in new[] { workflowEnv, jobEnv, stepEnv })
        {
            if (level is null)
            {
                continue;
            }

            foreach (var kvp in level)
            {
                merged[kvp.Key] = kvp.Value ?? string.Empty;
            }
        }

        return merged;
    }

    #region Artifact Parsing

    /// <summary>
    /// Parses GitHub upload-artifact action parameters into an ArtifactDefinition.
    /// </summary>
    /// <param name="with">The action's with parameters.</param>
    /// <returns>An ArtifactDefinition for the upload operation.</returns>
    private static ArtifactDefinition ParseUploadArtifact(Dictionary<string, string>? with)
    {
        var name = with?.GetValueOrDefault("name") ?? "artifact";
        var path = with?.GetValueOrDefault("path") ?? "";
        var retentionDays = with?.GetValueOrDefault("retention-days");
        var ifNoFilesFound = with?.GetValueOrDefault("if-no-files-found");

        return new ArtifactDefinition
        {
            Name = name,
            Operation = ArtifactOperation.Upload,
            Patterns = ParsePathPatterns(path),
            Options = new ArtifactOptions
            {
                RetentionDays = int.TryParse(retentionDays, out var days) ? days : null,
                IfNoFilesFound = ParseIfNoFilesFound(ifNoFilesFound),
                Compression = CompressionType.Gzip
            }
        };
    }

    /// <summary>
    /// Parses GitHub download-artifact action parameters into an ArtifactDefinition.
    /// </summary>
    /// <param name="with">The action's with parameters.</param>
    /// <returns>An ArtifactDefinition for the download operation.</returns>
    private static ArtifactDefinition ParseDownloadArtifact(Dictionary<string, string>? with)
    {
        var name = with?.GetValueOrDefault("name") ?? "";
        var path = with?.GetValueOrDefault("path") ?? "./";

        return new ArtifactDefinition
        {
            Name = name,
            Operation = ArtifactOperation.Download,
            Patterns = Array.Empty<string>(),
            TargetPath = path,
            Options = ArtifactOptions.Default
        };
    }

    /// <summary>
    /// Parses path patterns from the 'path' input which can be a single path or multi-line string.
    /// </summary>
    /// <param name="pathValue">The path value which may contain newline-separated patterns.</param>
    /// <returns>An array of path patterns.</returns>
    private static string[] ParsePathPatterns(string pathValue)
    {
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return Array.Empty<string>();
        }

        // Handle multi-line literal blocks or newline-separated paths
        return pathValue
            .Split('\n')
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToArray();
    }

    /// <summary>
    /// Parses the if-no-files-found parameter to the corresponding enum value.
    /// </summary>
    /// <param name="value">The string value from the action input.</param>
    /// <returns>The IfNoFilesFound enum value. Defaults to Warn for GitHub Actions.</returns>
    private static IfNoFilesFound ParseIfNoFilesFound(string? value)
    {
        return value?.ToLowerInvariant() switch
        {
            "error" => IfNoFilesFound.Error,
            "warn" => IfNoFilesFound.Warn,
            "ignore" => IfNoFilesFound.Ignore,
            _ => IfNoFilesFound.Warn  // GitHub default
        };
    }

    #endregion
}
