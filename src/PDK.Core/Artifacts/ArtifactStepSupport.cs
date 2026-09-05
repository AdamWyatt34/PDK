namespace PDK.Core.Artifacts;

using System.Text;

/// <summary>
/// Shared helpers for the artifact step executors (Docker and host).
/// </summary>
public static class ArtifactStepSupport
{
    /// <summary>
    /// Azure DevOps task name whose downloads always land in <c>&lt;downloadPath&gt;/&lt;artifactName&gt;/</c>.
    /// </summary>
    public const string AzureDownloadBuildArtifactsTask = "DownloadBuildArtifacts";

    /// <summary>
    /// Decides whether a named download should be placed in a sub-directory named after the artifact.
    /// This is the case for GitHub downloads without a name (handled by the manager), for definitions
    /// flagged with <see cref="ArtifactDefinition.DownloadIntoNamedSubdirectory"/>, and for Azure
    /// DevOps <c>DownloadBuildArtifacts</c> tasks (detected through the <c>_task</c> step input).
    /// </summary>
    /// <param name="definition">The artifact definition.</param>
    /// <param name="stepInputs">The step's inputs (<c>with</c>), if any.</param>
    /// <returns>True when the files go under <c>&lt;target&gt;/&lt;name&gt;/</c>.</returns>
    public static bool UsesNamedSubdirectory(ArtifactDefinition definition, IReadOnlyDictionary<string, string>? stepInputs)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (definition.DownloadIntoNamedSubdirectory)
        {
            return true;
        }

        if (stepInputs != null
            && stepInputs.TryGetValue("_task", out var task)
            && string.Equals(task?.Trim(), AzureDownloadBuildArtifactsTask, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets the directory name to use when an artifact is downloaded into its own sub-directory.
    /// </summary>
    /// <param name="artifactName">The artifact name.</param>
    /// <returns>A safe directory name.</returns>
    public static string GetDownloadDirectoryName(string artifactName)
    {
        var sanitized = ArtifactNames.SanitizeForFileSystem(artifactName ?? string.Empty);
        return sanitized.Length == 0 || sanitized == "." || sanitized == ".." ? "artifact" : sanitized;
    }

    /// <summary>
    /// Formats a byte count as a human-readable string.
    /// </summary>
    /// <param name="bytes">The byte count.</param>
    /// <returns>The formatted size (e.g. "1.5 MB").</returns>
    public static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        var order = 0;
        double size = bytes;

        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{size:0.##} {sizes[order]}");
    }

    /// <summary>
    /// Builds the step output for a successful upload, including any warnings.
    /// </summary>
    /// <param name="result">The upload result.</param>
    /// <returns>The output text.</returns>
    public static string DescribeUpload(UploadResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var builder = new StringBuilder();
        builder.Append(string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"Uploaded {result.FileCount} files to artifact '{result.ArtifactName}' ({FormatBytes(result.TotalSizeBytes)})"));

        if (result.CompressedSizeBytes.HasValue)
        {
            builder.Append(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $", compressed to {FormatBytes(result.CompressedSizeBytes.Value)}"));
        }

        builder.Append(" - stored in ").Append(result.StoragePath);
        AppendWarnings(builder, result.Warnings);
        return builder.ToString();
    }

    /// <summary>
    /// Builds the step output for a successful download, including any warnings.
    /// </summary>
    /// <param name="result">The download result.</param>
    /// <param name="targetPath">The path the files were placed in (as seen by the job).</param>
    /// <returns>The output text.</returns>
    public static string DescribeDownload(DownloadResult result, string targetPath)
    {
        ArgumentNullException.ThrowIfNull(result);

        var builder = new StringBuilder();

        if (string.IsNullOrEmpty(result.ArtifactName))
        {
            var names = result.Artifacts.Count == 0 ? "no artifacts" : string.Join(", ", result.Artifacts.Select(n => $"'{n}'"));
            builder.Append(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"Downloaded {result.FileCount} files from {result.Artifacts.Count} artifact(s) ({names}) to {targetPath}"));
        }
        else
        {
            builder.Append(string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"Downloaded {result.FileCount} files from artifact '{result.ArtifactName}' to {targetPath}"));
        }

        if (!string.IsNullOrEmpty(result.RunId))
        {
            builder.Append(" (run ").Append(result.RunId).Append(')');
        }

        AppendWarnings(builder, result.Warnings);
        return builder.ToString();
    }

    /// <summary>
    /// Describes the "no files matched" situation for an upload.
    /// </summary>
    /// <param name="patterns">The patterns that were used.</param>
    /// <returns>The message.</returns>
    public static string DescribeNoFiles(IEnumerable<string> patterns)
    {
        return $"No files found matching patterns: {string.Join(", ", patterns)}";
    }

    private static void AppendWarnings(StringBuilder builder, IReadOnlyList<string> warnings)
    {
        foreach (var warning in warnings)
        {
            builder.AppendLine().Append("Warning: ").Append(warning);
        }
    }
}
