namespace PDK.Core.Secrets;

using System.Diagnostics;

/// <summary>
/// File-system helpers shared by the secret storage and key management code:
/// owner-only file creation, atomic replacement, and a cross-process lock file.
/// </summary>
internal static class SecretFiles
{
    /// <summary>Unix mode 0600: owner read/write only.</summary>
    internal const UnixFileMode OwnerOnlyFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    /// <summary>Unix mode 0700: owner read/write/execute only.</summary>
    internal const UnixFileMode OwnerOnlyDirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    /// <summary>Default time to wait for the cross-process lock.</summary>
    internal static readonly TimeSpan DefaultLockTimeout = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromMilliseconds(10);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Ensures that <paramref name="directory"/> exists. When it has to be created on Unix it is
    /// created with mode 0700; an existing directory's permissions are left untouched.
    /// </summary>
    internal static void EnsureDirectory(string? directory)
    {
        if (string.IsNullOrEmpty(directory) || Directory.Exists(directory))
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(directory);
        }
        else
        {
            Directory.CreateDirectory(directory, OwnerOnlyDirectoryMode);
        }
    }

    /// <summary>
    /// Creates a new file that only the current user can read or write (mode 0600 on Unix).
    /// The file must not exist yet.
    /// </summary>
    internal static FileStream CreateOwnerOnlyFile(string path)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.WriteThrough
        };

        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = OwnerOnlyFileMode;
        }

        return new FileStream(path, options);
    }

    /// <summary>
    /// Restricts the permissions of an existing file to the owner (mode 0600) on Unix.
    /// Failures are ignored: the file is already written and some file systems do not support modes.
    /// </summary>
    internal static void TryRestrictToOwner(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(path, OwnerOnlyFileMode);
        }
        catch (IOException)
        {
            // Ignore: file system without permission support.
        }
        catch (UnauthorizedAccessException)
        {
            // Ignore: not the owner of the file.
        }
        catch (PlatformNotSupportedException)
        {
            // Ignore.
        }
    }

    /// <summary>
    /// Writes <paramref name="content"/> to <paramref name="path"/> atomically: the content is written to a
    /// temporary owner-only file in the same directory and then moved over the target, so readers see either
    /// the previous or the new content, never a partial file.
    /// </summary>
    /// <param name="path">The target file.</param>
    /// <param name="content">The bytes to write.</param>
    /// <param name="overwrite">
    /// When false the target must not exist; if it appears concurrently the temporary file is discarded and
    /// an <see cref="IOException"/> is thrown so the caller can read the winner's content.
    /// </param>
    internal static void WriteAtomically(string path, ReadOnlySpan<byte> content, bool overwrite)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        EnsureDirectory(directory);

        var tempPath = CreateTempPath(fullPath);
        try
        {
            using (var stream = CreateOwnerOnlyFile(tempPath))
            {
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, fullPath, overwrite);
            TryRestrictToOwner(fullPath);
        }
        finally
        {
            TryDelete(tempPath);
        }
    }

    /// <summary>
    /// Builds a unique temporary path next to <paramref name="targetPath"/> (same directory, so the final
    /// rename stays on one volume and is atomic).
    /// </summary>
    internal static string CreateTempPath(string targetPath)
    {
        return $"{targetPath}.{Guid.NewGuid():N}.tmp";
    }

    /// <summary>
    /// Deletes stale temporary files left next to <paramref name="targetPath"/> by a crashed writer.
    /// </summary>
    internal static void CleanupStaleTempFiles(string targetPath, TimeSpan olderThan)
    {
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(targetPath));
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return;
            }

            var pattern = Path.GetFileName(targetPath) + ".*.tmp";
            var cutoff = DateTime.UtcNow - olderThan;
            foreach (var stale in Directory.EnumerateFiles(directory, pattern))
            {
                if (File.GetLastWriteTimeUtc(stale) < cutoff)
                {
                    TryDelete(stale);
                }
            }
        }
        catch (IOException)
        {
            // Best effort only.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort only.
        }
    }

    /// <summary>
    /// Deletes a file, ignoring failures.
    /// </summary>
    internal static void TryDelete(string path)
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
            // Ignore.
        }
        catch (UnauthorizedAccessException)
        {
            // Ignore.
        }
    }

    /// <summary>
    /// Acquires a cross-process lock by opening <paramref name="lockPath"/> with <see cref="FileShare.None"/>
    /// (an exclusive advisory lock on Unix, a sharing-mode lock on Windows), retrying with backoff until
    /// <paramref name="timeout"/> elapses. The lock file itself is never deleted, which keeps the lock
    /// race-free between processes.
    /// </summary>
    /// <returns>A handle that releases the lock when disposed.</returns>
    /// <exception cref="TimeoutException">The lock could not be acquired in time.</exception>
    internal static async Task<IDisposable> AcquireLockAsync(
        string lockPath,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        EnsureDirectory(Path.GetDirectoryName(Path.GetFullPath(lockPath)));

        var stopwatch = Stopwatch.StartNew();
        var delay = InitialRetryDelay;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return new LockHandle(OpenLockFile(lockPath));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (stopwatch.Elapsed >= timeout)
                {
                    throw new TimeoutException(
                        $"Could not acquire lock '{lockPath}' within {timeout.TotalSeconds:0.#}s", ex);
                }
            }

            await Task.Delay(delay, cancellationToken);
            delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, MaxRetryDelay.TotalMilliseconds));
        }
    }

    /// <summary>
    /// Synchronous variant of <see cref="AcquireLockAsync"/> for callers that cannot await.
    /// </summary>
    internal static IDisposable AcquireLock(string lockPath, TimeSpan timeout)
    {
        EnsureDirectory(Path.GetDirectoryName(Path.GetFullPath(lockPath)));

        var stopwatch = Stopwatch.StartNew();
        var delay = InitialRetryDelay;
        while (true)
        {
            try
            {
                return new LockHandle(OpenLockFile(lockPath));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (stopwatch.Elapsed >= timeout)
                {
                    throw new TimeoutException(
                        $"Could not acquire lock '{lockPath}' within {timeout.TotalSeconds:0.#}s", ex);
                }
            }

            Thread.Sleep(delay);
            delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, MaxRetryDelay.TotalMilliseconds));
        }
    }

    private static FileStream OpenLockFile(string lockPath)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.OpenOrCreate,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None,
            BufferSize = 1
        };

        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = OwnerOnlyFileMode;
        }

        return new FileStream(lockPath, options);
    }

    private sealed class LockHandle : IDisposable
    {
        private FileStream? _stream;

        public LockHandle(FileStream stream)
        {
            _stream = stream;
        }

        public void Dispose()
        {
            var stream = Interlocked.Exchange(ref _stream, null);
            stream?.Dispose();
        }
    }
}
