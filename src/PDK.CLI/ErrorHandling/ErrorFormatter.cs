namespace PDK.CLI.ErrorHandling;

using PDK.Core.ErrorHandling;
using PDK.Core.Logging;
using PDK.Core.Models;
using Spectre.Console;

/// <summary>
/// Formats PdkExceptions for display using Spectre.Console.
/// </summary>
public sealed class ErrorFormatter
{
    private readonly IAnsiConsole _console;
    private readonly ISecretMasker? _secretMasker;
    private readonly ErrorSuggestionEngine _suggestionEngine;

    /// <summary>
    /// Default number of lines to show from output.
    /// </summary>
    public const int DefaultMaxOutputLines = 20;

    /// <summary>
    /// Initializes a new instance of the <see cref="ErrorFormatter"/> class.
    /// </summary>
    /// <param name="console">The console to write to.</param>
    /// <param name="secretMasker">Optional secret masker for hiding sensitive data.</param>
    public ErrorFormatter(IAnsiConsole console, ISecretMasker? secretMasker = null)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _secretMasker = secretMasker;
        _suggestionEngine = new ErrorSuggestionEngine();
    }

    /// <summary>
    /// Formats and displays a PdkException with full context and suggestions.
    /// </summary>
    /// <param name="exception">The exception to display.</param>
    /// <param name="verbose">Whether to show verbose output including stack trace.</param>
    public void DisplayError(PdkException exception, bool verbose = false)
    {
        ArgumentNullException.ThrowIfNull(exception);

        // Create and display the error panel
        var panel = CreateErrorPanel(exception);
        _console.Write(panel);
        _console.WriteLine();

        // Display context if available
        if (exception.HasContext)
        {
            DisplayContext(exception.Context);
        }

        // Display suggestions
        var suggestions = exception.HasSuggestions
            ? exception.Suggestions
            : _suggestionEngine.GetSuggestions(exception);

        if (suggestions.Count > 0)
        {
            DisplaySuggestions(suggestions);
        }

        // Display output context if available
        if (!string.IsNullOrEmpty(exception.Context.ErrorOutput))
        {
            DisplayOutputContext("Error Output", exception.Context.ErrorOutput);
        }
        else if (!string.IsNullOrEmpty(exception.Context.Output) && verbose)
        {
            DisplayOutputContext("Output", exception.Context.Output);
        }

        // Display troubleshooting command if available
        var troubleshootingCmd = _suggestionEngine.GetTroubleshootingCommand(exception);
        if (!string.IsNullOrEmpty(troubleshootingCmd))
        {
            DisplayTroubleshootingCommand(troubleshootingCmd);
        }

        // Display documentation link
        DisplayDocumentationLink(exception.ErrorCode);

        // Display stack trace in verbose mode
        if (verbose && exception.StackTrace != null)
        {
            DisplayStackTrace(exception);
        }
    }

    /// <summary>
    /// Formats and displays a generic exception.
    /// </summary>
    /// <param name="exception">The exception to display.</param>
    /// <param name="verbose">Whether to show verbose output including stack trace.</param>
    public void DisplayError(Exception exception, bool verbose = false)
    {
        ArgumentNullException.ThrowIfNull(exception);

        // If it's already a PdkException, use the specialized method
        if (exception is PdkException pdkException)
        {
            DisplayError(pdkException, verbose);
            return;
        }

        // Wrap the generic exception in a PdkException
        var wrapped = WrapException(exception);
        DisplayError(wrapped, verbose);
    }

    /// <summary>
    /// Creates a Spectre Panel for the error.
    /// </summary>
    /// <param name="exception">The exception to create a panel for.</param>
    /// <returns>A Spectre Panel.</returns>
    public Panel CreateErrorPanel(PdkException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var message = MaskSecrets(exception.Message);
        var description = ErrorCodes.GetDescription(exception.ErrorCode);

        var content = new Markup($"[white]{Markup.Escape(message)}[/]");

        // Add description if different from message
        if (!string.IsNullOrEmpty(description) &&
            !message.Contains(description, StringComparison.OrdinalIgnoreCase))
        {
            content = new Markup($"[white]{Markup.Escape(message)}[/]\n\n[dim]{Markup.Escape(description)}[/]");
        }

        var panel = new Panel(content)
        {
            Header = new PanelHeader($"[red]Error {exception.ErrorCode}[/]"),
            Border = BoxBorder.Rounded,
            BorderStyle = new Style(Color.Red),
            Padding = new Padding(1, 0, 1, 0)
        };

        return panel;
    }

    /// <summary>
    /// Formats suggestions as a bulleted list.
    /// </summary>
    /// <param name="suggestions">The suggestions to format.</param>
    /// <returns>A formatted string of suggestions.</returns>
    public string FormatSuggestions(IEnumerable<string> suggestions)
    {
        var lines = suggestions.Select(s => $"  [cyan]\u2022[/] {Markup.Escape(s)}");
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Formats output with last N lines and secret masking.
    /// </summary>
    /// <param name="output">The output to format.</param>
    /// <param name="maxLines">Maximum number of lines to include.</param>
    /// <returns>The formatted output.</returns>
    public string FormatOutputContext(string? output, int maxLines = DefaultMaxOutputLines)
    {
        if (string.IsNullOrEmpty(output))
        {
            return string.Empty;
        }

        var lines = output.Split('\n');
        var relevantLines = lines.TakeLast(maxLines);
        var result = string.Join(Environment.NewLine, relevantLines);

        return MaskSecrets(result);
    }

    private void DisplayContext(ErrorContext context)
    {
        _console.MarkupLine("[yellow]Context:[/]");

        if (!string.IsNullOrEmpty(context.PipelineFile))
        {
            _console.MarkupLine($"  Pipeline: [dim]{Markup.Escape(context.PipelineFile)}[/]");
        }

        if (!string.IsNullOrEmpty(context.JobName))
        {
            _console.MarkupLine($"  Job: [dim]{Markup.Escape(context.JobName)}[/]");
        }

        if (!string.IsNullOrEmpty(context.StepName))
        {
            _console.MarkupLine($"  Step: [dim]{Markup.Escape(context.StepName)}[/]");
        }

        if (context.LineNumber.HasValue)
        {
            var location = context.ColumnNumber.HasValue
                ? $"Line {context.LineNumber}, Column {context.ColumnNumber}"
                : $"Line {context.LineNumber}";
            _console.MarkupLine($"  Location: [dim]{location}[/]");
        }

        if (context.ExitCode.HasValue)
        {
            _console.MarkupLine($"  Exit Code: [dim]{context.ExitCode}[/]");
        }

        if (!string.IsNullOrEmpty(context.ImageName))
        {
            _console.MarkupLine($"  Image: [dim]{Markup.Escape(context.ImageName)}[/]");
        }

        if (!string.IsNullOrEmpty(context.ContainerId))
        {
            var shortId = context.ContainerId.Length > 12
                ? context.ContainerId[..12]
                : context.ContainerId;
            _console.MarkupLine($"  Container: [dim]{Markup.Escape(shortId)}[/]");
        }

        if (context.Duration.HasValue)
        {
            _console.MarkupLine($"  Duration: [dim]{context.Duration.Value.TotalSeconds:F2}s[/]");
        }

        _console.WriteLine();
    }

    private void DisplaySuggestions(IReadOnlyList<string> suggestions)
    {
        _console.MarkupLine("[green]Suggestions:[/]");

        foreach (var suggestion in suggestions)
        {
            var masked = MaskSecrets(suggestion);
            _console.MarkupLine($"  [cyan]\u2022[/] {Markup.Escape(masked)}");
        }

        _console.WriteLine();
    }

    private void DisplayOutputContext(string title, string output)
    {
        _console.MarkupLine($"[yellow]{Markup.Escape(title)}:[/]");

        var formatted = FormatOutputContext(output);
        var lines = formatted.Split(Environment.NewLine);

        foreach (var line in lines)
        {
            _console.MarkupLine($"  [dim]{Markup.Escape(line)}[/]");
        }

        if (output.Split('\n').Length > DefaultMaxOutputLines)
        {
            _console.MarkupLine($"  [dim]... ({output.Split('\n').Length - DefaultMaxOutputLines} more lines)[/]");
        }

        _console.WriteLine();
    }

    private void DisplayTroubleshootingCommand(string command)
    {
        _console.MarkupLine("[yellow]Try running:[/]");
        _console.MarkupLine($"  [cyan]{Markup.Escape(command)}[/]");
        _console.WriteLine();
    }

    private void DisplayDocumentationLink(string errorCode)
    {
        var url = _suggestionEngine.GetDocumentationUrl(errorCode);
        _console.MarkupLine("[dim]Documentation:[/]");
        _console.MarkupLine($"  [link={url}]{url}[/]");
        _console.WriteLine();
    }

    private void DisplayStackTrace(Exception exception)
    {
        _console.MarkupLine("[dim]Stack Trace:[/]");

        if (exception.StackTrace != null)
        {
            var lines = exception.StackTrace.Split(Environment.NewLine);
            foreach (var line in lines.Take(20))
            {
                _console.MarkupLine($"  [dim]{Markup.Escape(line.Trim())}[/]");
            }

            if (lines.Length > 20)
            {
                _console.MarkupLine($"  [dim]... ({lines.Length - 20} more frames)[/]");
            }
        }

        // Display inner exception if present
        if (exception.InnerException != null)
        {
            _console.WriteLine();
            _console.MarkupLine("[dim]Inner Exception:[/]");
            _console.MarkupLine($"  [dim]{Markup.Escape(exception.InnerException.Message)}[/]");

            if (exception.InnerException.StackTrace != null)
            {
                var innerLines = exception.InnerException.StackTrace.Split(Environment.NewLine).Take(10);
                foreach (var line in innerLines)
                {
                    _console.MarkupLine($"  [dim]{Markup.Escape(line.Trim())}[/]");
                }
            }
        }

        _console.WriteLine();
    }

    private string MaskSecrets(string text)
    {
        if (string.IsNullOrEmpty(text) || _secretMasker == null)
        {
            return text;
        }

        return _secretMasker.MaskSecrets(text);
    }

    private static PdkException WrapException(Exception exception)
    {
        // Try to determine the appropriate error code
        var errorCode = exception switch
        {
            FileNotFoundException => ErrorCodes.FileNotFound,
            DirectoryNotFoundException => ErrorCodes.DirectoryNotFound,
            UnauthorizedAccessException => ErrorCodes.FileAccessDenied,
            TimeoutException => ErrorCodes.NetworkTimeout,
            OperationCanceledException => ErrorCodes.Unknown,
            _ => ErrorCodes.Unknown
        };

        return new PdkException(errorCode, exception.Message, null, null, exception);
    }
}
