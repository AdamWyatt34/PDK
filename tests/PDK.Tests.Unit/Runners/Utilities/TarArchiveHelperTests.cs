namespace PDK.Tests.Unit.Runners.Utilities;

using System.Diagnostics;
using System.Text;
using FluentAssertions;
using ICSharpCode.SharpZipLib.Tar;
using PDK.Runners.Utilities;

/// <summary>
/// Unit tests for <see cref="TarArchiveHelper"/>: round trips, streaming, mode/symlink preservation and
/// hardening against archives that try to escape the extraction directory.
/// </summary>
public sealed class TarArchiveHelperTests : IDisposable
{
    private const int Mode644 = 0x1A4;
    private const int Mode755 = 0x1ED;
    private const int Mode600 = 0x180;
    private const int Mode500 = 0x140;

    private readonly string _root;

    public TarArchiveHelperTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "pdk-tar-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort cleanup.
        }
    }

    private string NewDirectory(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteFile(string directory, string relativePath, string content)
    {
        var path = Path.Combine(directory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static MemoryStream BuildTar(Action<TarOutputStream> build)
    {
        var stream = new MemoryStream();
        using (var tar = new TarOutputStream(stream, Encoding.UTF8) { IsStreamOwner = false })
        {
            build(tar);
            tar.Finish();
        }

        stream.Position = 0;
        return stream;
    }

    private static void AddFile(TarOutputStream tar, string name, string content, int mode = Mode644)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var entry = TarEntry.CreateTarEntry(name);
        entry.TarHeader.Name = name;
        entry.TarHeader.TypeFlag = TarHeader.LF_NORMAL;
        entry.TarHeader.Mode = mode;
        entry.Size = bytes.Length;
        tar.PutNextEntry(entry);
        tar.Write(bytes, 0, bytes.Length);
        tar.CloseEntry();
    }

    private static void AddDirectory(TarOutputStream tar, string name, int mode = Mode755)
    {
        var entry = TarEntry.CreateTarEntry(name.EndsWith('/') ? name : name + "/");
        entry.TarHeader.TypeFlag = TarHeader.LF_DIR;
        entry.TarHeader.Mode = mode;
        entry.Size = 0;
        tar.PutNextEntry(entry);
        tar.CloseEntry();
    }

    private static void AddLink(TarOutputStream tar, string name, string target, byte typeFlag)
    {
        var entry = TarEntry.CreateTarEntry(name);
        entry.TarHeader.TypeFlag = typeFlag;
        entry.TarHeader.LinkName = target;
        entry.TarHeader.Mode = 0x1FF;
        entry.Size = 0;
        tar.PutNextEntry(entry);
        tar.CloseEntry();
    }

    private static void AddSpecial(TarOutputStream tar, string name, byte typeFlag)
    {
        var entry = TarEntry.CreateTarEntry(name);
        entry.TarHeader.TypeFlag = typeFlag;
        entry.Size = 0;
        tar.PutNextEntry(entry);
        tar.CloseEntry();
    }

    #region Round trips

    [Fact]
    public async Task CreateAndExtract_RoundTripsFilesDirectoriesAndContent()
    {
        var source = NewDirectory("source");
        WriteFile(source, "a.txt", "alpha");
        WriteFile(source, "sub/b.txt", "beta");
        WriteFile(source, "sub/deep/c.txt", "gamma");
        Directory.CreateDirectory(Path.Combine(source, "empty"));
        var target = NewDirectory("target");

        using var tar = await TarArchiveHelper.CreateTarAsync(source);
        var count = await TarArchiveHelper.ExtractTarAsync(tar, target);

        count.Should().Be(3);
        File.ReadAllText(Path.Combine(target, "a.txt")).Should().Be("alpha");
        File.ReadAllText(Path.Combine(target, "sub", "b.txt")).Should().Be("beta");
        File.ReadAllText(Path.Combine(target, "sub", "deep", "c.txt")).Should().Be("gamma");
        Directory.Exists(Path.Combine(target, "empty")).Should().BeTrue();
    }

    [Fact]
    public async Task WriteTarAsync_WritesArchiveAndLeavesDestinationOpen()
    {
        var source = NewDirectory("source");
        WriteFile(source, "a.txt", "alpha");
        using var destination = new MemoryStream();

        await TarArchiveHelper.WriteTarAsync(source, destination);

        destination.CanWrite.Should().BeTrue();
        destination.Length.Should().BeGreaterThan(0);
        destination.Position = 0;
        var target = NewDirectory("target");
        (await TarArchiveHelper.ExtractTarAsync(destination, target)).Should().Be(1);
        File.ReadAllText(Path.Combine(target, "a.txt")).Should().Be("alpha");
    }

    [Fact]
    public async Task CreateTarStream_ProducesTheSameBytesAsCreateTarAsync()
    {
        var source = NewDirectory("source");
        WriteFile(source, "small.txt", "alpha");
        WriteFile(source, "nested/large.bin", new string('x', 300_000));

        using var buffered = await TarArchiveHelper.CreateTarAsync(source);
        using var streamed = new MemoryStream();
        var stream = TarArchiveHelper.CreateTarStream(source);
        await using (stream.ConfigureAwait(false))
        {
            await stream.CopyToAsync(streamed);
        }

        streamed.ToArray().Should().Equal(buffered.ToArray());
    }

    [Fact]
    public async Task CreateTarStream_CanBeExtracted()
    {
        var source = NewDirectory("source");
        WriteFile(source, "sub/file.txt", "content");
        var target = NewDirectory("target");

        var stream = TarArchiveHelper.CreateTarStream(source);
        int count;
        await using (stream.ConfigureAwait(false))
        {
            count = await TarArchiveHelper.ExtractTarAsync(stream, target);
        }

        count.Should().Be(1);
        File.ReadAllText(Path.Combine(target, "sub", "file.txt")).Should().Be("content");
    }

    [Fact]
    public async Task CreateTarStream_DisposedBeforeFullyRead_StopsTheWriterPromptly()
    {
        var source = NewDirectory("source");
        WriteFile(source, "large.bin", new string('y', 8_000_000));

        var stream = TarArchiveHelper.CreateTarStream(source);
        var buffer = new byte[512];
        (await stream.ReadAsync(buffer)).Should().BeGreaterThan(0);

        var stopwatch = Stopwatch.StartNew();
        await stream.DisposeAsync();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(4));
    }

    [Fact]
    public void CreateTarStream_MissingDirectory_ThrowsDirectoryNotFound()
    {
        var act = () => TarArchiveHelper.CreateTarStream(Path.Combine(_root, "missing"));

        act.Should().Throw<DirectoryNotFoundException>();
    }

    [Fact]
    public async Task CreateTarFromFilesAsync_IncludesOnlyListedExistingFiles()
    {
        var source = NewDirectory("source");
        WriteFile(source, "a.txt", "a");
        WriteFile(source, "b.txt", "b");
        WriteFile(source, "sub/c.txt", "c");
        var target = NewDirectory("target");

        using var tar = await TarArchiveHelper.CreateTarFromFilesAsync(source, new[] { "a.txt", "sub/c.txt", "missing.txt" });
        var count = await TarArchiveHelper.ExtractTarAsync(tar, target);

        count.Should().Be(2);
        File.Exists(Path.Combine(target, "a.txt")).Should().BeTrue();
        File.Exists(Path.Combine(target, "sub", "c.txt")).Should().BeTrue();
        File.Exists(Path.Combine(target, "b.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task ExtractTarAsync_OverwritesExistingFiles()
    {
        var target = NewDirectory("target");
        File.WriteAllText(Path.Combine(target, "a.txt"), "old");
        using var tar = BuildTar(t => AddFile(t, "a.txt", "new"));

        await TarArchiveHelper.ExtractTarAsync(tar, target);

        File.ReadAllText(Path.Combine(target, "a.txt")).Should().Be("new");
    }

    [Fact]
    public async Task ExtractTarAsync_RootMarkerEntries_AreIgnored()
    {
        var target = NewDirectory("target");
        using var tar = BuildTar(t =>
        {
            AddDirectory(t, "./");
            AddFile(t, "./x.txt", "1");
        });

        var count = await TarArchiveHelper.ExtractTarAsync(tar, target);

        count.Should().Be(1);
        File.ReadAllText(Path.Combine(target, "x.txt")).Should().Be("1");
    }

    [Fact]
    public async Task ExtractTarAsync_LongEntryNames_AreSupported()
    {
        var target = NewDirectory("target");
        var longName = string.Join("/", Enumerable.Repeat("directory-with-a-long-name", 6)) + "/file.txt";
        longName.Length.Should().BeGreaterThan(100);
        using var tar = BuildTar(t => AddFile(t, longName, "deep"));

        var count = await TarArchiveHelper.ExtractTarAsync(tar, target);

        count.Should().Be(1);
        File.ReadAllText(Path.Combine(target, longName.Replace('/', Path.DirectorySeparatorChar))).Should().Be("deep");
    }

    #endregion

    #region Hardening

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("sub/../../evil.txt")]
    [InlineData("a/b/../../../evil.txt")]
    public async Task ExtractTarAsync_EntryEscapingTarget_ThrowsInvalidDataException(string entryName)
    {
        var target = NewDirectory("target");
        using var tar = BuildTar(t => AddFile(t, entryName, "evil"));

        Func<Task> act = () => TarArchiveHelper.ExtractTarAsync(tar, target);

        await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*escapes the target directory*");
        File.Exists(Path.Combine(_root, "evil.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task ExtractTarAsync_AbsoluteEntryName_IsRootedInsideTarget()
    {
        var target = NewDirectory("target");
        using var tar = BuildTar(t => AddFile(t, "/abs/file.txt", "rooted"));

        var count = await TarArchiveHelper.ExtractTarAsync(tar, target);

        count.Should().Be(1);
        File.ReadAllText(Path.Combine(target, "abs", "file.txt")).Should().Be("rooted");
    }

    [Fact]
    public async Task ExtractTarAsync_SymlinkInsideTarget_IsRecreated()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var target = NewDirectory("target");
        var warnings = new List<string>();
        using var tar = BuildTar(t =>
        {
            AddFile(t, "data/a.txt", "hello");
            AddLink(t, "link.txt", "data/a.txt", TarHeader.LF_SYMLINK);
        });

        var count = await TarArchiveHelper.ExtractTarAsync(tar, target, warnings.Add);

        count.Should().Be(1);
        warnings.Should().BeEmpty();
        var link = new FileInfo(Path.Combine(target, "link.txt"));
        link.LinkTarget.Should().Be("data/a.txt");
        File.ReadAllText(link.FullName).Should().Be("hello");
    }

    [Fact]
    public async Task ExtractTarAsync_SymlinkPointingOutsideTarget_IsSkippedWithWarning()
    {
        var target = NewDirectory("target");
        var warnings = new List<string>();
        using var tar = BuildTar(t => AddLink(t, "escape", "../../outside", TarHeader.LF_SYMLINK));

        var count = await TarArchiveHelper.ExtractTarAsync(tar, target, warnings.Add);

        count.Should().Be(0);
        warnings.Should().ContainSingle(w => w.Contains("outside the extraction directory", StringComparison.Ordinal));
        new FileInfo(Path.Combine(target, "escape")).LinkTarget.Should().BeNull();
        File.Exists(Path.Combine(target, "escape")).Should().BeFalse();
    }

    [Fact]
    public async Task ExtractTarAsync_SymlinkWithAbsoluteTarget_IsSkippedWithWarning()
    {
        var target = NewDirectory("target");
        var warnings = new List<string>();
        using var tar = BuildTar(t => AddLink(t, "passwd", "/etc/passwd", TarHeader.LF_SYMLINK));

        await TarArchiveHelper.ExtractTarAsync(tar, target, warnings.Add);

        warnings.Should().ContainSingle(w => w.Contains("absolute targets", StringComparison.Ordinal));
        File.Exists(Path.Combine(target, "passwd")).Should().BeFalse();
    }

    [Fact]
    public async Task ExtractTarAsync_ThroughPreexistingSymlinkLeavingTarget_ThrowsInvalidDataException()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var outside = NewDirectory("outside");
        var target = NewDirectory("target");
        Directory.CreateSymbolicLink(Path.Combine(target, "sub"), outside);
        using var tar = BuildTar(t => AddFile(t, "sub/pwned.txt", "pwned"));

        Func<Task> act = () => TarArchiveHelper.ExtractTarAsync(tar, target);

        await act.Should().ThrowAsync<InvalidDataException>().WithMessage("*symbolic link*");
        File.Exists(Path.Combine(outside, "pwned.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task ExtractTarAsync_ThroughSymlinkStayingInsideTarget_IsAllowed()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var target = NewDirectory("target");
        Directory.CreateDirectory(Path.Combine(target, "real"));
        Directory.CreateSymbolicLink(Path.Combine(target, "alias"), "real");
        using var tar = BuildTar(t => AddFile(t, "alias/file.txt", "inside"));

        var count = await TarArchiveHelper.ExtractTarAsync(tar, target);

        count.Should().Be(1);
        File.ReadAllText(Path.Combine(target, "real", "file.txt")).Should().Be("inside");
    }

    [Fact]
    public async Task ExtractTarAsync_ExistingSymlinkAtFilePath_IsReplacedWithoutFollowingIt()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var outside = NewDirectory("outside");
        var victim = Path.Combine(outside, "victim.txt");
        File.WriteAllText(victim, "victim");
        var target = NewDirectory("target");
        File.CreateSymbolicLink(Path.Combine(target, "a.txt"), victim);
        using var tar = BuildTar(t => AddFile(t, "a.txt", "payload"));

        await TarArchiveHelper.ExtractTarAsync(tar, target);

        File.ReadAllText(victim).Should().Be("victim");
        var extracted = new FileInfo(Path.Combine(target, "a.txt"));
        extracted.LinkTarget.Should().BeNull();
        File.ReadAllText(extracted.FullName).Should().Be("payload");
    }

    #endregion

    #region Modes, hard links and unsupported entries

    [Fact]
    public async Task ExtractTarAsync_PreservesUnixFileModes()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var target = NewDirectory("target");
        using var tar = BuildTar(t =>
        {
            AddFile(t, "run.sh", "#!/bin/sh\n", Mode755);
            AddFile(t, "secret.txt", "s", Mode600);
        });

        await TarArchiveHelper.ExtractTarAsync(tar, target);

        File.GetUnixFileMode(Path.Combine(target, "run.sh")).Should().HaveFlag(UnixFileMode.UserExecute);
        File.GetUnixFileMode(Path.Combine(target, "secret.txt")).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    [Fact]
    public async Task ExtractTarAsync_AppliesDirectoryModesAfterExtractingTheirContents()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var target = NewDirectory("target");
        var locked = Path.Combine(target, "locked");
        using var tar = BuildTar(t =>
        {
            AddDirectory(t, "locked/", Mode500);
            AddFile(t, "locked/inner.txt", "inner");
        });

        try
        {
            await TarArchiveHelper.ExtractTarAsync(tar, target);

            File.Exists(Path.Combine(locked, "inner.txt")).Should().BeTrue();
            File.GetUnixFileMode(locked).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserExecute);
        }
        finally
        {
            if (Directory.Exists(locked))
            {
                File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }
    }

    [Fact]
    public async Task ExtractTarAsync_HardLink_CopiesTheLinkedFile()
    {
        var target = NewDirectory("target");
        using var tar = BuildTar(t =>
        {
            AddFile(t, "a.txt", "same");
            AddLink(t, "b.txt", "a.txt", TarHeader.LF_LINK);
        });

        var count = await TarArchiveHelper.ExtractTarAsync(tar, target);

        count.Should().Be(2);
        File.ReadAllText(Path.Combine(target, "b.txt")).Should().Be("same");
    }

    [Fact]
    public async Task ExtractTarAsync_HardLinkToMissingTarget_IsSkippedWithWarning()
    {
        var target = NewDirectory("target");
        var warnings = new List<string>();
        using var tar = BuildTar(t => AddLink(t, "b.txt", "missing.txt", TarHeader.LF_LINK));

        var count = await TarArchiveHelper.ExtractTarAsync(tar, target, warnings.Add);

        count.Should().Be(0);
        warnings.Should().ContainSingle(w => w.Contains("hard link", StringComparison.Ordinal));
        File.Exists(Path.Combine(target, "b.txt")).Should().BeFalse();
    }

    [Fact]
    public async Task ExtractTarAsync_SpecialEntryTypes_AreSkippedWithoutCreatingFiles()
    {
        // SharpZipLib drops FIFO/device entries before they reach the extractor; nothing must be created for them.
        var target = NewDirectory("target");
        var warnings = new List<string>();
        using var tar = BuildTar(t =>
        {
            AddSpecial(t, "pipe", TarHeader.LF_FIFO);
            AddSpecial(t, "device", TarHeader.LF_CHR);
            AddFile(t, "regular.txt", "ok");
        });

        var count = await TarArchiveHelper.ExtractTarAsync(tar, target, warnings.Add);

        count.Should().Be(1);
        File.Exists(Path.Combine(target, "pipe")).Should().BeFalse();
        File.Exists(Path.Combine(target, "device")).Should().BeFalse();
        File.ReadAllText(Path.Combine(target, "regular.txt")).Should().Be("ok");
    }

    [Fact]
    public async Task CreateTarAsync_PreservesSymbolicLinks()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var source = NewDirectory("source");
        WriteFile(source, "file.txt", "content");
        WriteFile(source, "sub/inner.txt", "inner");
        File.CreateSymbolicLink(Path.Combine(source, "link.txt"), "file.txt");
        Directory.CreateSymbolicLink(Path.Combine(source, "dirlink"), "sub");
        var target = NewDirectory("target");

        using var tar = await TarArchiveHelper.CreateTarAsync(source);
        var count = await TarArchiveHelper.ExtractTarAsync(tar, target);

        count.Should().Be(2);
        new FileInfo(Path.Combine(target, "link.txt")).LinkTarget.Should().Be("file.txt");
        new DirectoryInfo(Path.Combine(target, "dirlink")).LinkTarget.Should().Be("sub");
        File.ReadAllText(Path.Combine(target, "dirlink", "inner.txt")).Should().Be("inner");
    }

    [Fact]
    public async Task CreateTarAsync_PreservesExecutableBit()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        const UnixFileMode executable = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                        UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                                        UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
        var source = NewDirectory("source");
        WriteFile(source, "tool.sh", "#!/bin/sh\n");
        File.SetUnixFileMode(Path.Combine(source, "tool.sh"), executable);
        var target = NewDirectory("target");

        using var tar = await TarArchiveHelper.CreateTarAsync(source);
        await TarArchiveHelper.ExtractTarAsync(tar, target);

        File.GetUnixFileMode(Path.Combine(target, "tool.sh")).Should().Be(executable);
    }

    #endregion

    #region Argument validation and cancellation

    [Fact]
    public async Task ExtractTarAsync_NullStream_ThrowsArgumentNullException()
    {
        Func<Task> act = () => TarArchiveHelper.ExtractTarAsync(null!, NewDirectory("target"));

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ExtractTarAsync_EmptyTargetDirectory_ThrowsArgumentException()
    {
        using var tar = BuildTar(t => AddFile(t, "a.txt", "a"));

        Func<Task> act = () => TarArchiveHelper.ExtractTarAsync(tar, " ");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ExtractTarAsync_Cancelled_ThrowsOperationCanceledException()
    {
        using var tar = BuildTar(t => AddFile(t, "a.txt", "a"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => TarArchiveHelper.ExtractTarAsync(tar, NewDirectory("target"), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task CreateTarAsync_EmptySourceDirectory_ThrowsArgumentException()
    {
        Func<Task> act = () => TarArchiveHelper.CreateTarAsync(" ");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CreateTarAsync_MissingSourceDirectory_ThrowsDirectoryNotFound()
    {
        Func<Task> act = () => TarArchiveHelper.CreateTarAsync(Path.Combine(_root, "missing"));

        await act.Should().ThrowAsync<DirectoryNotFoundException>();
    }

    #endregion
}
