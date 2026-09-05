namespace PDK.Core.Artifacts;

using System.IO.Compression;
using SharpCompress.Common;
using SharpCompress.Readers;
using SharpCompress.Writers;
using SharpCompress.Writers.Tar;
using SharpCompressCompressionType = SharpCompress.Common.CompressionType;

/// <summary>
/// Compresses and decompresses artifacts using Zip or Gzip (tar.gz) formats.
/// </summary>
public class ArtifactCompressor : IArtifactCompressor
{
    private const int BufferSize = 81920; // 80KB buffer for streaming

    // ZipArchiveEntry.LastWriteTime only accepts DOS timestamps (1980-01-01 .. 2107-12-31).
    private static readonly DateTime MinZipTimestamp = new(1980, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
    private static readonly DateTime MaxZipTimestamp = new(2107, 12, 31, 23, 59, 58, DateTimeKind.Unspecified);

    /// <inheritdoc/>
    public async Task CompressAsync(
        string sourcePath,
        string targetPath,
        CompressionType type,
        IProgress<ArtifactProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (type == CompressionType.None)
        {
            return;
        }

        if (!Directory.Exists(sourcePath))
        {
            throw ArtifactException.CompressionFailed($"Source directory not found: {sourcePath}");
        }

        var files = Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => new ArchiveFileEntry(f, Path.GetRelativePath(sourcePath, f).Replace('\\', '/')))
            .ToList();

        await CompressFilesAsync(files, targetPath, type, progress, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task CompressFilesAsync(
        IReadOnlyList<ArchiveFileEntry> files,
        string targetPath,
        CompressionType type,
        IProgress<ArtifactProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);

        if (type == CompressionType.None)
        {
            return;
        }

        try
        {
            // Ensure target directory exists
            var targetDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            switch (type)
            {
                case CompressionType.Gzip:
                    await CompressTarGzAsync(files, targetPath, progress, cancellationToken);
                    break;

                case CompressionType.Zip:
                    await CompressZipAsync(files, targetPath, progress, cancellationToken);
                    break;

                default:
                    throw ArtifactException.CompressionFailed($"Unsupported compression type: {type}");
            }
        }
        catch (OperationCanceledException)
        {
            TryDelete(targetPath);
            throw;
        }
        catch (ArtifactException)
        {
            TryDelete(targetPath);
            throw;
        }
        catch (Exception ex)
        {
            TryDelete(targetPath);
            throw ArtifactException.CompressionFailed(ex.Message, ex);
        }
    }

    /// <inheritdoc/>
    public async Task DecompressAsync(
        string archivePath,
        string targetPath,
        IProgress<ArtifactProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(archivePath))
        {
            throw ArtifactException.DecompressionFailed(archivePath, reason: "archive not found");
        }

        var type = DetectType(archivePath);
        if (type == CompressionType.None)
        {
            throw ArtifactException.DecompressionFailed(archivePath, reason: "unknown archive format");
        }

        try
        {
            // Ensure target directory exists
            Directory.CreateDirectory(targetPath);
            var targetRoot = Path.GetFullPath(targetPath);

            switch (type)
            {
                case CompressionType.Gzip:
                    await DecompressTarGzAsync(archivePath, targetRoot, progress, cancellationToken);
                    break;

                case CompressionType.Zip:
                    await DecompressZipAsync(archivePath, targetRoot, progress, cancellationToken);
                    break;

                default:
                    throw ArtifactException.DecompressionFailed(archivePath);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArtifactException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw ArtifactException.DecompressionFailed(archivePath, ex);
        }
    }

    /// <inheritdoc/>
    public string GetExtension(CompressionType type) => type switch
    {
        CompressionType.Gzip => ".tar.gz",
        CompressionType.Zip => ".zip",
        _ => ""
    };

    /// <inheritdoc/>
    public CompressionType DetectType(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return CompressionType.None;
        }

        var lowerPath = filePath.ToLowerInvariant();

        if (lowerPath.EndsWith(".tar.gz", StringComparison.Ordinal) || lowerPath.EndsWith(".tgz", StringComparison.Ordinal))
        {
            return CompressionType.Gzip;
        }

        if (lowerPath.EndsWith(".zip", StringComparison.Ordinal))
        {
            return CompressionType.Zip;
        }

        return CompressionType.None;
    }

