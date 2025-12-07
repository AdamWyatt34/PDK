namespace PDK.CLI.UI;

/// <summary>
/// Provides step status visualization with symbols and colors.
/// Supports NO_COLOR environment variable for accessibility (REQ-06-011).
/// </summary>
public static class StepStatusDisplay
{
    /// <summary>
    /// Represents the execution status of a pipeline step.
    /// </summary>
    public enum StepStatus
    {
        /// <summary>Step has not started yet.</summary>
        Pending,

        /// <summary>Step is currently executing.</summary>
        Running,

        /// <summary>Step completed successfully.</summary>
        Success,

        /// <summary>Step failed.</summary>
        Failed,

        /// <summary>Step was skipped.</summary>
        Skipped
    }

    /// <summary>
    /// Symbol for pending steps.
    /// </summary>
    public const string PendingSymbol = "○";

    /// <summary>
    /// Symbol for running steps.
    /// </summary>
    public const string RunningSymbol = "◎";

    /// <summary>
    /// Symbol for successful steps.
    /// </summary>
    public const string SuccessSymbol = "✓";

    /// <summary>
    /// Symbol for failed steps.
    /// </summary>
    public const string FailedSymbol = "✗";

    /// <summary>
    /// Symbol for skipped steps.
    /// </summary>
    public const string SkippedSymbol = "⊘";

    /// <summary>
    /// Plain text symbol for pending steps (NO_COLOR mode).
    /// </summary>
    public const string PendingSymbolPlain = "o";

    /// <summary>
    /// Plain text symbol for running steps (NO_COLOR mode).
    /// </summary>
    public const string RunningSymbolPlain = "*";

    /// <summary>
    /// Plain text symbol for successful steps (NO_COLOR mode).
    /// </summary>
    public const string SuccessSymbolPlain = "+";

    /// <summary>
    /// Plain text symbol for failed steps (NO_COLOR mode).
    /// </summary>
    public const string FailedSymbolPlain = "x";

    /// <summary>
    /// Plain text symbol for skipped steps (NO_COLOR mode).
    /// </summary>
    public const string SkippedSymbolPlain = "-";

    /// <summary>
    /// Gets the appropriate symbol for a step status.
    /// </summary>
    /// <param name="status">The step status.</param>
    /// <param name="noColor">Whether to use plain text symbols (NO_COLOR mode).</param>
    /// <returns>The symbol representing the status.</returns>
    public static string GetSymbol(StepStatus status, bool noColor = false)
    {
        if (noColor)
        {
            return status switch
            {
                StepStatus.Pending => PendingSymbolPlain,
                StepStatus.Running => RunningSymbolPlain,
                StepStatus.Success => SuccessSymbolPlain,
                StepStatus.Failed => FailedSymbolPlain,
                StepStatus.Skipped => SkippedSymbolPlain,
                _ => "?"
            };
        }

        return status switch
        {
            StepStatus.Pending => PendingSymbol,
            StepStatus.Running => RunningSymbol,
            StepStatus.Success => SuccessSymbol,
            StepStatus.Failed => FailedSymbol,
            StepStatus.Skipped => SkippedSymbol,
            _ => "?"
        };
    }

    /// <summary>
    /// Gets the Spectre.Console color name for a step status.
    /// </summary>
    /// <param name="status">The step status.</param>
    /// <returns>The color name for markup.</returns>
    public static string GetColor(StepStatus status)
    {
        return status switch
        {
            StepStatus.Pending => "dim",
            StepStatus.Running => "cyan",
            StepStatus.Success => "green",
            StepStatus.Failed => "red",
            StepStatus.Skipped => "yellow",
            _ => "white"
        };
    }

    /// <summary>
    /// Formats a step status with symbol and name for display.
    /// </summary>
    /// <param name="status">The step status.</param>
    /// <param name="stepName">The name of the step.</param>
    /// <param name="noColor">Whether to use plain text without colors.</param>
    /// <returns>Formatted string for console output.</returns>
    public static string FormatStatus(StepStatus status, string stepName, bool noColor = false)
    {
        var symbol = GetSymbol(status, noColor);

        if (noColor)
        {
            return $"{symbol} {stepName}";
        }

        var color = GetColor(status);
        var escapedName = Spectre.Console.Markup.Escape(stepName);
        return $"[{color}]{symbol}[/] {escapedName}";
    }

    /// <summary>
    /// Formats a step status with symbol, name, and duration for display.
    /// </summary>
    /// <param name="status">The step status.</param>
    /// <param name="stepName">The name of the step.</param>
    /// <param name="duration">The execution duration.</param>
    /// <param name="noColor">Whether to use plain text without colors.</param>
    /// <returns>Formatted string for console output.</returns>
    public static string FormatStatusWithDuration(
        StepStatus status,
        string stepName,
        TimeSpan duration,
        bool noColor = false)
    {
        var symbol = GetSymbol(status, noColor);
        var durationStr = FormatDuration(duration);

        if (noColor)
        {
            return $"{symbol} {stepName} ({durationStr})";
        }

        var color = GetColor(status);
        var escapedName = Spectre.Console.Markup.Escape(stepName);
        return $"[{color}]{symbol}[/] {escapedName} [dim]({durationStr})[/]";
    }

    /// <summary>
    /// Formats a duration for display.
    /// </summary>
    /// <param name="duration">The duration to format.</param>
    /// <returns>Human-readable duration string.</returns>
    public static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalMinutes >= 1)
        {
            return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
        }
        return $"{duration.TotalSeconds:F2}s";
    }
}
