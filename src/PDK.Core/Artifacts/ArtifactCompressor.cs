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
                    await CompressTarGzAsync(sourcePath, targetPath, progress, cancellationToken);
                    break;

                case CompressionType.Zip:
                    await CompressZipAsync(sourcePath, targetPath, progress, cancellationToken);
                    break;

                default:
                    throw ArtifactException.CompressionFailed($"Unsupported compression type: {type}");
            }
        }
        catch (ArtifactException)
        {
            throw;
        }
        catch (Exception ex)
        {
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
            throw ArtifactException.DecompressionFailed(archivePath);
        }

        var type = DetectType(archivePath);
        if (type == CompressionType.None)
        {
            throw ArtifactException.DecompressionFailed(archivePath);
        }

        try
        {
            // Ensure target directory exists
            Directory.CreateDirectory(targetPath);

            switch (type)
            {
                case CompressionType.Gzip:
                    await DecompressTarGzAsync(archivePath, targetPath, progress, cancellationToken);
                    break;

                case CompressionType.Zip:
                    await DecompressZipAsync(archivePath, targetPath, progress, cancellationToken);
                    break;

                default:
                    throw ArtifactException.DecompressionFailed(archivePath);
            }
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

        if (lowerPath.EndsWith(".tar.gz") || lowerPath.EndsWith(".tgz"))
        {
            return CompressionType.Gzip;
        }

        if (lowerPath.EndsWith(".zip"))
        {
            return CompressionType.Zip;
        }

        return CompressionType.None;
    }

    private async Task CompressTarGzAsync(
        string sourcePath,
        string targetPath,
        IProgress<ArtifactProgress>? progress,
        CancellationToken cancellationToken)
    {
        var files = Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories);
        var totalBytes = files.Sum(f => new FileInfo(f).Length);
        var processedBytes = 0L;
        var processedFiles = 0;

        await using var fileStream = File.Create(targetPath);
        await using var gzipStream = new GZipStream(fileStream, CompressionLevel.Optimal);

        var writerOptions = new TarWriterOptions(SharpCompressCompressionType.None, true);

        using var writer = WriterFactory.Open(gzipStream, ArchiveType.Tar, writerOptions);

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = Path.GetRelativePath(sourcePath, file);
            var normalizedPath = relativePath.Replace('\\', '/');

            await using var entryStream = File.OpenRead(file);
            writer.Write(normalizedPath, entryStream, new FileInfo(file).LastWriteTime);

            var fileSize = new FileInfo(file).Length;
            processedBytes += fileSize;
            processedFiles++;

            progress?.Report(new ArtifactProgress
            {
                TotalFiles = files.Length,
                ProcessedFiles = processedFiles,
                TotalBytes = totalBytes,
                ProcessedBytes = processedBytes,
                CurrentFile = relativePath
            });
        }
    }

    private async Task CompressZipAsync(
        string sourcePath,
        string targetPath,
        IProgress<ArtifactProgress>? progress,
        CancellationToken cancellationToken)
    {
        var files = Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories);
        var totalBytes = files.Sum(f => new FileInfo(f).Length);
        var processedBytes = 0L;
        var processedFiles = 0;

        await using var fileStream = File.Create(targetPath);
        using var archive = new ZipArchive(fileStream, ZipArchiveMode.Create, leaveOpen: false);

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = Path.GetRelativePath(sourcePath, file);
            var normalizedPath = relativePath.Replace('\\', '/');

            var entry = archive.CreateEntry(normalizedPath, CompressionLevel.Optimal);
            entry.LastWriteTime = new FileInfo(file).LastWriteTime;

            await using var entryStream = entry.Open();
            await using var sourceStream = File.OpenRead(file);
            await sourceStream.CopyToAsync(entryStream, BufferSize, cancellationToken);

            var fileSize = new FileInfo(file).Length;
            processedBytes += fileSize;
            processedFiles++;

            progress?.Report(new ArtifactProgress
            {
                TotalFiles = files.Length,
                ProcessedFiles = processedFiles,
                TotalBytes = totalBytes,
                ProcessedBytes = processedBytes,
                CurrentFile = relativePath
            });
        }
    }

    private async Task DecompressTarGzAsync(
        string archivePath,
        string targetPath,
        IProgress<ArtifactProgress>? progress,
        CancellationToken cancellationToken)
    {
        var archiveSize = new FileInfo(archivePath).Length;
        var processedBytes = 0L;
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

            var entryPath = Path.Combine(targetPath, reader.Entry.Key.Replace('/', Path.DirectorySeparatorChar));

            // Ensure directory exists
            var entryDir = Path.GetDirectoryName(entryPath);
            if (!string.IsNullOrEmpty(entryDir))
            {
                Directory.CreateDirectory(entryDir);
            }

            await using var entryStream = reader.OpenEntryStream();
            await using var outputStream = File.Create(entryPath);
            await entryStream.CopyToAsync(outputStream, BufferSize, cancellationToken);

            processedFiles++;
            processedBytes = fileStream.Position; // Approximate progress from gzip stream position

            progress?.Report(new ArtifactProgress
            {
                TotalFiles = 0, // Unknown until complete
                ProcessedFiles = processedFiles,
                TotalBytes = archiveSize,
                ProcessedBytes = processedBytes,
                CurrentFile = reader.Entry.Key
            });
        }
    }

    private async Task DecompressZipAsync(
        string archivePath,
        string targetPath,
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

            var entryPath = Path.Combine(targetPath, entry.FullName.Replace('/', Path.DirectorySeparatorChar));

            // Ensure directory exists
            var entryDir = Path.GetDirectoryName(entryPath);
            if (!string.IsNullOrEmpty(entryDir))
            {
                Directory.CreateDirectory(entryDir);
            }

            await using var entryStream = entry.Open();
            await using var outputStream = File.Create(entryPath);
            await entryStream.CopyToAsync(outputStream, BufferSize, cancellationToken);

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
}