    /// <summary>
    /// Resolves an archive entry name to a path inside <paramref name="targetRoot"/>, rejecting
    /// absolute names, parent-directory references and anything that canonicalizes outside the root.
    /// </summary>
    /// <param name="targetRoot">The fully qualified extraction directory.</param>
    /// <param name="entryName">The entry name from the archive.</param>
    /// <param name="archivePath">The archive (for error messages).</param>
    /// <returns>The safe, fully qualified path of the entry.</returns>
    /// <exception cref="ArtifactException">The entry is unsafe.</exception>
    public static string GetSafeExtractionPath(string targetRoot, string? entryName, string archivePath)
    {
        if (string.IsNullOrWhiteSpace(entryName))
        {
            throw ArtifactException.DecompressionFailed(archivePath, reason: "archive contains an entry without a name");
        }

        var normalized = entryName.Replace('\\', '/');

        if (normalized.StartsWith('/') || Path.IsPathRooted(normalized) || (normalized.Length >= 2 && normalized[1] == ':'))
        {
            throw ArtifactException.DecompressionFailed(archivePath, reason: $"entry '{entryName}' has an absolute path");
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            throw ArtifactException.DecompressionFailed(archivePath, reason: "archive contains an entry without a name");
        }

        if (segments.Any(s => s == ".."))
        {
            throw ArtifactException.DecompressionFailed(archivePath, reason: $"entry '{entryName}' contains a parent directory reference");
        }

        var root = Path.GetFullPath(targetRoot);
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(root, Path.Combine(segments)));

        if (!fullPath.StartsWith(rootWithSeparator, ArtifactPathResolver.PathComparison))
        {
            throw ArtifactException.DecompressionFailed(archivePath, reason: $"entry '{entryName}' resolves outside the target directory");
        }

