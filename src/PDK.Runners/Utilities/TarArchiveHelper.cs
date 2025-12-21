using ICSharpCode.SharpZipLib.Tar;

namespace PDK.Runners.Utilities;

/// <summary>
/// Provides utility methods for working with tar archives.
/// Used for Docker container file copy operations.
/// </summary>
public static class TarArchiveHelper
{
    /// <summary>
    /// Extracts a tar archive stream to a target directory.
    /// </summary>
    /// <param name="tarStream">The tar archive stream to extract.</param>
    /// <param name="targetDirectory">The directory to extract files to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of files extracted.</returns>
    public static async Task<int> ExtractTarAsync(
        Stream tarStream,
        string targetDirectory,
        CancellationToken cancellationToken = default)
    {
        if (tarStream == null)
        {
            throw new ArgumentNullException(nameof(tarStream));
        }

        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            throw new ArgumentException("Target directory cannot be null or empty.", nameof(targetDirectory));
        }

        Directory.CreateDirectory(targetDirectory);

        var fileCount = 0;

        await Task.Run(() =>
        {
            using var tarInput = new TarInputStream(tarStream, null);

            TarEntry? entry;
            while ((entry = tarInput.GetNextEntry()) != null)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (entry.IsDirectory)
                {
                    // Create directory
                    var dirPath = Path.Combine(targetDirectory, entry.Name);
                    Directory.CreateDirectory(dirPath);
                }
                else
                {
                    // Extract file
                    var filePath = Path.Combine(targetDirectory, entry.Name);

                    // Ensure parent directory exists
                    var fileDir = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(fileDir))
                    {
                        Directory.CreateDirectory(fileDir);
                    }

                    using var fileStream = File.Create(filePath);
                    tarInput.CopyEntryContents(fileStream);
                    fileCount++;
                }
            }
        }, cancellationToken);

        return fileCount;
    }

    /// <summary>
    /// Creates a tar archive from a source directory.
    /// </summary>
    /// <param name="sourceDirectory">The directory to archive.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A memory stream containing the tar archive.</returns>
    public static async Task<MemoryStream> CreateTarAsync(
        string sourceDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory))
        {
            throw new ArgumentException("Source directory cannot be null or empty.", nameof(sourceDirectory));
        }

        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException($"Source directory not found: {sourceDirectory}");
        }

        var memoryStream = new MemoryStream();

        await Task.Run(() =>
        {
            using var tarOutput = new TarOutputStream(memoryStream, null);
            tarOutput.IsStreamOwner = false; // Don't close the underlying stream

            AddDirectoryToTar(tarOutput, sourceDirectory, "", cancellationToken);

            tarOutput.Finish();
        }, cancellationToken);

        memoryStream.Position = 0;
        return memoryStream;
    }

    /// <summary>
    /// Creates a tar archive from specific files in a source directory.
    /// </summary>
    /// <param name="sourceDirectory">The base directory for relative paths.</param>
    /// <param name="relativePaths">The relative paths of files to include.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A memory stream containing the tar archive.</returns>
    public static async Task<MemoryStream> CreateTarFromFilesAsync(
        string sourceDirectory,
        IEnumerable<string> relativePaths,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory))
        {
            throw new ArgumentException("Source directory cannot be null or empty.", nameof(sourceDirectory));
        }

        if (relativePaths == null)
        {
            throw new ArgumentNullException(nameof(relativePaths));
        }

        var memoryStream = new MemoryStream();

        await Task.Run(() =>
        {
            using var tarOutput = new TarOutputStream(memoryStream, null);
            tarOutput.IsStreamOwner = false;

            foreach (var relativePath in relativePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fullPath = Path.Combine(sourceDirectory, relativePath);

                if (File.Exists(fullPath))
                {
                    AddFileToTar(tarOutput, fullPath, relativePath);
                }
            }

            tarOutput.Finish();
        }, cancellationToken);

        memoryStream.Position = 0;
        return memoryStream;
    }

    /// <summary>
    /// Recursively adds a directory and its contents to a tar archive.
    /// </summary>
    private static void AddDirectoryToTar(
        TarOutputStream tarOutput,
        string sourceDirectory,
        string basePath,
        CancellationToken cancellationToken)
    {
        // Add files in current directory
        foreach (var filePath in Directory.GetFiles(sourceDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileName = Path.GetFileName(filePath);
            var entryName = string.IsNullOrEmpty(basePath)
                ? fileName
                : $"{basePath}/{fileName}";

            AddFileToTar(tarOutput, filePath, entryName);
        }

        // Recursively add subdirectories
        foreach (var subDir in Directory.GetDirectories(sourceDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var dirName = Path.GetFileName(subDir);
            var entryPath = string.IsNullOrEmpty(basePath)
                ? dirName
                : $"{basePath}/{dirName}";

            // Add directory entry
            var dirEntry = TarEntry.CreateTarEntry(entryPath + "/");
            dirEntry.TarHeader.TypeFlag = TarHeader.LF_DIR;
            tarOutput.PutNextEntry(dirEntry);
            tarOutput.CloseEntry();

            // Recursively add contents
            AddDirectoryToTar(tarOutput, subDir, entryPath, cancellationToken);
        }
    }

    /// <summary>
    /// Adds a single file to a tar archive.
    /// </summary>
    private static void AddFileToTar(TarOutputStream tarOutput, string filePath, string entryName)
    {
        var fileInfo = new FileInfo(filePath);

        // Create tar entry
        var entry = TarEntry.CreateTarEntry(entryName);
        entry.Size = fileInfo.Length;
        entry.ModTime = fileInfo.LastWriteTimeUtc;

        tarOutput.PutNextEntry(entry);

        // Write file contents
        using var fileStream = File.OpenRead(filePath);
        fileStream.CopyTo(tarOutput);

        tarOutput.CloseEntry();
    }
}
