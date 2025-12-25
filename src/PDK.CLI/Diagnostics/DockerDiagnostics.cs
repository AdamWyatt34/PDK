using PDK.Core.Docker;
using Spectre.Console;

namespace PDK.CLI.Diagnostics;

/// <summary>
/// Provides Docker diagnostic and error message formatting utilities.
/// Implements REQ-DK-007: Docker Availability Detection error messaging.
/// </summary>
public static class DockerDiagnostics
{
    /// <summary>
    /// Displays Docker availability status with formatted output.
    /// Shows success message with version info or detailed error with suggestions.
    /// </summary>
    /// <param name="status">The Docker availability status to display.</param>
    public static void DisplayDockerStatus(DockerAvailabilityStatus status)
    {
        if (status.IsAvailable)
        {
            DisplaySuccess(status);
        }
        else
        {
            DisplayError(status);
        }
    }

    /// <summary>
    /// Displays a success message when Docker is available.
    /// </summary>
    /// <param name="status">The successful Docker status.</param>
    private static void DisplaySuccess(DockerAvailabilityStatus status)
    {
        AnsiConsole.MarkupLine("[green]✓ Docker is available[/]");

        if (!string.IsNullOrEmpty(status.Version))
        {
            AnsiConsole.MarkupLine($"[green]✓ Version:[/] {status.Version}");
        }

        if (!string.IsNullOrEmpty(status.Platform))
        {
            AnsiConsole.MarkupLine($"[green]✓ Platform:[/] {status.Platform}");
        }

        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Displays an error message when Docker is not available.
    /// Includes suggestions based on the error type (REQ-DK-007).
    /// </summary>
    /// <param name="status">The failed Docker status.</param>
    private static void DisplayError(DockerAvailabilityStatus status)
    {
        AnsiConsole.MarkupLine("[red]✗ Docker is not available[/]");
        AnsiConsole.WriteLine();

        if (!string.IsNullOrEmpty(status.ErrorMessage))
        {
            AnsiConsole.MarkupLine($"[yellow]Problem:[/] {status.ErrorMessage}");
            AnsiConsole.WriteLine();
        }

        if (status.ErrorType.HasValue)
        {
            var suggestions = GetSuggestionsForError(status.ErrorType.Value);
            if (suggestions.Count > 0)
            {
                AnsiConsole.MarkupLine("[yellow]Solutions:[/]");
                foreach (var suggestion in suggestions)
                {
                    AnsiConsole.MarkupLine($"  [dim]•[/] {suggestion}");
                }
                AnsiConsole.WriteLine();
            }
        }
    }

    /// <summary>
    /// Gets actionable suggestions based on the Docker error type.
    /// Implements REQ-DK-007 error messaging requirements.
    /// </summary>
    /// <param name="errorType">The type of Docker error.</param>
    /// <returns>A list of actionable suggestions.</returns>
    public static List<string> GetSuggestionsForError(DockerErrorType errorType)
    {
        return errorType switch
        {
            DockerErrorType.NotInstalled => new List<string>
            {
                "Install Docker Desktop: [link]https://docker.com/get-started[/]",
                "Alternative: Use host mode (no Docker required): [cyan]pdk run --host[/]"
            },

            DockerErrorType.NotRunning => new List<string>
            {
                "Start Docker Desktop",
                "Linux users: Run [cyan]sudo systemctl start docker[/]",
                "Alternative: Use host mode (no Docker required): [cyan]pdk run --host[/]"
            },

            DockerErrorType.PermissionDenied => new List<string>
            {
                "Add your user to the docker group: [cyan]sudo usermod -aG docker $USER[/]",
                "Then log out and log back in for changes to take effect",
                "Alternative: Use host mode (no Docker required): [cyan]pdk run --host[/]"
            },

            DockerErrorType.Unknown => new List<string>
            {
                "Check if Docker is installed and running",
                "Try restarting Docker Desktop",
                "Alternative: Use host mode (no Docker required): [cyan]pdk run --host[/]"
            },

            _ => new List<string>()
        };
    }

    /// <summary>
    /// Displays a quick error message for use in the run command.
    /// Shows a compact error with primary suggestion.
    /// </summary>
    /// <param name="status">The failed Docker status.</param>
    public static void DisplayQuickError(DockerAvailabilityStatus status)
    {
        AnsiConsole.MarkupLine($"[red]Error:[/] {status.ErrorMessage ?? "Docker is not available"}");

        if (status.ErrorType.HasValue)
        {
            var suggestions = GetSuggestionsForError(status.ErrorType.Value);
            if (suggestions.Count > 0)
            {
                AnsiConsole.MarkupLine($"[dim]Suggestion:[/] {suggestions[0]}");
                AnsiConsole.MarkupLine("[dim]Run[/] [cyan]pdk doctor[/] [dim]for more details.[/]");
            }
        }
    }
}