        return fullPath;
    }

    private static async Task CompressTarGzAsync(
        IReadOnlyList<ArchiveFileEntry> files,
        string targetPath,
        IProgress<ArtifactProgress>? progress,
        CancellationToken cancellationToken)
    {
        var totalBytes = files.Sum(f => new FileInfo(f.SourceFilePath).Length);
        var processedBytes = 0L;
        var processedFiles = 0;

        await using var fileStream = File.Create(targetPath);
        await using var gzipStream = new GZipStream(fileStream, CompressionLevel.Optimal);

        var writerOptions = new TarWriterOptions(SharpCompressCompressionType.None, true);

        using var writer = WriterFactory.Open(gzipStream, ArchiveType.Tar, writerOptions);

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileInfo = new FileInfo(file.SourceFilePath);

            await using var entryStream = File.OpenRead(file.SourceFilePath);
            writer.Write(NormalizeEntryPath(file.EntryPath), entryStream, ClampTarTimestamp(fileInfo.LastWriteTimeUtc));

            processedBytes += fileInfo.Length;
            processedFiles++;

            progress?.Report(new ArtifactProgress
            {
                TotalFiles = files.Count,
                ProcessedFiles = processedFiles,
                TotalBytes = totalBytes,
                ProcessedBytes = processedBytes,
                CurrentFile = file.EntryPath
            });
        }
    }

    private static async Task CompressZipAsync(
        IReadOnlyList<ArchiveFileEntry> files,
        string targetPath,
        IProgress<ArtifactProgress>? progress,
        CancellationToken cancellationToken)
    {
        var totalBytes = files.Sum(f => new FileInfo(f.SourceFilePath).Length);
        var processedBytes = 0L;
        var processedFiles = 0;

        await using var fileStream = File.Create(targetPath);
        using var archive = new ZipArchive(fileStream, ZipArchiveMode.Create, leaveOpen: false);

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileInfo = new FileInfo(file.SourceFilePath);

            var entry = archive.CreateEntry(NormalizeEntryPath(file.EntryPath), CompressionLevel.Optimal);
            entry.LastWriteTime = ClampZipTimestamp(fileInfo.LastWriteTime);

            await using (var entryStream = entry.Open())
            await using (var sourceStream = File.OpenRead(file.SourceFilePath))
            {
                await sourceStream.CopyToAsync(entryStream, BufferSize, cancellationToken);
            }

            processedBytes += fileInfo.Length;
            processedFiles++;

            progress?.Report(new ArtifactProgress
            {
                TotalFiles = files.Count,
                ProcessedFiles = processedFiles,
                TotalBytes = totalBytes,
                ProcessedBytes = processedBytes,
                CurrentFile = file.EntryPath
            });
        }
    }

    private static async Task DecompressTarGzAsync(
        string archivePath,
        string targetRoot,
        IProgress<ArtifactProgress>? progress,
        CancellationToken cancellationToken)
    {
        var archiveSize = new FileInfo(archivePath).Length;
        var processedFiles = 0;

        await using var fileStream = File.OpenRead(archivePath);
        await using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);

        using var reader = ReaderFactory.Open(gzipStream);

        while (reader.MoveToNextEntry())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (reader.Entry.IsDirectory)
            {
                continue;
            }

            var entryPath = GetSafeExtractionPath(targetRoot, reader.Entry.Key, archivePath);

            // Ensure directory exists
            var entryDir = Path.GetDirectoryName(entryPath);
            if (!string.IsNullOrEmpty(entryDir))
            {
                Directory.CreateDirectory(entryDir);
            }

            await using (var entryStream = reader.OpenEntryStream())
            await using (var outputStream = File.Create(entryPath))
            {
                await entryStream.CopyToAsync(outputStream, BufferSize, cancellationToken);
            }

            processedFiles++;

            progress?.Report(new ArtifactProgress
            {
                TotalFiles = 0, // Unknown until complete
                ProcessedFiles = processedFiles,
                TotalBytes = archiveSize,
                ProcessedBytes = fileStream.Position, // Approximate progress from gzip stream position
                CurrentFile = reader.Entry.Key
            });
        }
    }

    private static async Task DecompressZipAsync(
        string archivePath,
        string targetRoot,
        IProgress<ArtifactProgress>? progress,
        CancellationToken cancellationToken)
    {
        await using var fileStream = File.OpenRead(archivePath);
        using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read);

        var entries = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
        var totalBytes = entries.Sum(e => e.Length);
        var processedBytes = 0L;
        var processedFiles = 0;

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entryPath = GetSafeExtractionPath(targetRoot, entry.FullName, archivePath);

            // Ensure directory exists
            var entryDir = Path.GetDirectoryName(entryPath);
            if (!string.IsNullOrEmpty(entryDir))
            {
                Directory.CreateDirectory(entryDir);
            }

            await using (var entryStream = entry.Open())
            await using (var outputStream = File.Create(entryPath))
            {
                await entryStream.CopyToAsync(outputStream, BufferSize, cancellationToken);
            }

            processedBytes += entry.Length;
            processedFiles++;

            progress?.Report(new ArtifactProgress
            {
                TotalFiles = entries.Count,
                ProcessedFiles = processedFiles,
                TotalBytes = totalBytes,
                ProcessedBytes = processedBytes,
                CurrentFile = entry.FullName
            });
        }
    }

    private static string NormalizeEntryPath(string entryPath)
    {
        var normalized = entryPath.Replace('\\', '/').TrimStart('/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized;
    }

    private static DateTimeOffset ClampZipTimestamp(DateTime timestamp)
    {
        if (timestamp < MinZipTimestamp)
        {
            return new DateTimeOffset(MinZipTimestamp, TimeSpan.Zero);
        }

        if (timestamp > MaxZipTimestamp)
        {
            return new DateTimeOffset(MaxZipTimestamp, TimeSpan.Zero);
        }

        return timestamp;
    }

    private static DateTime ClampTarTimestamp(DateTime timestampUtc)
    {
        return timestampUtc < DateTime.UnixEpoch ? DateTime.UnixEpoch : timestampUtc;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best effort cleanup of a partially written archive.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort cleanup of a partially written archive.
        }
    }
}
