using System.Text;
using System.Threading.Channels;
using ICSharpCode.SharpZipLib.Tar;

namespace PDK.Runners.Utilities;

/// <summary>
/// Provides utility methods for working with tar archives.
/// Used for Docker container file copy operations.
/// </summary>
/// <remarks>
/// Extraction is hardened against archives that try to escape the target directory (entries containing
/// <c>..</c>, absolute paths, or writes through symbolic links that point outside the target). Symbolic
/// links are recreated when their target stays inside the extraction directory and the platform allows it;
/// otherwise they are skipped with a warning. Unix file modes are preserved. Archives can be written
/// directly to a stream so large workspaces are not buffered in memory.
/// </remarks>
public static class TarArchiveHelper
{
    private const int DefaultFileMode = 0x1A4;      // 0644
    private const int DefaultDirectoryMode = 0x1ED; // 0755
    private const int PermissionBits = 0x1FF;       // 0777

    /// <summary>
    /// Extracts a tar archive stream to a target directory.
    /// </summary>
    /// <param name="tarStream">The tar archive stream to extract.</param>
    /// <param name="targetDirectory">The directory to extract files to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of files extracted.</returns>
    /// <exception cref="InvalidDataException">Thrown when an entry would escape the target directory.</exception>
    public static Task<int> ExtractTarAsync(
        Stream tarStream,
        string targetDirectory,
        CancellationToken cancellationToken = default)
    {
        return ExtractTarAsync(tarStream, targetDirectory, null, cancellationToken);
    }

