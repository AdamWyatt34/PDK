using Spectre.Console;

namespace PDK.Cli.Filtering;

/// <summary>
/// Provides interactive confirmation for filtered execution.
/// </summary>
public class FilterConfirmationPrompt
{
    private readonly IAnsiConsole _console;

    /// <summary>
    /// Initializes a new instance of the <see cref="FilterConfirmationPrompt"/> class.
    /// </summary>
    /// <param name="console">The Spectre.Console instance.</param>
    public FilterConfirmationPrompt(IAnsiConsole console)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
    }

    /// <summary>
    /// Prompts the user to confirm execution.
    /// </summary>
    /// <param name="preview">The filter preview to show.</param>
    /// <returns>True if the user confirmed, false otherwise.</returns>
    public bool Confirm(FilterPreview preview)
    {
        var previewUI = new FilterPreviewUI(_console);
        previewUI.Display(preview);

        return PromptForConfirmation();
    }

    /// <summary>
    /// Prompts the user to confirm execution without displaying preview.
    /// </summary>
    /// <returns>True if the user confirmed, false otherwise.</returns>
    public bool PromptForConfirmation()
    {
        var noColor = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));

        if (noColor)
        {
            _console.WriteLine("Proceed with filtered execution? (y/n): ");
        }
        else
        {
            _console.Markup("[yellow]Proceed with filtered execution?[/] ");
        }

        // Check if we're in an interactive terminal
        if (!_console.Profile.Capabilities.Interactive)
        {
            if (noColor)
            {
                _console.WriteLine("Non-interactive mode. Aborting.");
            }
            else
            {
                _console.MarkupLine("[red]Non-interactive mode. Aborting.[/]");
            }
            return false;
        }

        try
        {
            var confirmed = _console.Confirm(string.Empty, false);
            return confirmed;
        }
        catch (Exception)
        {
            // If confirmation fails (e.g., non-interactive), default to no
            return false;
        }
    }

    /// <summary>
    /// Prompts the user with a custom message.
    /// </summary>
    /// <param name="message">The message to display.</param>
    /// <param name="defaultValue">The default value if user just presses Enter.</param>
    /// <returns>True if the user confirmed, false otherwise.</returns>
    public bool PromptWithMessage(string message, bool defaultValue = false)
    {
        if (!_console.Profile.Capabilities.Interactive)
        {
            return defaultValue;
        }

        return _console.Confirm(message, defaultValue);
    }
}
