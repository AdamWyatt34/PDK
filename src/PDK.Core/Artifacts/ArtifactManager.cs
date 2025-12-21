namespace PDK.Core.Artifacts;

using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PDK.Core.Configuration;

/// <summary>
/// Manages artifact upload, download, and lifecycle operations.
/// </summary>
public partial class ArtifactManager : IArtifactManager
{
    private readonly IConfiguration _configuration;
    private readonly IFileSelector _fileSelector;
    private readonly IArtifactCompressor _compressor;
    private readonly ILogger<ArtifactManager>? _logger;

    private const string MetadataFileName = "artifact.metadata.json";
    private const int MaxArtifactNameLength = 100;
    private const int DefaultRetentionDays = 7;
    private const string DefaultBasePath = ".pdk/artifacts";

    [GeneratedRegex(@"^[a-zA-Z0-9_-]+$")]
    private static partial Regex ValidNamePattern();

    /// <summary>
    /// Initializes a new instance of the <see cref="ArtifactManager"/> class.
    /// </summary>
    /// <param name="configuration">The configuration provider.</param>
    /// <param name="fileSelector">The file selector for glob patterns.</param>
    /// <param name="compressor">The artifact compressor.</param>
    /// <param name="logger">Optional logger.</param>
    public ArtifactManager(
        IConfiguration configuration,
        IFileSelector fileSelector,
        IArtifactCompressor compressor,
        ILogger<ArtifactManager>? logger = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _fileSelector = fileSelector ?? throw new ArgumentNullException(nameof(fileSelector));
        _compressor = compressor ?? throw new ArgumentNullException(nameof(compressor));
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<UploadResult> UploadAsync(
        string artifactName,
        IEnumerable<string> patterns,
        ArtifactContext context,
        ArtifactOptions? options = null,
        IProgress<ArtifactProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateArtifactName(artifactName);
        options ??= ArtifactOptions.Default;

        var basePath = GetArtifactsBasePath(context.WorkspacePath);
        var artifactPath = context.GetArtifactPath(basePath, artifactName);

        _logger?.LogDebug("Uploading artifact '{ArtifactName}' to {Path}", artifactName, artifactPath);

        // Check if already exists
        if (!options.OverwriteExisting && Directory.Exists(artifactPath))
        {
            throw ArtifactException.AlreadyExists(artifactName);
        }

        // Select files
        var patternList = patterns.ToList();
        var files = _fileSelector.SelectFiles(context.WorkspacePath, patternList).ToList();

        if (files.Count == 0)
        {
            HandleNoFilesFound(artifactName, patternList, context.WorkspacePath, options.IfNoFilesFound);
            return new UploadResult
            {
                ArtifactName = artifactName,
                FileCount = 0,
                TotalSizeBytes = 0,
                StoragePath = artifactPath
            };
        }

        // Create artifact directory
        if (Directory.Exists(artifactPath))
        {
            Directory.Delete(artifactPath, recursive: true);
        }
        Directory.CreateDirectory(artifactPath);

        try
        {
            // Copy files and calculate checksums
            var fileInfos = await CopyFilesAsync(
                context.WorkspacePath, artifactPath, files, progress, cancellationToken);

            var totalSize = fileInfos.Sum(f => f.SizeBytes);
            long? compressedSize = null;

            // Compress if requested
            if (options.Compression != CompressionType.None)
            {
                var archivePath = artifactPath + _compressor.GetExtension(options.Compression);
                await _compressor.CompressAsync(artifactPath, archivePath, options.Compression, progress, cancellationToken);
                compressedSize = new FileInfo(archivePath).Length;

                _logger?.LogDebug("Compressed artifact to {Size} bytes ({Ratio:P0} of original)",
                    compressedSize, (double)compressedSize / totalSize);
            }

            // Write metadata
            var metadata = CreateMetadata(artifactName, context, fileInfos, options.Compression, compressedSize);
            await WriteMetadataAsync(artifactPath, metadata, cancellationToken);

            _logger?.LogInformation("Uploaded artifact '{ArtifactName}' with {FileCount} files ({TotalSize} bytes)",
                artifactName, fileInfos.Count, totalSize);

            return new UploadResult
            {
                ArtifactName = artifactName,
                FileCount = fileInfos.Count,
                TotalSizeBytes = totalSize,
                CompressedSizeBytes = compressedSize,
                StoragePath = artifactPath
            };
        }
        catch (Exception ex) when (ex is not ArtifactException)
        {
            // Cleanup on failure
            try
            {
                if (Directory.Exists(artifactPath))
                {
                    Directory.Delete(artifactPath, recursive: true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }

            if (ex is UnauthorizedAccessException or IOException { HResult: -2147024891 }) // Access denied
            {
                throw ArtifactException.PermissionDenied(artifactPath, ex);
            }

            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<DownloadResult> DownloadAsync(
        string artifactName,
        string targetPath,
        ArtifactOptions? options = null,
        IProgress<ArtifactProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= ArtifactOptions.Default;

        // Find the artifact
        var artifactInfo = await FindArtifactAsync(artifactName, null);
        if (artifactInfo == null)
        {
            throw ArtifactException.NotFound(artifactName);
        }

        _logger?.LogDebug("Downloading artifact '{ArtifactName}' from {Path} to {Target}",
            artifactName, artifactInfo.StoragePath, targetPath);

        // Load metadata
        var metadataPath = Path.Combine(artifactInfo.StoragePath, MetadataFileName);
        var metadata = await LoadMetadataAsync(metadataPath, cancellationToken);

        if (metadata == null)
        {
            throw ArtifactException.CorruptMetadata(metadataPath);
        }

        // Ensure target directory exists
        Directory.CreateDirectory(targetPath);

        try
        {
            // Check if compressed
            if (metadata.Artifact.Compression != CompressionType.None)
            {
                var archivePath = artifactInfo.StoragePath + _compressor.GetExtension(metadata.Artifact.Compression);
                if (File.Exists(archivePath))
                {
                    await _compressor.DecompressAsync(archivePath, targetPath, progress, cancellationToken);
                }
                else
                {
                    // Fall back to copying files directly
                    await CopyFilesToTargetAsync(artifactInfo.StoragePath, targetPath, metadata.Files, progress, cancellationToken);
                }
            }
            else
            {
                await CopyFilesToTargetAsync(artifactInfo.StoragePath, targetPath, metadata.Files, progress, cancellationToken);
            }

            _logger?.LogInformation("Downloaded artifact '{ArtifactName}' with {FileCount} files to {Target}",
                artifactName, metadata.Summary.FileCount, targetPath);

            return new DownloadResult
            {
                ArtifactName = artifactName,
                FileCount = metadata.Summary.FileCount,
                TargetPath = targetPath
            };
        }
        catch (Exception ex) when (ex is not ArtifactException)
        {
            if (ex is UnauthorizedAccessException)
            {
                throw ArtifactException.PermissionDenied(targetPath, ex);
            }

            throw;
        }
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<ArtifactListItem>> ListAsync(string? runId = null)
    {
        var basePath = GetArtifactsBasePath(Directory.GetCurrentDirectory());
        if (!Directory.Exists(basePath))
        {
            return Enumerable.Empty<ArtifactListItem>();
        }

        var results = new List<ArtifactListItem>();

        // Find all run directories
        var runDirectories = Directory.GetDirectories(basePath, "run-*");

        foreach (var runDir in runDirectories)
        {
            var runDirName = Path.GetFileName(runDir);
            var currentRunId = runDirName.StartsWith("run-") ? runDirName[4..] : runDirName;

            // Filter by runId if specified
            if (runId != null && currentRunId != runId)
            {
                continue;
            }

            // Find all artifact directories recursively
            var artifactDirs = FindArtifactDirectories(runDir);

            foreach (var artifactDir in artifactDirs)
            {
                var metadataPath = Path.Combine(artifactDir, MetadataFileName);
                if (!File.Exists(metadataPath))
                {
                    continue;
                }

                try
                {
                    var metadata = await LoadMetadataAsync(metadataPath, CancellationToken.None);
                    if (metadata != null)
                    {
                        results.Add(new ArtifactListItem
                        {
                            Name = metadata.Artifact.Name,
                            RunId = currentRunId,
                            JobName = metadata.Artifact.Job,
                            StepName = metadata.Artifact.Step,
                            UploadedAt = metadata.Artifact.UploadedAt,
                            FileCount = metadata.Summary.FileCount,
                            TotalSizeBytes = metadata.Summary.TotalSizeBytes,
                            StoragePath = artifactDir
                        });
                    }
                }
                catch
                {
                    // Skip invalid metadata files
                }
            }
        }

        return results.OrderByDescending(a => a.UploadedAt);
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(string artifactName, string? runId = null)
    {
        var artifactInfo = await FindArtifactAsync(artifactName, runId);
        return artifactInfo != null;
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string artifactName, string? runId = null)
    {
        var artifacts = (await ListAsync(runId))
            .Where(a => a.Name.Equals(artifactName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var artifact in artifacts)
        {
            _logger?.LogDebug("Deleting artifact '{ArtifactName}' from {Path}", artifactName, artifact.StoragePath);

            if (Directory.Exists(artifact.StoragePath))
            {
                Directory.Delete(artifact.StoragePath, recursive: true);

                // Also delete any archive files
                foreach (CompressionType compressionType in Enum.GetValues<CompressionType>())
                {
                    var archivePath = artifact.StoragePath + _compressor.GetExtension(compressionType);
                    if (File.Exists(archivePath))
                    {
                        File.Delete(archivePath);
                    }
                }
            }
        }

        _logger?.LogInformation("Deleted {Count} artifact(s) named '{ArtifactName}'", artifacts.Count, artifactName);
    }

    /// <inheritdoc/>
    public Task<int> CleanupAsync(int retentionDays)
    {
        var basePath = GetArtifactsBasePath(Directory.GetCurrentDirectory());
        if (!Directory.Exists(basePath))
        {
            return Task.FromResult(0);
        }

        var cutoffDate = DateTime.UtcNow.AddDays(-retentionDays);
        var deletedCount = 0;

        var runDirectories = Directory.GetDirectories(basePath, "run-*");

        foreach (var runDir in runDirectories)
        {
            var runDirName = Path.GetFileName(runDir);
            var runId = runDirName.StartsWith("run-") ? runDirName[4..] : runDirName;

            // Parse run timestamp
            if (TryParseRunTimestamp(runId, out var runTimestamp) && runTimestamp < cutoffDate)
            {
                _logger?.LogDebug("Deleting old run directory: {Path} (created {Timestamp})", runDir, runTimestamp);

                try
                {
                    // Count artifacts before deletion
                    var artifactDirs = FindArtifactDirectories(runDir);
                    deletedCount += artifactDirs.Count;

                    Directory.Delete(runDir, recursive: true);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to delete run directory: {Path}", runDir);
                }
            }
        }

        _logger?.LogInformation("Cleaned up {Count} artifact(s) older than {Days} days", deletedCount, retentionDays);
        return Task.FromResult(deletedCount);
    }

    #region Private Methods

    private string GetArtifactsBasePath(string workspacePath)
    {
        var configuredPath = _configuration.GetString("artifacts.basePath");
        if (!string.IsNullOrEmpty(configuredPath))
        {
            return Path.IsPathRooted(configuredPath)
                ? configuredPath
                : Path.Combine(workspacePath, configuredPath);
        }

        return Path.Combine(workspacePath, DefaultBasePath);
    }

    private static void ValidateArtifactName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw ArtifactException.InvalidName(name ?? "null");
        }

        if (name.Length > MaxArtifactNameLength)
        {
            throw ArtifactException.InvalidName(name);
        }

        if (!ValidNamePattern().IsMatch(name))
        {
            throw ArtifactException.InvalidName(name);
        }
    }

    private static void HandleNoFilesFound(string artifactName, IEnumerable<string> patterns, string basePath, IfNoFilesFound behavior)
    {
        switch (behavior)
        {
            case IfNoFilesFound.Error:
                throw ArtifactException.NoFilesMatched(patterns, basePath);
            case IfNoFilesFound.Warn:
                // In a real implementation, this would log a warning
                break;
            case IfNoFilesFound.Ignore:
                // Do nothing
                break;
            default:
                throw ArtifactException.NoFilesMatched(patterns, basePath);
        }
    }

    private async Task<List<ArtifactFileInfo>> CopyFilesAsync(
        string basePath,
        string targetPath,
        IEnumerable<string> relativePaths,
        IProgress<ArtifactProgress>? progress,
        CancellationToken cancellationToken)
    {
        var fileInfos = new List<ArtifactFileInfo>();
        var fileList = relativePaths.ToList();
        var totalBytes = fileList.Sum(f =>
        {
            var fullPath = Path.Combine(basePath, f);
            return File.Exists(fullPath) ? new FileInfo(fullPath).Length : 0;
        });

        var processedBytes = 0L;
        var processedFiles = 0;

        foreach (var relativePath in fileList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sourcePath = Path.Combine(basePath, relativePath);
            var destPath = Path.Combine(targetPath, relativePath);

            if (!File.Exists(sourcePath))
            {
                continue;
            }

            // Ensure directory exists
            var destDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            // Copy file
            var sourceInfo = new FileInfo(sourcePath);
            await CopyFileAsync(sourcePath, destPath, cancellationToken);

            // Calculate checksum
            var sha256 = await ComputeSha256Async(destPath, cancellationToken);

            fileInfos.Add(new ArtifactFileInfo
            {
                SourcePath = relativePath,
                ArtifactPath = relativePath,
                SizeBytes = sourceInfo.Length,
                Sha256 = sha256
            });

            processedBytes += sourceInfo.Length;
            processedFiles++;

            progress?.Report(new ArtifactProgress
            {
                TotalFiles = fileList.Count,
                ProcessedFiles = processedFiles,
                TotalBytes = totalBytes,
                ProcessedBytes = processedBytes,
                CurrentFile = relativePath
            });

            _logger?.LogDebug("Uploaded file: {Path} ({Size} bytes)", relativePath, sourceInfo.Length);
        }

        return fileInfos;
    }

    private static async Task CopyFileAsync(string sourcePath, string destPath, CancellationToken cancellationToken)
    {
        const int bufferSize = 81920; // 80KB
        await using var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true);
        await using var destStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, useAsync: true);
        await sourceStream.CopyToAsync(destStream, bufferSize, cancellationToken);

        // Preserve file timestamps
        var sourceInfo = new FileInfo(sourcePath);
        File.SetLastWriteTimeUtc(destPath, sourceInfo.LastWriteTimeUtc);
    }

    private async Task CopyFilesToTargetAsync(
        string artifactPath,
        string targetPath,
        IReadOnlyList<ArtifactFileInfo> files,
        IProgress<ArtifactProgress>? progress,
        CancellationToken cancellationToken)
    {
        var totalBytes = files.Sum(f => f.SizeBytes);
        var processedBytes = 0L;
        var processedFiles = 0;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sourcePath = Path.Combine(artifactPath, file.ArtifactPath);
            var destPath = Path.Combine(targetPath, file.ArtifactPath);

            if (!File.Exists(sourcePath))
            {
                continue;
            }

            // Ensure directory exists
            var destDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            await CopyFileAsync(sourcePath, destPath, cancellationToken);

            processedBytes += file.SizeBytes;
            processedFiles++;

            progress?.Report(new ArtifactProgress
            {
                TotalFiles = files.Count,
                ProcessedFiles = processedFiles,
                TotalBytes = totalBytes,
                ProcessedBytes = processedBytes,
                CurrentFile = file.ArtifactPath
            });

            _logger?.LogDebug("Downloaded file: {Path} ({Size} bytes)", file.ArtifactPath, file.SizeBytes);
        }
    }

    private static async Task<string> ComputeSha256Async(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(filePath);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static ArtifactMetadata CreateMetadata(
        string artifactName,
        ArtifactContext context,
        List<ArtifactFileInfo> files,
        CompressionType compression,
        long? compressedSize)
    {
        return new ArtifactMetadata
        {
            Version = "1.0",
            Artifact = new ArtifactInfo
            {
                Name = artifactName,
                UploadedAt = DateTime.UtcNow,
                Job = context.JobName,
                Step = context.StepName,
                Compression = compression
            },
            Files = files,
            Summary = new ArtifactSummary
            {
                FileCount = files.Count,
                TotalSizeBytes = files.Sum(f => f.SizeBytes),
                CompressedSizeBytes = compressedSize
            }
        };
    }

    private static async Task WriteMetadataAsync(string artifactPath, ArtifactMetadata metadata, CancellationToken cancellationToken)
    {
        var metadataPath = Path.Combine(artifactPath, MetadataFileName);
        await File.WriteAllTextAsync(metadataPath, metadata.ToJson(), cancellationToken);
    }

    private static async Task<ArtifactMetadata?> LoadMetadataAsync(string metadataPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(metadataPath, cancellationToken);
            return ArtifactMetadata.FromJson(json);
        }
        catch
        {
            return null;
        }
    }

    private async Task<ArtifactListItem?> FindArtifactAsync(string artifactName, string? runId)
    {
        var artifacts = await ListAsync(runId);
        return artifacts.FirstOrDefault(a => a.Name.Equals(artifactName, StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> FindArtifactDirectories(string runDirectory)
    {
        var results = new List<string>();

        try
        {
            var directories = Directory.GetDirectories(runDirectory, "artifact-*", SearchOption.AllDirectories);
            results.AddRange(directories);
        }
        catch
        {
            // Ignore access errors
        }

        return results;
    }

    private static bool TryParseRunTimestamp(string runId, out DateTime timestamp)
    {
        // Format: yyyyMMdd-HHmmss-fff or yyyyMMdd-HHmmss
        var formats = new[]
        {
            "yyyyMMdd-HHmmss-fff",
            "yyyyMMdd-HHmmss"
        };

        foreach (var format in formats)
        {
            if (DateTime.TryParseExact(runId, format, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out timestamp))
            {
                timestamp = DateTime.SpecifyKind(timestamp, DateTimeKind.Utc);
                return true;
            }
        }

        timestamp = default;
        return false;
    }

    #endregion
}
