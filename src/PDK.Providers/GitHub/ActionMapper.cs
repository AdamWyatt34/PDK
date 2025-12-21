using PDK.Core.Artifacts;
using PDK.Core.Models;
using PDK.Providers.GitHub.Models;
using System.Text.RegularExpressions;

namespace PDK.Providers.GitHub;

/// <summary>
/// Maps GitHub Actions to PDK step types.
/// Handles action reference parsing and shell detection for run commands.
/// </summary>
public static class ActionMapper
{
    private static readonly Regex ActionReferenceRegex = new(
        @"^(?<owner>[^/]+)/(?<repo>[^/@]+)(?:/(?<path>[^@]+))?@(?<version>.+)$",
        RegexOptions.Compiled);

    /// <summary>
    /// Maps a GitHub step to a PDK Step model.
    /// </summary>
    /// <param name="gitHubStep">The GitHub step to map.</param>
    /// <param name="stepIndex">The index of the step (used for auto-generating names).</param>
    /// <returns>A PDK Step object.</returns>
    public static Step MapStep(GitHubStep gitHubStep, int stepIndex)
    {
        var step = new Step
        {
            Id = gitHubStep.Id,
            Name = GenerateStepName(gitHubStep, stepIndex),
            Environment = gitHubStep.Env ?? new Dictionary<string, string>(),
            ContinueOnError = gitHubStep.ContinueOnError ?? false,
            WorkingDirectory = gitHubStep.WorkingDirectory
        };

        // Determine step type and configuration
        if (!string.IsNullOrWhiteSpace(gitHubStep.Uses))
        {
            MapActionStep(gitHubStep, step);
        }
        else if (!string.IsNullOrWhiteSpace(gitHubStep.Run))
        {
            MapScriptStep(gitHubStep, step);
        }
        else
        {
            step.Type = StepType.Unknown;
        }

        // Map conditional if present
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
    /// Maps an action step (uses) to a PDK Step.
    /// </summary>
    private static void MapActionStep(GitHubStep gitHubStep, Step step)
    {
        var actionRef = gitHubStep.Uses!;
        var match = ActionReferenceRegex.Match(actionRef);

        if (!match.Success)
        {
            // Invalid action reference format, treat as unknown
            step.Type = StepType.Unknown;
            step.Script = actionRef;
            return;
        }

        var owner = match.Groups["owner"].Value;
        var repo = match.Groups["repo"].Value;
        var path = match.Groups["path"].Value;
        var version = match.Groups["version"].Value;

        // Map well-known actions to step types
        var actionKey = string.IsNullOrEmpty(path)
            ? $"{owner}/{repo}"
            : $"{owner}/{repo}/{path}";

        step.Type = MapActionToStepType(actionKey);
        step.With = gitHubStep.With ?? new Dictionary<string, string>();

        // Store original action reference for reference
        step.With["_action"] = actionRef;
        step.With["_version"] = version;

        // Parse artifact definitions for artifact steps
        if (step.Type == StepType.UploadArtifact)
        {
            step.Artifact = ParseUploadArtifact(gitHubStep.With);
        }
        else if (step.Type == StepType.DownloadArtifact)
        {
            step.Artifact = ParseDownloadArtifact(gitHubStep.With);
        }
    }

    /// <summary>
    /// Maps a script step (run) to a PDK Step.
    /// </summary>
    private static void MapScriptStep(GitHubStep gitHubStep, Step step)
    {
        step.Script = gitHubStep.Run;
        step.Shell = gitHubStep.Shell ?? "bash"; // Default to bash

        // Determine step type based on shell
        step.Type = gitHubStep.Shell?.ToLowerInvariant() switch
        {
            "pwsh" or "powershell" => StepType.PowerShell,
            "bash" => StepType.Bash,
            "sh" => StepType.Bash,
            "python" => StepType.Python,
            _ => StepType.Script // Generic script type
        };
    }

    /// <summary>
    /// Maps a GitHub action reference to a PDK StepType.
    /// </summary>
    /// <param name="actionKey">The action key in format "owner/repo" or "owner/repo/path".</param>
    /// <returns>The corresponding StepType.</returns>
    private static StepType MapActionToStepType(string actionKey)
    {
        return actionKey.ToLowerInvariant() switch
        {
            "actions/checkout" => StepType.Checkout,
            "actions/setup-dotnet" => StepType.Dotnet,
            "actions/setup-node" => StepType.Npm,
            "actions/setup-python" => StepType.Python,
            "actions/setup-java" => StepType.Maven,
            "actions/upload-artifact" => StepType.UploadArtifact,
            "actions/download-artifact" => StepType.DownloadArtifact,
            "gradle/gradle-build-action" => StepType.Gradle,
            "docker/build-push-action" => StepType.Docker,
            "docker/login-action" => StepType.Docker,
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
            var match = ActionReferenceRegex.Match(gitHubStep.Uses);
            if (match.Success)
            {
                var repo = match.Groups["repo"].Value;
                var path = match.Groups["path"].Value;

                // Format: "Setup .NET" or "Checkout"
                var actionName = string.IsNullOrEmpty(path) ? repo : path;
                return FormatActionName(actionName);
            }

            return gitHubStep.Uses;
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
                firstLine = firstLine.Substring(0, maxCommandLength) + ellipsis;
            }

            return $"{prefix}{firstLine}";
        }

        // Fallback to step index
        return $"Step {stepIndex + 1}";
    }

    /// <summary>
    /// Formats an action name into a human-readable format.
    /// Example: "setup-dotnet" -> "Setup .NET"
    /// </summary>
    private static string FormatActionName(string actionName)
    {
        // Handle special cases
        var formatted = actionName switch
        {
            "checkout" => "Checkout",
            "setup-dotnet" => "Setup .NET",
            "setup-node" => "Setup Node.js",
            "setup-python" => "Setup Python",
            "setup-java" => "Setup Java",
            "upload-artifact" => "Upload Artifact",
            "download-artifact" => "Download Artifact",
            _ => actionName
        };

        // If no special case, apply general formatting
        if (formatted == actionName)
        {
            // Replace hyphens with spaces and title case
            formatted = string.Join(" ",
                actionName.Split('-')
                    .Select(word => char.ToUpper(word[0]) + word.Substring(1)));
        }

        return formatted;
    }

    /// <summary>
    /// Parses job dependencies from the needs field.
    /// The needs field can be a single string or an array of strings.
    /// </summary>
    /// <param name="needs">The needs field value.</param>
    /// <returns>A list of job IDs this job depends on.</returns>
    public static List<string> ParseJobDependencies(object? needs)
    {
        if (needs == null)
        {
            return new List<string>();
        }

        if (needs is string singleDep)
        {
            return new List<string> { singleDep };
        }

        if (needs is IEnumerable<object> deps)
        {
            return deps.Select(d => d.ToString() ?? string.Empty)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
        }

        return new List<string>();
    }

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
        if (workflowEnv != null)
        {
            foreach (var kvp in workflowEnv)
            {
                merged[kvp.Key] = kvp.Value;
            }
        }

        if (jobEnv != null)
        {
            foreach (var kvp in jobEnv)
            {
                merged[kvp.Key] = kvp.Value;
            }
        }

        if (stepEnv != null)
        {
            foreach (var kvp in stepEnv)
            {
                merged[kvp.Key] = kvp.Value;
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
            return Array.Empty<string>();

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
