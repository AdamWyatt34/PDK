namespace PDK.CLI.UI;

using Spectre.Console;
using Spectre.Console.Rendering;

/// <summary>
/// Provides an abstraction over <see cref="IAnsiConsole"/> for testable console output.
/// Supports NO_COLOR environment variable and provides convenience methods for common output patterns.
/// </summary>
public interface IConsoleOutput
{
    /// <summary>
    /// Writes an informational message with blue formatting.
    /// </summary>
    /// <param name="message">The message to write.</param>
    void WriteInfo(string message);

    /// <summary>
    /// Writes a success message with green formatting.
    /// </summary>
    /// <param name="message">The message to write.</param>
    void WriteSuccess(string message);

    /// <summary>
    /// Writes an error message with red formatting.
    /// </summary>
    /// <param name="message">The message to write.</param>
    void WriteError(string message);

    /// <summary>
    /// Writes a warning message with yellow formatting.
    /// </summary>
    /// <param name="message">The message to write.</param>
    void WriteWarning(string message);

    /// <summary>
    /// Writes a debug message. Only visible when verbose mode is enabled.
    /// </summary>
    /// <param name="message">The message to write.</param>
    void WriteDebug(string message);

    /// <summary>
    /// Writes a line of text without special formatting.
    /// </summary>
    /// <param name="message">The message to write.</param>
    void WriteLine(string message);

    /// <summary>
    /// Writes an empty line.
    /// </summary>
    void WriteLine();

    /// <summary>
    /// Writes a table to the console.
    /// </summary>
    /// <param name="table">The Spectre.Console table to render.</param>
    void WriteTable(Table table);

    /// <summary>
    /// Writes a panel to the console.
    /// </summary>
    /// <param name="panel">The Spectre.Console panel to render.</param>
    void WritePanel(Panel panel);

    /// <summary>
    /// Writes any renderable object to the console.
    /// </summary>
    /// <param name="renderable">The renderable object to write.</param>
    void Write(IRenderable renderable);

    /// <summary>
    /// Gets whether color output is enabled.
    /// </summary>
    bool ColorEnabled { get; }

    /// <summary>
    /// Gets whether the terminal supports interactive features.
    /// </summary>
    bool IsInteractive { get; }

    /// <summary>
    /// Gets the terminal width in characters.
    /// </summary>
    int TerminalWidth { get; }
}

/// <summary>
/// Spectre.Console implementation of <see cref="IConsoleOutput"/>.
/// Provides colored output with accessible symbols and NO_COLOR support.
/// </summary>
public sealed class ConsoleOutput : IConsoleOutput
{
    private readonly IAnsiConsole _console;
    private readonly bool _noColor;
    private readonly bool _verbose;

    /// <summary>
    /// Initializes a new instance of <see cref="ConsoleOutput"/>.
    /// </summary>
    /// <param name="console">The Spectre.Console IAnsiConsole instance.</param>
    /// <param name="verbose">Whether verbose/debug output is enabled.</param>
    public ConsoleOutput(IAnsiConsole console, bool verbose = false)
    {
        _console = console ?? throw new ArgumentNullException(nameof(console));
        _noColor = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));
        _verbose = verbose;
    }

    /// <inheritdoc/>
    public bool ColorEnabled => !_noColor && _console.Profile.Capabilities.ColorSystem != ColorSystem.NoColors;

    /// <inheritdoc/>
    public bool IsInteractive => _console.Profile.Capabilities.Interactive;

    /// <inheritdoc/>
    public int TerminalWidth => _console.Profile.Width;

    /// <inheritdoc/>
    public void WriteInfo(string message)
    {
        if (_noColor)
        {
            _console.WriteLine($"i {message}");
        }
        else
        {
            _console.MarkupLine($"[blue]i[/] {Markup.Escape(message)}");
        }
    }

    /// <inheritdoc/>
    public void WriteSuccess(string message)
    {
        if (_noColor)
        {
            _console.WriteLine($"+ {message}");
        }
        else
        {
            _console.MarkupLine($"[green]+[/] {Markup.Escape(message)}");
        }
    }

    /// <inheritdoc/>
    public void WriteError(string message)
    {
        if (_noColor)
        {
            _console.WriteLine($"x Error: {message}");
        }
        else
        {
            _console.MarkupLine($"[red]x Error:[/] {Markup.Escape(message)}");
        }
    }

    /// <inheritdoc/>
    public void WriteWarning(string message)
    {
        if (_noColor)
        {
            _console.WriteLine($"! Warning: {message}");
        }
        else
        {
            _console.MarkupLine($"[yellow]! Warning:[/] {Markup.Escape(message)}");
        }
    }

    /// <inheritdoc/>
    public void WriteDebug(string message)
    {
        if (!_verbose)
        {
            return;
        }

        if (_noColor)
        {
            _console.WriteLine($"[DEBUG] {message}");
        }
        else
        {
            // Escape brackets in [DEBUG] to prevent markup interpretation
            _console.MarkupLine($"[dim][[DEBUG]][/] {Markup.Escape(message)}");
        }
    }

    /// <inheritdoc/>
    public void WriteLine(string message)
    {
        _console.WriteLine(message);
    }

    /// <inheritdoc/>
    public void WriteLine()
    {
        _console.WriteLine();
    }

    /// <inheritdoc/>
    public void WriteTable(Table table)
    {
        _console.Write(table);
    }

    /// <inheritdoc/>
    public void WritePanel(Panel panel)
    {
        _console.Write(panel);
    }

    /// <inheritdoc/>
    public void Write(IRenderable renderable)
    {
        _console.Write(renderable);
    }
}
