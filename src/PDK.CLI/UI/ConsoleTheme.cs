namespace PDK.CLI.UI;

using Microsoft.Extensions.Logging;
using Spectre.Console;

/// <summary>
/// Defines color themes for PDK console output with semantic meaning.
/// Colors are chosen for clarity and accessibility across different terminal themes.
/// </summary>
public static class ConsoleTheme
{
    // Log level colors
    /// <summary>Color for error messages.</summary>
    public static readonly Color Error = Color.Red;

    /// <summary>Color for warning messages.</summary>
    public static readonly Color Warning = Color.Yellow;

    /// <summary>Color for informational messages.</summary>
    public static readonly Color Info = Color.Blue;

    /// <summary>Color for success messages.</summary>
    public static readonly Color Success = Color.Green;

    /// <summary>Color for debug messages.</summary>
    public static readonly Color Debug = Color.Grey;

    /// <summary>Color for trace messages (most verbose).</summary>
    public static readonly Color Trace = Color.DarkSlateGray1;

    // Status indicator colors
    /// <summary>Color for running/in-progress status.</summary>
    public static readonly Color Running = Color.Blue;

    /// <summary>Color for skipped/cancelled status.</summary>
    public static readonly Color Skipped = Color.Grey;

    /// <summary>Color for pending/waiting status.</summary>
    public static readonly Color Pending = Color.White;

    // Component colors
    /// <summary>Color for job names and headers.</summary>
    public static readonly Color Job = Color.Cyan1;

    /// <summary>Color for step names.</summary>
    public static readonly Color Step = Color.White;

    /// <summary>Color for duration/timing information.</summary>
    public static readonly Color Duration = Color.Grey;

    /// <summary>Color for correlation IDs.</summary>
    public static readonly Color CorrelationId = Color.DarkSlateGray1;

    /// <summary>Color for muted/secondary text.</summary>
    public static readonly Color Muted = Color.Grey;

    /// <summary>Color for emphasized text.</summary>
    public static readonly Color Emphasis = Color.Yellow;

    /// <summary>Color for headers and titles.</summary>
    public static readonly Color Header = Color.Cyan1;

    /// <summary>Color for subheaders.</summary>
    public static readonly Color Subheader = Color.Blue;

    /// <summary>
    /// Gets the appropriate color for a log level.
    /// </summary>
    /// <param name="level">The log level.</param>
    /// <returns>The corresponding color.</returns>
    public static Color ForLevel(LogLevel level) => level switch
    {
        LogLevel.Trace => Trace,
        LogLevel.Debug => Debug,
        LogLevel.Information => Info,
        LogLevel.Warning => Warning,
        LogLevel.Error => Error,
        LogLevel.Critical => Error,
        _ => Info
    };

    /// <summary>
    /// Gets the appropriate color for a step execution result.
    /// </summary>
    /// <param name="success">Whether the step succeeded.</param>
    /// <param name="skipped">Whether the step was skipped.</param>
    /// <returns>The corresponding color.</returns>
    public static Color ForStepResult(bool success, bool skipped = false)
    {
        if (skipped) return Skipped;
        return success ? Success : Error;
    }

    /// <summary>
    /// Gets the symbol for a step result (respects NO_COLOR).
    /// </summary>
    /// <param name="success">Whether the step succeeded.</param>
    /// <param name="skipped">Whether the step was skipped.</param>
    /// <param name="noColor">Whether NO_COLOR mode is enabled.</param>
    /// <returns>The appropriate symbol.</returns>
    public static string GetResultSymbol(bool success, bool skipped = false, bool noColor = false)
    {
        if (skipped) return noColor ? "-" : "[grey]-[/]";
        return success
            ? (noColor ? "+" : "[green]+[/]")
            : (noColor ? "x" : "[red]x[/]");
    }

    /// <summary>
    /// Gets the symbol for a log level (respects NO_COLOR).
    /// </summary>
    /// <param name="level">The log level.</param>
    /// <param name="noColor">Whether NO_COLOR mode is enabled.</param>
    /// <returns>The appropriate symbol or formatted string.</returns>
    public static string GetLevelSymbol(LogLevel level, bool noColor = false)
    {
        return level switch
        {
            LogLevel.Trace => noColor ? "[TRC]" : $"[{Trace.ToMarkup()}]TRC[/]",
            LogLevel.Debug => noColor ? "[DBG]" : $"[{Debug.ToMarkup()}]DBG[/]",
            LogLevel.Information => noColor ? "[INF]" : $"[{Info.ToMarkup()}]INF[/]",
            LogLevel.Warning => noColor ? "[WRN]" : $"[{Warning.ToMarkup()}]WRN[/]",
            LogLevel.Error => noColor ? "[ERR]" : $"[{Error.ToMarkup()}]ERR[/]",
            LogLevel.Critical => noColor ? "[CRT]" : $"[{Error.ToMarkup()}]CRT[/]",
            _ => noColor ? "[INF]" : $"[{Info.ToMarkup()}]INF[/]"
        };
    }

    /// <summary>
    /// Formats text with the specified color, or returns plain text if NO_COLOR is set.
    /// </summary>
    /// <param name="text">The text to format.</param>
    /// <param name="color">The color to apply.</param>
    /// <param name="noColor">Whether NO_COLOR mode is enabled.</param>
    /// <returns>The formatted or plain text.</returns>
    public static string Format(string text, Color color, bool noColor = false)
    {
        if (noColor)
        {
            return text;
        }
        return $"[{color.ToMarkup()}]{Markup.Escape(text)}[/]";
    }

    /// <summary>
    /// Formats text as bold, or returns plain text if NO_COLOR is set.
    /// </summary>
    /// <param name="text">The text to format.</param>
    /// <param name="noColor">Whether NO_COLOR mode is enabled.</param>
    /// <returns>The formatted or plain text.</returns>
    public static string Bold(string text, bool noColor = false)
    {
        if (noColor)
        {
            return text;
        }
        return $"[bold]{Markup.Escape(text)}[/]";
    }

    /// <summary>
    /// Formats text as dim/muted, or returns plain text if NO_COLOR is set.
    /// </summary>
    /// <param name="text">The text to format.</param>
    /// <param name="noColor">Whether NO_COLOR mode is enabled.</param>
    /// <returns>The formatted or plain text.</returns>
    public static string Dim(string text, bool noColor = false)
    {
        if (noColor)
        {
            return text;
        }
        return $"[dim]{Markup.Escape(text)}[/]";
    }
}