    /// <summary>
    /// Extracts a tar archive stream to a target directory, reporting skipped entries through <paramref name="onWarning"/>.
    /// </summary>
    /// <param name="tarStream">The tar archive stream to extract.</param>
    /// <param name="targetDirectory">The directory to extract files to.</param>
    /// <param name="onWarning">Receives a message for every entry that was skipped (unsupported type, symlink that cannot be created).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of files extracted.</returns>
    /// <exception cref="InvalidDataException">Thrown when an entry would escape the target directory.</exception>
    public static async Task<int> ExtractTarAsync(
        Stream tarStream,
        string targetDirectory,
        Action<string>? onWarning,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tarStream);

        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            throw new ArgumentException("Target directory cannot be null or empty.", nameof(targetDirectory));
        }

        Directory.CreateDirectory(targetDirectory);
        var root = Path.GetFullPath(targetDirectory);

        return await Task.Run(() => ExtractCore(tarStream, root, onWarning, cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a tar archive from a source directory, buffered in memory.
    /// Prefer <see cref="WriteTarAsync"/> or <see cref="CreateTarStream"/> for large directories.
    /// </summary>
    /// <param name="sourceDirectory">The directory to archive.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A memory stream containing the tar archive.</returns>
    public static async Task<MemoryStream> CreateTarAsync(
        string sourceDirectory,
        CancellationToken cancellationToken = default)
    {
        var memoryStream = new MemoryStream();
        await WriteTarAsync(sourceDirectory, memoryStream, cancellationToken).ConfigureAwait(false);
        memoryStream.Position = 0;
        return memoryStream;
    }

    /// <summary>
    /// Writes a tar archive of a source directory to <paramref name="destination"/> without buffering it in memory.
    /// </summary>
    /// <param name="sourceDirectory">The directory to archive.</param>
    /// <param name="destination">The stream to write the archive to (left open).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task WriteTarAsync(
        string sourceDirectory,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ValidateSourceDirectory(sourceDirectory);
        ArgumentNullException.ThrowIfNull(destination);

        await Task.Run(() =>
        {
            using var tarOutput = new TarOutputStream(destination, Encoding.UTF8) { IsStreamOwner = false };
            AddDirectoryToTar(tarOutput, sourceDirectory, string.Empty, cancellationToken);
            tarOutput.Finish();
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a readable stream that produces the tar archive of <paramref name="sourceDirectory"/> on the fly
    /// (a background writer feeds a bounded pipe), so the archive is never held in memory as a whole.
    /// Dispose the stream to stop the writer.
    /// </summary>
    /// <param name="sourceDirectory">The directory to archive.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A read-only stream yielding the archive bytes.</returns>
    public static Stream CreateTarStream(string sourceDirectory, CancellationToken cancellationToken = default)
    {
        ValidateSourceDirectory(sourceDirectory);
        return new PipedStream(destination => WriteTarAsync(sourceDirectory, destination, cancellationToken));
    }

    /// <summary>
    /// Creates a tar archive from specific files in a source directory, buffered in memory.
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
        var memoryStream = new MemoryStream();
        await WriteTarFromFilesAsync(sourceDirectory, relativePaths, memoryStream, cancellationToken).ConfigureAwait(false);
        memoryStream.Position = 0;
        return memoryStream;
    }

    /// <summary>
    /// Writes a tar archive of specific files in a source directory to <paramref name="destination"/>.
    /// </summary>
    /// <param name="sourceDirectory">The base directory for relative paths.</param>
    /// <param name="relativePaths">The relative paths of files to include.</param>
    /// <param name="destination">The stream to write the archive to (left open).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task WriteTarFromFilesAsync(
        string sourceDirectory,
        IEnumerable<string> relativePaths,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory))
        {
            throw new ArgumentException("Source directory cannot be null or empty.", nameof(sourceDirectory));
        }

        ArgumentNullException.ThrowIfNull(relativePaths);
        ArgumentNullException.ThrowIfNull(destination);

        await Task.Run(() =>
        {
            using var tarOutput = new TarOutputStream(destination, Encoding.UTF8) { IsStreamOwner = false };

            foreach (var relativePath in relativePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fullPath = Path.Combine(sourceDirectory, relativePath);
                if (File.Exists(fullPath))
                {
                    AddFileToTar(tarOutput, fullPath, relativePath.Replace('\\', '/'));
                }
            }

            tarOutput.Finish();
        }, cancellationToken).ConfigureAwait(false);
    }

    private static int ExtractCore(Stream tarStream, string root, Action<string>? onWarning, CancellationToken cancellationToken)
    {
        var fileCount = 0;
        var directoryModes = new List<(string Path, int Mode)>();

        using var tarInput = new TarInputStream(tarStream, Encoding.UTF8) { IsStreamOwner = false };

        TarEntry? entry;
        while ((entry = tarInput.GetNextEntry()) != null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relative = NormalizeEntryName(entry.Name);
            if (relative == null)
            {
                continue; // "." / "./" root markers
            }

            var fullPath = ResolveInsideRoot(root, relative);
            var typeFlag = entry.TarHeader.TypeFlag;

            if (entry.IsDirectory || typeFlag == TarHeader.LF_DIR)
            {
                EnsureParentDirectory(root, fullPath, relative);
                Directory.CreateDirectory(fullPath);
                directoryModes.Add((fullPath, entry.TarHeader.Mode));
                continue;
            }

            EnsureParentDirectory(root, fullPath, relative);

            switch (typeFlag)
            {
                case TarHeader.LF_SYMLINK:
                    CreateSymbolicLink(root, fullPath, relative, entry.TarHeader.LinkName, onWarning);
                    break;

                case TarHeader.LF_LINK:
                {
                    var linkTarget = NormalizeEntryName(entry.TarHeader.LinkName ?? string.Empty);
                    var sourcePath = linkTarget == null ? null : ResolveInsideRoot(root, linkTarget);
                    if (sourcePath != null && File.Exists(sourcePath))
                    {
                        File.Copy(sourcePath, fullPath, overwrite: true);
                        fileCount++;
                    }
                    else
                    {
                        onWarning?.Invoke($"Skipping hard link '{relative}': target '{entry.TarHeader.LinkName}' was not extracted");
                    }

                    break;
                }

                case TarHeader.LF_NORMAL:
                case TarHeader.LF_OLDNORM:
                case TarHeader.LF_CONTIG:
                {
                    RemoveExistingLink(fullPath);
                    using (var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920))
                    {
                        tarInput.CopyEntryContents(fileStream);
                    }

                    ApplyFileMode(fullPath, entry.TarHeader.Mode);
                    TrySetModificationTime(fullPath, entry.ModTime);
                    fileCount++;
                    break;
                }

                default:
                    onWarning?.Invoke($"Skipping unsupported tar entry type '{(char)typeFlag}' for '{relative}'");
                    break;
            }
        }

        // Directory modes are applied last so restrictive modes do not block the files inside them.
        foreach (var (path, mode) in directoryModes.OrderByDescending(d => d.Path.Length))
        {
            ApplyFileMode(path, mode);
        }

        return fileCount;
    }

    /// <summary>
    /// Normalizes an archive entry name to a forward-slash relative path; null for the root marker.
    /// </summary>
    internal static string? NormalizeEntryName(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        var normalized = name.Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal) || normalized.StartsWith('/'))
        {
            normalized = normalized.StartsWith("./", StringComparison.Ordinal) ? normalized[2..] : normalized[1..];
        }

        normalized = normalized.TrimEnd('/');
        return normalized.Length == 0 || normalized == "." ? null : normalized;
    }

    /// <summary>
    /// Resolves an entry path below the root and rejects anything that would escape it.
    /// </summary>
    internal static string ResolveInsideRoot(string root, string relative)
    {
        var segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(s => s == ".."))
        {
            throw new InvalidDataException($"Tar entry '{relative}' escapes the target directory.");
        }

        var fullPath = Path.GetFullPath(Path.Combine(root, Path.Combine(segments)));
        if (!IsInside(root, fullPath) || string.Equals(fullPath, root, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Tar entry '{relative}' escapes the target directory.");
        }

        return fullPath;
    }

    private static bool IsInside(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path);
        if (relative == ".")
        {
            return true;
        }

        return !Path.IsPathRooted(relative) &&
               relative != ".." &&
               !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
               !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    /// <summary>
    /// Creates the parent directory of an entry, refusing to descend through a symbolic link that leaves the root.
    /// </summary>
    private static void EnsureParentDirectory(string root, string fullPath, string relative)
    {
        var parent = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(parent))
        {
            return;
        }

        var relativeParent = Path.GetRelativePath(root, parent);
        if (relativeParent != ".")
        {
            var current = root;
            foreach (var segment in relativeParent.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            {
                current = Path.Combine(current, segment);
                var info = new DirectoryInfo(current);
                if (info.Exists && info.LinkTarget != null)
                {
                    var resolved = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName;
                    if (resolved == null || !IsInside(root, resolved))
                    {
                        throw new InvalidDataException(
                            $"Tar entry '{relative}' would be written through a symbolic link that leaves the target directory.");
                    }
                }
            }
        }

        Directory.CreateDirectory(parent);
    }

    private static void CreateSymbolicLink(string root, string linkPath, string relative, string? target, Action<string>? onWarning)
    {
        if (string.IsNullOrEmpty(target))
        {
            onWarning?.Invoke($"Skipping symbolic link '{relative}': empty target");
            return;
        }

        var normalizedTarget = target.Replace('\\', '/');
        if (normalizedTarget.StartsWith('/') || Path.IsPathRooted(target))
        {
            onWarning?.Invoke($"Skipping symbolic link '{relative}' -> '{target}': absolute targets are not recreated");
            return;
        }

        var linkDirectory = Path.GetDirectoryName(linkPath)!;
        var resolvedTarget = Path.GetFullPath(Path.Combine(linkDirectory, normalizedTarget.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsInside(root, resolvedTarget))
        {
            onWarning?.Invoke($"Skipping symbolic link '{relative}' -> '{target}': target is outside the extraction directory");
            return;
        }

        try
        {
            RemoveExistingLink(linkPath);
            var osTarget = normalizedTarget.Replace('/', Path.DirectorySeparatorChar);

            if (Directory.Exists(resolvedTarget))
            {
                Directory.CreateSymbolicLink(linkPath, osTarget);
            }
            else
            {
                File.CreateSymbolicLink(linkPath, osTarget);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            onWarning?.Invoke($"Skipping symbolic link '{relative}' -> '{target}': {ex.Message}");
        }
    }

    private static void RemoveExistingLink(string path)
    {
        var info = new FileInfo(path);
        if (info.Exists)
        {
            if (info.LinkTarget != null)
            {
                info.Delete();
            }

            return;
        }

        var directory = new DirectoryInfo(path);
        if (directory.Exists && directory.LinkTarget != null)
        {
            directory.Delete();
        }
    }

    private static void ApplyFileMode(string path, int mode)
    {
        var permissions = mode & PermissionBits;
        if (permissions == 0 || OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(path, (UnixFileMode)permissions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // Best effort: not all file systems support modes.
        }
    }

    private static void TrySetModificationTime(string path, DateTime modTime)
    {
        try
        {
            if (modTime > DateTime.UnixEpoch)
            {
                File.SetLastWriteTimeUtc(path, modTime.ToUniversalTime());
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Best effort.
        }
    }

    private static void ValidateSourceDirectory(string sourceDirectory)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory))
        {
            throw new ArgumentException("Source directory cannot be null or empty.", nameof(sourceDirectory));
        }

        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException($"Source directory not found: {sourceDirectory}");
        }
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
        foreach (var filePath in Directory.GetFiles(sourceDirectory).OrderBy(p => p, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileName = Path.GetFileName(filePath);
            var entryName = string.IsNullOrEmpty(basePath) ? fileName : $"{basePath}/{fileName}";

            var info = new FileInfo(filePath);
            if (info.LinkTarget != null)
            {
                AddSymbolicLinkToTar(tarOutput, entryName, info.LinkTarget);
                continue;
            }

            AddFileToTar(tarOutput, filePath, entryName);
        }

        foreach (var subDir in Directory.GetDirectories(sourceDirectory).OrderBy(p => p, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var dirName = Path.GetFileName(subDir);
            var entryPath = string.IsNullOrEmpty(basePath) ? dirName : $"{basePath}/{dirName}";

            var info = new DirectoryInfo(subDir);
            if (info.LinkTarget != null)
            {
                AddSymbolicLinkToTar(tarOutput, entryPath, info.LinkTarget);
                continue;
            }

            var dirEntry = TarEntry.CreateTarEntry(entryPath + "/");
            dirEntry.TarHeader.TypeFlag = TarHeader.LF_DIR;
            dirEntry.TarHeader.Mode = GetUnixMode(subDir, DefaultDirectoryMode);
            dirEntry.ModTime = info.LastWriteTimeUtc;
            tarOutput.PutNextEntry(dirEntry);
            tarOutput.CloseEntry();

            AddDirectoryToTar(tarOutput, subDir, entryPath, cancellationToken);
        }
    }

    /// <summary>
    /// Adds a single file to a tar archive, preserving its Unix mode.
    /// </summary>
    private static void AddFileToTar(TarOutputStream tarOutput, string filePath, string entryName)
    {
        var fileInfo = new FileInfo(filePath);

        var entry = TarEntry.CreateTarEntry(entryName);
        entry.TarHeader.TypeFlag = TarHeader.LF_NORMAL;
        entry.Size = fileInfo.Length;
        entry.ModTime = fileInfo.LastWriteTimeUtc;
        entry.TarHeader.Mode = GetUnixMode(filePath, DefaultFileMode);

        tarOutput.PutNextEntry(entry);

        using (var fileStream = File.OpenRead(filePath))
        {
            fileStream.CopyTo(tarOutput);
        }

        tarOutput.CloseEntry();
    }

    private static void AddSymbolicLinkToTar(TarOutputStream tarOutput, string entryName, string linkTarget)
    {
        var entry = TarEntry.CreateTarEntry(entryName);
        entry.TarHeader.TypeFlag = TarHeader.LF_SYMLINK;
        entry.TarHeader.LinkName = linkTarget.Replace('\\', '/');
        entry.TarHeader.Mode = 0x1FF;
        entry.Size = 0;
        entry.ModTime = DateTime.UtcNow;

        tarOutput.PutNextEntry(entry);
        tarOutput.CloseEntry();
    }

    private static int GetUnixMode(string path, int fallback)
    {
        if (OperatingSystem.IsWindows())
        {
            return fallback;
        }

        try
        {
            return (int)File.GetUnixFileMode(path) & PermissionBits;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return fallback;
        }
    }

    /// <summary>
    /// A read-only stream fed by a background writer through a bounded channel of chunks.
    /// </summary>
    private sealed class PipedStream : Stream
    {
        private const int ChunkCapacity = 32;

        private readonly Channel<byte[]> _channel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(ChunkCapacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });

        private readonly CancellationTokenSource _writerCancellation = new();
        private readonly Task _writer;
        private byte[]? _current;
        private int _offset;

        public PipedStream(Func<Stream, Task> producer)
        {
            _writer = Task.Run(async () =>
            {
                try
                {
                    var sink = new ChannelWriterStream(_channel.Writer, _writerCancellation.Token);
                    await using (sink.ConfigureAwait(false))
                    {
                        await producer(sink).ConfigureAwait(false);
                    }

                    _channel.Writer.TryComplete();
                }
                catch (Exception ex)
                {
                    _channel.Writer.TryComplete(ex);
                }
            });
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return ReadAsync(buffer.AsMemory(offset, count), CancellationToken.None).AsTask().GetAwaiter().GetResult();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            while (_current == null || _offset >= _current.Length)
            {
                try
                {
                    if (!await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                    {
                        return 0;
                    }
                }
                catch (ChannelClosedException ex) when (ex.InnerException != null)
                {
                    throw new IOException("Failed to produce the tar archive.", ex.InnerException);
                }

                if (!_channel.Reader.TryRead(out _current))
                {
                    _current = null;
                    continue;
                }

                _offset = 0;
            }

            var toCopy = Math.Min(buffer.Length, _current.Length - _offset);
            _current.AsMemory(_offset, toCopy).CopyTo(buffer);
            _offset += toCopy;
            return toCopy;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _writerCancellation.Cancel();
                _channel.Writer.TryComplete();
                try
                {
                    _writer.Wait(TimeSpan.FromSeconds(5));
                }
                catch (AggregateException)
                {
                    // Writer failures surface through Read; nothing to do on dispose.
                }

                _writerCancellation.Dispose();
            }

            base.Dispose(disposing);
        }

        private sealed class ChannelWriterStream : Stream
        {
            private readonly ChannelWriter<byte[]> _writer;
            private readonly CancellationToken _cancellationToken;

            public ChannelWriterStream(ChannelWriter<byte[]> writer, CancellationToken cancellationToken)
            {
                _writer = writer;
                _cancellationToken = cancellationToken;
            }

            public override bool CanRead => false;

            public override bool CanSeek => false;

            public override bool CanWrite => true;

            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                if (count == 0)
                {
                    return;
                }

                var chunk = new byte[count];
                Buffer.BlockCopy(buffer, offset, chunk, 0, count);
                _writer.WriteAsync(chunk, _cancellationToken).AsTask().GetAwaiter().GetResult();
            }

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();
        }
    }
}
