namespace PDK.Tests.Unit.WatchMode;

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using PDK.CLI.WatchMode;
using Xunit;

public class FileWatcherTests : IDisposable
{
    private readonly string _testDir;
    private readonly FileWatcher _watcher;

    public FileWatcherTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"pdk-watcher-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _watcher = new FileWatcher(NullLogger<FileWatcher>.Instance);
    }

    public void Dispose()
    {
        _watcher.Dispose();
        try
        {
            Directory.Delete(_testDir, recursive: true);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public void Constructor_InitializesAsNotWatching()
    {
        // Assert
        _watcher.IsWatching.Should().BeFalse();
        _watcher.WatchedDirectory.Should().BeNull();
    }

    [Fact]
    public void Start_SetsIsWatchingToTrue()
    {
        // Act
        _watcher.Start(_testDir, new FileWatcherOptions());

        // Assert
        _watcher.IsWatching.Should().BeTrue();
        _watcher.WatchedDirectory.Should().Be(Path.GetFullPath(_testDir));
    }

    [Fact]
    public void Start_WithNonExistentDirectory_ThrowsDirectoryNotFoundException()
    {
        // Arrange
        var nonExistentDir = Path.Combine(_testDir, "nonexistent");

        // Act
        var act = () => _watcher.Start(nonExistentDir, new FileWatcherOptions());

        // Assert
        act.Should().Throw<DirectoryNotFoundException>();
    }

    [Fact]
    public void Stop_SetsIsWatchingToFalse()
    {
        // Arrange
        _watcher.Start(_testDir, new FileWatcherOptions());

        // Act
        _watcher.Stop();

        // Assert
        _watcher.IsWatching.Should().BeFalse();
    }

    [Fact]
    public async Task FileChanged_DetectsFileCreation()
    {
        // Arrange
        var tcs = new TaskCompletionSource<FileChangeEvent>();
        _watcher.FileChanged += (_, e) => tcs.TrySetResult(e);
        _watcher.Start(_testDir, new FileWatcherOptions());

        // Act
        var testFile = Path.Combine(_testDir, "newfile.txt");
        await File.WriteAllTextAsync(testFile, "test content");

        // Assert
        var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // Note: macOS may report file creation as Modified instead of Created
        // The important thing is that the change is detected
        result.ChangeType.Should().BeOneOf(FileChangeType.Created, FileChangeType.Modified);
        result.RelativePath.Should().Be("newfile.txt");
    }

    [Fact]
    public async Task FileChanged_DetectsFileModification()
    {
        // Arrange
        var testFile = Path.Combine(_testDir, "existing.txt");
        await File.WriteAllTextAsync(testFile, "initial");

        var tcs = new TaskCompletionSource<FileChangeEvent>();
        _watcher.FileChanged += (_, e) =>
        {
            if (e.ChangeType == FileChangeType.Modified)
                tcs.TrySetResult(e);
        };
        _watcher.Start(_testDir, new FileWatcherOptions());

        // Act
        await Task.Delay(100); // Wait for watcher to settle
        await File.WriteAllTextAsync(testFile, "modified");

        // Assert
        var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        result.ChangeType.Should().Be(FileChangeType.Modified);
        result.RelativePath.Should().Be("existing.txt");
    }

    [Fact]
    public async Task FileChanged_DetectsFileDeletion()
    {
        // Arrange
        var testFile = Path.Combine(_testDir, "todelete.txt");
        await File.WriteAllTextAsync(testFile, "will be deleted");

        var tcs = new TaskCompletionSource<FileChangeEvent>();
        _watcher.FileChanged += (_, e) =>
        {
            if (e.ChangeType == FileChangeType.Deleted)
                tcs.TrySetResult(e);
        };
        _watcher.Start(_testDir, new FileWatcherOptions());

        // Act
        await Task.Delay(100);
        File.Delete(testFile);

        // Assert
        var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        result.ChangeType.Should().Be(FileChangeType.Deleted);
        result.RelativePath.Should().Be("todelete.txt");
    }

    [Fact]
    public async Task FileChanged_IgnoresExcludedPatterns()
    {
        // Arrange
        var changesReceived = new List<FileChangeEvent>();
        _watcher.FileChanged += (_, e) => changesReceived.Add(e);

        var options = new FileWatcherOptions();
        options.ExcludePatterns.Add("**/*.log");

        _watcher.Start(_testDir, options);

        // Act
        var testFile = Path.Combine(_testDir, "test.log");
        await File.WriteAllTextAsync(testFile, "log content");
        await Task.Delay(200);

        // Assert
        changesReceived.Should().BeEmpty();
    }

    [Fact]
    public async Task FileChanged_IgnoresGitDirectory()
    {
        // Arrange
        var changesReceived = new List<FileChangeEvent>();
        _watcher.FileChanged += (_, e) => changesReceived.Add(e);
        _watcher.Start(_testDir, new FileWatcherOptions());

        // Act
        var gitDir = Path.Combine(_testDir, ".git");
        Directory.CreateDirectory(gitDir);
        var gitFile = Path.Combine(gitDir, "config");
        await File.WriteAllTextAsync(gitFile, "git config");
        await Task.Delay(200);

        // Assert
        changesReceived.Where(c => c.RelativePath.Contains(".git")).Should().BeEmpty();
    }

    [Fact]
    public async Task FileChanged_IgnoresNodeModules()
    {
        // Arrange
        var changesReceived = new List<FileChangeEvent>();
        _watcher.FileChanged += (_, e) => changesReceived.Add(e);
        _watcher.Start(_testDir, new FileWatcherOptions());

        // Act
        var nodeDir = Path.Combine(_testDir, "node_modules");
        Directory.CreateDirectory(nodeDir);
        var nodeFile = Path.Combine(nodeDir, "package.json");
        await File.WriteAllTextAsync(nodeFile, "{}");
        await Task.Delay(200);

        // Assert
        changesReceived.Where(c => c.RelativePath.Contains("node_modules")).Should().BeEmpty();
    }

    [Fact]
    public async Task FileChanged_IgnoresBinaryFiles()
    {
        // Arrange
        var changesReceived = new List<FileChangeEvent>();
        _watcher.FileChanged += (_, e) => changesReceived.Add(e);
        _watcher.Start(_testDir, new FileWatcherOptions());

        // Act
        var dllFile = Path.Combine(_testDir, "test.dll");
        await File.WriteAllBytesAsync(dllFile, new byte[] { 0x00, 0x01, 0x02 });
        await Task.Delay(200);

        // Assert
        changesReceived.Should().BeEmpty();
    }

    [Fact]
    public void ExcludedPatterns_ReturnsConfiguredPatterns()
    {
        // Arrange
        var options = new FileWatcherOptions();
        options.UserExcludePatterns.Add("*.tmp");

        // Act
        _watcher.Start(_testDir, options);

        // Assert
        _watcher.ExcludedPatterns.Should().Contain("*.tmp");
        _watcher.ExcludedPatterns.Should().Contain(".git/**");
        _watcher.ExcludedPatterns.Should().Contain("node_modules/**");
    }

    [Fact]
    public void Start_CalledTwice_StopsPreviousWatcher()
    {
        // Arrange
        _watcher.Start(_testDir, new FileWatcherOptions());
        var firstDir = _watcher.WatchedDirectory;

        var secondDir = Path.Combine(_testDir, "subdir");
        Directory.CreateDirectory(secondDir);

        // Act
        _watcher.Start(secondDir, new FileWatcherOptions());

        // Assert
        _watcher.IsWatching.Should().BeTrue();
        _watcher.WatchedDirectory.Should().Be(Path.GetFullPath(secondDir));
    }

    [Fact]
    public void Dispose_DisposesCleanly()
    {
        // Arrange
        _watcher.Start(_testDir, new FileWatcherOptions());

        // Act & Assert (should not throw)
        _watcher.Dispose();
        _watcher.IsWatching.Should().BeFalse();
    }
}
