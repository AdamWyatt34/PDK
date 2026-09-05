namespace PDK.Runners.StepExecutors;

using System.Text;
using PDK.Core.Models;
using PDK.Runners.Models;

/// <summary>
/// Shared plumbing for step executors: environment merging, option resolution, timeouts and
/// result construction. Keeps the individual executors small and consistent between Docker and host mode.
/// </summary>
internal static class StepExecutionHelpers
{
    /// <summary>
    /// Resolves the effective execution options for a container step.
    /// </summary>
    /// <remarks>
    /// Integration point for the job runner: once <see cref="ExecutionContext"/> exposes
    /// <c>OutputLineHandler</c> / <c>Timeout</c>, fall back to them here, e.g.
    /// <c>options ?? new StepExecutionOptions { OnOutputLine = context.OutputLineHandler, Timeout = context.Timeout }</c>.
    /// Every executor resolves its options through this single method.
    /// </remarks>
    public static StepExecutionOptions ResolveOptions(ExecutionContext context, StepExecutionOptions? options)
    {
        ArgumentNullException.ThrowIfNull(context);
        return options is null || ReferenceEquals(options, StepExecutionOptions.None)
            ? new StepExecutionOptions { OnOutputLine = context.OutputLineHandler, Timeout = context.Timeout }
            : options;
    }

    /// <summary>
    /// Resolves the effective execution options for a host step.
    /// </summary>
    /// <remarks>
    /// Integration point for the job runner: once <see cref="HostExecutionContext"/> exposes
    /// <c>OutputLineHandler</c> / <c>Timeout</c>, fall back to them here (see the container overload).
    /// </remarks>
    public static StepExecutionOptions ResolveOptions(HostExecutionContext context, StepExecutionOptions? options)
    {
        ArgumentNullException.ThrowIfNull(context);
        return options is null || ReferenceEquals(options, StepExecutionOptions.None)
            ? new StepExecutionOptions { OnOutputLine = context.OutputLineHandler, Timeout = context.Timeout }
            : options;
    }

    /// <summary>
    /// Gets the timeout for a step: the step's own <c>timeout-minutes</c> wins, then the runner default.
    /// </summary>
    public static TimeSpan? GetTimeout(Step step, StepExecutionOptions options)
    {
        if (step.TimeoutMinutes is > 0)
        {
            return TimeSpan.FromMinutes(step.TimeoutMinutes.Value);
        }

        return options.Timeout;
    }

    /// <summary>
    /// Gets the stderr line callback: an explicit error handler, or the output handler so that a single
    /// live-log consumer sees both streams.
    /// </summary>
    public static Action<string>? GetErrorLineHandler(StepExecutionOptions options)
    {
        return options.OnErrorLine ?? options.OnOutputLine;
    }

    /// <summary>
    /// Merges the context environment with the step environment (step values win).
    /// </summary>
    public static Dictionary<string, string> MergeEnvironment(
        IReadOnlyDictionary<string, string>? contextEnvironment,
        IDictionary<string, string>? stepEnvironment)
    {
        var merged = contextEnvironment != null
            ? new Dictionary<string, string>(contextEnvironment)
            : new Dictionary<string, string>();

        if (stepEnvironment != null)
        {
            foreach (var (key, value) in stepEnvironment)
            {
                merged[key] = value;
            }
        }

        return merged;
    }

    /// <summary>
    /// Builds a failed step result (exit code -1 unless specified).
    /// </summary>
    public static StepExecutionResult Failed(
        string stepName,
        string errorMessage,
        DateTimeOffset startTime,
        int exitCode = -1,
        string? output = null)
    {
        var endTime = DateTimeOffset.Now;
        return new StepExecutionResult
        {
            StepName = stepName,
            Success = false,
            ExitCode = exitCode,
            Output = output ?? string.Empty,
            ErrorOutput = errorMessage,
            Duration = endTime - startTime,
            StartTime = startTime,
            EndTime = endTime
        };
    }

    /// <summary>
    /// Builds a successful step result carrying an informational note.
    /// </summary>
    public static StepExecutionResult Succeeded(string stepName, string output, DateTimeOffset startTime)
    {
        var endTime = DateTimeOffset.Now;
        return new StepExecutionResult
        {
            StepName = stepName,
            Success = true,
            ExitCode = 0,
            Output = output,
            ErrorOutput = string.Empty,
            Duration = endTime - startTime,
            StartTime = startTime,
            EndTime = endTime
        };
    }

    /// <summary>
    /// Builds a step result from a command execution result.
    /// </summary>
    /// <param name="stepName">The step name.</param>
    /// <param name="result">The command result.</param>
    /// <param name="startTime">When the step started.</param>
    /// <param name="notes">Optional informational lines prepended to the error output (e.g. warnings).</param>
    public static StepExecutionResult FromExecution(
        string stepName,
        ExecutionResult result,
        DateTimeOffset startTime,
        IEnumerable<string>? notes = null)
    {
        var endTime = DateTimeOffset.Now;
        var errorOutput = result.StandardError;

        if (notes != null)
        {
            var noteText = string.Join(Environment.NewLine, notes.Where(n => !string.IsNullOrWhiteSpace(n)));
            if (noteText.Length > 0)
            {
                errorOutput = string.IsNullOrEmpty(errorOutput)
                    ? noteText
                    : noteText + Environment.NewLine + errorOutput;
            }
        }

        return new StepExecutionResult
        {
            StepName = stepName,
            Success = result.Success,
            ExitCode = result.ExitCode,
            Output = result.StandardOutput,
            ErrorOutput = errorOutput,
            Duration = endTime - startTime,
            StartTime = startTime,
            EndTime = endTime
        };
    }

    /// <summary>
    /// Formats an exception for a step's error output, including PDK recovery suggestions when present.
    /// </summary>
    public static string FormatException(Exception exception, string? prefix = null)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrEmpty(prefix))
        {
            builder.Append(prefix).Append(": ");
        }

        builder.Append(exception.Message);

        if (exception is PdkException pdkException && pdkException.Suggestions.Count > 0)
        {
            builder.AppendLine().Append("Suggestions:");
            foreach (var suggestion in pdkException.Suggestions)
            {
                builder.AppendLine().Append("  - ").Append(suggestion);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Reads a step input by any of the given (case-insensitive) names.
    /// </summary>
    public static string? GetInput(Step step, params string[] names)
    {
        foreach (var name in names)
        {
            foreach (var (key, value) in step.With)
            {
                if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Reads a boolean step input (<c>true</c>/<c>false</c>/<c>yes</c>/<c>no</c>/<c>1</c>/<c>0</c>).
    /// </summary>
    public static bool GetBoolInput(Step step, bool defaultValue, params string[] names)
    {
        var value = GetInput(step, names);
        if (value == null)
        {
            return defaultValue;
        }

        return value.ToLowerInvariant() switch
        {
            "true" or "yes" or "1" or "on" => true,
            "false" or "no" or "0" or "off" => false,
            _ => defaultValue
        };
    }

    /// <summary>
    /// Splits a multi-value input on newlines and commas, trimming entries and dropping empty ones.
    /// </summary>
    public static IReadOnlyList<string> SplitList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        return value
            .Split(new[] { '\n', '\r', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(v => v.Trim())
            .Where(v => v.Length > 0)
            .ToList();
    }

    /// <summary>
    /// Returns true when the value still contains an unexpanded expression such as <c>${{ ... }}</c> or <c>$(...)</c>.
    /// </summary>
    public static bool IsUnexpandedExpression(string value)
    {
        return value.Contains("${{", StringComparison.Ordinal) || value.Contains("$(", StringComparison.Ordinal);
    }
}
