namespace PDK.Core.Artifacts;

using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
/// Metadata stored alongside artifacts for tracking and restoration.
/// </summary>
public record ArtifactMetadata
{
    /// <summary>
    /// Gets the metadata schema version.
    /// </summary>
    public string Version { get; init; } = "1.0";

    /// <summary>
    /// Gets the artifact information.
    /// </summary>
    public required ArtifactInfo Artifact { get; init; }

    /// <summary>
    /// Gets the list of files in the artifact.
    /// </summary>
    public required IReadOnlyList<ArtifactFileInfo> Files { get; init; }

    /// <summary>
    /// Gets the summary statistics.
    /// </summary>
    public required ArtifactSummary Summary { get; init; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Serializes the metadata to JSON.
    /// </summary>
    /// <returns>JSON string representation of the metadata.</returns>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>
    /// Deserializes metadata from JSON.
    /// </summary>
    /// <param name="json">The JSON string to deserialize.</param>
    /// <returns>The deserialized metadata, or null if deserialization fails.</returns>
    public static ArtifactMetadata? FromJson(string json) =>
        JsonSerializer.Deserialize<ArtifactMetadata>(json, JsonOptions);
}

/// <summary>
/// Core artifact information.
/// </summary>
public record ArtifactInfo
{
    /// <summary>
    /// Gets the artifact name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the timestamp when the artifact was uploaded.
    /// </summary>
    public required DateTime UploadedAt { get; init; }

    /// <summary>
    /// Gets the job name where the artifact was created.
    /// </summary>
    public required string Job { get; init; }

    /// <summary>
    /// Gets the step name where the artifact was created.
    /// </summary>
    public required string Step { get; init; }

    /// <summary>
    /// Gets the compression type used for the artifact.
    /// </summary>
    public required CompressionType Compression { get; init; }
}

/// <summary>
/// Information about a single file in the artifact.
/// </summary>
public record ArtifactFileInfo
{
    /// <summary>
    /// Gets the original source path of the file.
    /// </summary>
    public required string SourcePath { get; init; }

    /// <summary>
    /// Gets the path of the file within the artifact.
    /// </summary>
    public required string ArtifactPath { get; init; }

    /// <summary>
    /// Gets the size of the file in bytes.
    /// </summary>
    public required long SizeBytes { get; init; }

    /// <summary>
    /// Gets the SHA256 hash of the file content.
    /// </summary>
    public required string Sha256 { get; init; }
}

/// <summary>
/// Summary statistics for the artifact.
/// </summary>
public record ArtifactSummary
{
    /// <summary>
    /// Gets the total number of files in the artifact.
    /// </summary>
    public required int FileCount { get; init; }

    /// <summary>
    /// Gets the total size of all files in bytes before compression.
    /// </summary>
    public required long TotalSizeBytes { get; init; }

    /// <summary>
    /// Gets the compressed size in bytes, if compression was applied.
    /// </summary>
    public long? CompressedSizeBytes { get; init; }
}
