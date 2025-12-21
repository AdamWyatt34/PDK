using PDK.Core.Artifacts;

namespace PDK.Core.Models;

/// <summary>
/// Represents a single step in a pipeline job.
/// </summary>
public class Step
{
    /// <summary>
    /// Gets or sets the unique identifier for this step.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Gets or sets the display name of the step.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the type of step.
    /// </summary>
    public StepType Type { get; set; }

    /// <summary>
    /// Gets or sets the script content for script-based steps.
    /// </summary>
    public string? Script { get; set; }

    /// <summary>
    /// Gets or sets the shell to use for script execution.
    /// </summary>
    public string Shell { get; set; } = "bash";

    /// <summary>
    /// Gets or sets the input parameters for the step.
    /// </summary>
    public Dictionary<string, string> With { get; set; } = new();

    /// <summary>
    /// Gets or sets the environment variables for the step.
    /// </summary>
    public Dictionary<string, string> Environment { get; set; } = new();

    /// <summary>
    /// Gets or sets whether to continue on error.
    /// </summary>
    public bool ContinueOnError { get; set; }

    /// <summary>
    /// Gets or sets the condition for executing this step.
    /// </summary>
    public Condition? Condition { get; set; }

    /// <summary>
    /// Gets or sets the working directory for the step.
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Gets or sets the artifact definition for artifact upload/download steps.
    /// This property is populated when the step type is UploadArtifact or DownloadArtifact.
    /// </summary>
    public ArtifactDefinition? Artifact { get; set; }
}