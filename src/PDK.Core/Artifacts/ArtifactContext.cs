namespace PDK.Core.Artifacts;

/// <summary>
/// Provides context for artifact operations including run, job, and step information.
/// </summary>
public record ArtifactContext
{
    /// <summary>
    /// Gets the workspace root path.
    /// </summary>
    public required string WorkspacePath { get; init; }

    /// <summary>
    /// Gets the unique run identifier (timestamp-based).
    /// Format: "yyyyMMdd-HHmmss-fff"
    /// </summary>
    public required string RunId { get; init; }

    /// <summary>
    /// Gets the current job name.
    /// </summary>
    public required string JobName { get; init; }

    /// <summary>
    /// Gets the current step index (0-based).
    /// </summary>
    public required int StepIndex { get; init; }

    /// <summary>
    /// Gets the current step name.
    /// </summary>
    public required string StepName { get; init; }

    /// <summary>
    /// Creates a run ID from the current timestamp.
    /// </summary>
    /// <returns>A run ID in format "yyyyMMdd-HHmmss-fff".</returns>
    public static string GenerateRunId() => DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");

    /// <summary>
    /// Gets the artifact storage path for this context.
    /// </summary>
    /// <param name="basePath">The base artifacts path.</param>
    /// <param name="artifactName">The artifact name.</param>
    /// <returns>The full path to the artifact directory.</returns>
    public string GetArtifactPath(string basePath, string artifactName)
    {
        var sanitizedJobName = SanitizeName(JobName);
        var sanitizedStepName = SanitizeName(StepName);

        return Path.Combine(
            basePath,
            $"run-{RunId}",
            $"job-{sanitizedJobName}",
            $"step-{StepIndex}-{sanitizedStepName}",
            $"artifact-{artifactName}");
    }

    private static string SanitizeName(string name)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return string.Join("_", name.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
    }
}
