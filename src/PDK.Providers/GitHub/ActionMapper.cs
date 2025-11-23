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
    private static StepType MapActionToStepType(string actionKey)
    {
        return actionKey.ToLowerInvariant() switch
        {
            "actions/checkout" => StepType.Checkout,
            "actions/setup-dotnet" => StepType.Dotnet,
            "actions/setup-node" => StepType.Npm,
            "actions/setup-python" => StepType.Python,
            "actions/setup-java" => StepType.Maven,
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
}
