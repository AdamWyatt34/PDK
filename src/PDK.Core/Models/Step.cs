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
    /// Gets or sets whether the step is enabled. Disabled steps (Azure DevOps <c>enabled: false</c>)
    /// are reported as skipped and never executed.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the step timeout in minutes (GitHub <c>timeout-minutes</c>, Azure <c>timeoutInMinutes</c>).
    /// Null means no step-level timeout.
    /// </summary>
    public int? TimeoutMinutes { get; set; }

    /// <summary>
    /// Gets or sets the original action or task reference (e.g. <c>actions/setup-dotnet@v4</c>, <c>DotNetCoreCLI@2</c>).
    /// Populated by the providers for diagnostics and for skip-with-warning handling of unsupported steps.
    /// </summary>
    public string? ActionReference { get; set; }

    /// <summary>
    /// Gets or sets the artifact definition for artifact upload/download steps.
    /// This property is populated when the step type is UploadArtifact or DownloadArtifact.
    /// </summary>
    public ArtifactDefinition? Artifact { get; set; }

    /// <summary>
    /// Gets or sets the list of step IDs or names that this step depends on.
    /// Used for parallel execution - steps with unmet dependencies wait for those steps to complete.
    /// When null or empty, the step has no explicit dependencies (but may still be ordered sequentially).
    /// </summary>
    public List<string>? Needs { get; set; }

    /// <summary>
    /// Creates a shallow copy of this step (dictionaries and lists are copied, nested objects are shared).
    /// </summary>
    /// <returns>A new <see cref="Step"/> with the same values.</returns>
    public Step Clone()
    {
        return new Step
        {
            Id = Id,
            Name = Name,
            Type = Type,
            Script = Script,
            Shell = Shell,
            With = new Dictionary<string, string>(With),
            Environment = new Dictionary<string, string>(Environment),
            ContinueOnError = ContinueOnError,
            Condition = Condition,
            WorkingDirectory = WorkingDirectory,
            Enabled = Enabled,
            TimeoutMinutes = TimeoutMinutes,
            ActionReference = ActionReference,
            Artifact = Artifact,
            Needs = Needs == null ? null : new List<string>(Needs)
        };
    }
}
