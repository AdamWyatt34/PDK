namespace PDK.Runners.StepExecutors;

using PDK.Core.Models;

/// <summary>
/// Maps <see cref="StepType"/> values to executor names for Docker and host mode.
/// <see cref="StepType.Unknown"/> and <see cref="StepType.Setup"/> have no executor: the job runners handle
/// them (skip with a warning / logged no-op) before consulting the factories.
/// </summary>
public static class StepTypeMapping
{
    /// <summary>
    /// Gets the Docker executor name for a step type, or null when the step type has no executor
    /// (Unknown, Setup).
    /// </summary>
    public static string? GetDockerExecutorName(StepType stepType)
    {
        return stepType switch
        {
            StepType.Checkout => "checkout",
            StepType.Script => "script",
            StepType.Bash => "script",
            StepType.PowerShell => "pwsh",
            StepType.Docker => "docker",
            StepType.Npm => "npm",
            StepType.Dotnet => "dotnet",
            StepType.Python => "python",
            StepType.Maven => "maven",
            StepType.Gradle => "gradle",
            StepType.FileOperation => "fileoperation",
            StepType.UploadArtifact => "uploadartifact",
            StepType.DownloadArtifact => "downloadartifact",
            _ => null
        };
    }

    /// <summary>
    /// Gets the host executor name for a step type, or null when the step type has no executor
    /// (Unknown, Setup). PowerShell steps run through the host script executor, which handles pwsh/powershell.
    /// </summary>
    public static string? GetHostExecutorName(StepType stepType)
    {
        return stepType switch
        {
            StepType.PowerShell => "script",
            _ => GetDockerExecutorName(stepType)
        };
    }
}
