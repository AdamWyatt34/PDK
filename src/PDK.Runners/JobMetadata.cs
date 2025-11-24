namespace PDK.Runners;

/// <summary>
/// Metadata about the job being executed.
/// </summary>
public record JobMetadata
{
    /// <summary>
    /// Gets the name of the job.
    /// </summary>
    public string JobName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the unique identifier of the job.
    /// </summary>
    public string JobId { get; init; } = string.Empty;

    /// <summary>
    /// Gets the runner specification (e.g., "ubuntu-latest", "windows-latest").
    /// </summary>
    public string Runner { get; init; } = string.Empty;
}
