namespace PDK.Core.Models;

/// <summary>
/// Represents the type of step in a pipeline.
/// </summary>
public enum StepType
{
    /// <summary>
    /// Unknown or unrecognized step type.
    /// </summary>
    Unknown,

    /// <summary>
    /// Source code checkout step.
    /// </summary>
    Checkout,

    /// <summary>
    /// Generic script execution step.
    /// </summary>
    Script,

    /// <summary>
    /// Docker-related step.
    /// </summary>
    Docker,

    /// <summary>
    /// NPM/Node.js-related step.
    /// </summary>
    Npm,

    /// <summary>
    /// .NET CLI step.
    /// </summary>
    Dotnet,

    /// <summary>
    /// Python-related step.
    /// </summary>
    Python,

    /// <summary>
    /// Maven build step.
    /// </summary>
    Maven,

    /// <summary>
    /// Gradle build step.
    /// </summary>
    Gradle,

    /// <summary>
    /// PowerShell script step.
    /// </summary>
    PowerShell,

    /// <summary>
    /// Bash script step.
    /// </summary>
    Bash,

    /// <summary>
    /// Generic file operation step (copy, move, etc.).
    /// </summary>
    FileOperation,

    /// <summary>
    /// Artifact upload step.
    /// </summary>
    UploadArtifact,

    /// <summary>
    /// Artifact download step.
    /// </summary>
    DownloadArtifact
}