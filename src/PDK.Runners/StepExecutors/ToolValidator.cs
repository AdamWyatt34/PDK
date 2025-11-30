namespace PDK.Runners.StepExecutors;

using IContainerManager = PDK.Runners.IContainerManager;
using PDK.Runners.Models;

/// <summary>
/// Validates tool availability in containers and provides helpful error messages.
/// </summary>
public static class ToolValidator
{
    /// <summary>
    /// Checks if a tool is available in the container.
    /// </summary>
    /// <param name="containerManager">The container manager to use for command execution.</param>
    /// <param name="containerId">The ID of the container to check.</param>
    /// <param name="toolName">The name of the tool to check (e.g., "dotnet", "npm", "docker").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the tool is available; otherwise, false.</returns>
    public static async Task<bool> IsToolAvailableAsync(
        IContainerManager containerManager,
        string containerId,
        string toolName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await containerManager.ExecuteCommandAsync(
                containerId,
                $"command -v {toolName}",
                cancellationToken: cancellationToken);

            return result.ExitCode == 0;
        }
        catch
        {
            // If command execution fails, assume tool is not available
            return false;
        }
    }

    /// <summary>
    /// Gets the version of a tool in the container.
    /// </summary>
    /// <param name="containerManager">The container manager to use for command execution.</param>
    /// <param name="containerId">The ID of the container to check.</param>
    /// <param name="toolName">The name of the tool (e.g., "dotnet", "npm", "docker").</param>
    /// <param name="versionFlag">The flag to use to get the version (defaults to "--version").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The version string if successful; otherwise, null.</returns>
    public static async Task<string?> GetToolVersionAsync(
        IContainerManager containerManager,
        string containerId,
        string toolName,
        string versionFlag = "--version",
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await containerManager.ExecuteCommandAsync(
                containerId,
                $"{toolName} {versionFlag}",
                cancellationToken: cancellationToken);

            return result.ExitCode == 0 ? result.StandardOutput.Trim() : null;
        }
        catch
        {
            // If command execution fails, return null
            return null;
        }
    }

    /// <summary>
    /// Validates that a tool is available in the container, throwing a detailed exception if not.
    /// </summary>
    /// <param name="containerManager">The container manager to use for command execution.</param>
    /// <param name="containerId">The ID of the container to check.</param>
    /// <param name="toolName">The name of the tool to validate (e.g., "dotnet", "npm", "docker").</param>
    /// <param name="imageName">The container image name (for error messages).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ToolNotFoundException">Thrown when the tool is not available in the container.</exception>
    public static async Task ValidateToolOrThrowAsync(
        IContainerManager containerManager,
        string containerId,
        string toolName,
        string imageName,
        CancellationToken cancellationToken = default)
    {
        var isAvailable = await IsToolAvailableAsync(
            containerManager,
            containerId,
            toolName,
            cancellationToken);

        if (!isAvailable)
        {
            var suggestions = GetToolSuggestions(toolName);
            throw new ToolNotFoundException(toolName, imageName, suggestions);
        }
    }

    /// <summary>
    /// Gets tool-specific suggestions for resolving missing tool issues.
    /// </summary>
    /// <param name="toolName">The name of the tool.</param>
    /// <returns>A list of suggested solutions.</returns>
    private static IReadOnlyList<string> GetToolSuggestions(string toolName)
    {
        return toolName.ToLowerInvariant() switch
        {
            "dotnet" => new[]
            {
                "Use an appropriate image with .NET pre-installed: mcr.microsoft.com/dotnet/sdk:8.0",
                "Install .NET SDK in your setup steps",
                "Switch to a runner that includes .NET SDK"
            },
            "npm" => new[]
            {
                "Use an appropriate image with npm pre-installed: node:18 or node:20",
                "Install Node.js and npm in your setup steps",
                "Switch to a runner that includes Node.js"
            },
            "node" => new[]
            {
                "Use an appropriate image with Node.js pre-installed: node:18 or node:20",
                "Install Node.js in your setup steps",
                "Switch to a runner that includes Node.js"
            },
            "docker" => new[]
            {
                "Use an appropriate image with Docker CLI pre-installed: docker:latest or docker:dind",
                "Install Docker CLI in your setup steps",
                "Ensure Docker socket is mounted: -v /var/run/docker.sock:/var/run/docker.sock",
                "Note: PDK automatically mounts the Docker socket, but Docker CLI must be in your image"
            },
            _ => new[]
            {
                $"Use an image with {toolName} pre-installed",
                $"Install {toolName} in your setup steps",
                $"Switch to a runner that includes {toolName}"
            }
        };
    }
}
