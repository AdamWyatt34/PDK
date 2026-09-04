namespace PDK.Tests.Unit.Artifacts;

using FluentAssertions;
using PDK.Core.Artifacts;
using Xunit;

public class FileSelectorTests : IDisposable
{
    private readonly string _testDir;
    private readonly FileSelector _selector;

    public FileSelectorTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"pdk-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
        _selector = new FileSelector();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, recursive: true);
        }
    }

    #region Basic Pattern Tests

    [Fact]
    public void SelectFiles_SimplePattern_MatchesSingleExtension()
    {
        // Arrange
        CreateTestFile("test.dll");
        CreateTestFile("test.exe");
        CreateTestFile("test.txt");

        // Act
        var result = _selector.SelectFiles(_testDir, new[] { "*.dll" }).ToList();

        // Assert
        result.Should().ContainSingle();
        result[0].Should().EndWith("test.dll");
    }

    [Fact]
    public void SelectFiles_MultipleFiles_MatchesAll()
    {
        // Arrange
        CreateTestFile("one.dll");
        CreateTestFile("two.dll");
        CreateTestFile("three.dll");
        CreateTestFile("other.exe");

        // Act
        var result = _selector.SelectFiles(_testDir, new[] { "*.dll" }).ToList();

        // Assert
        result.Should().HaveCount(3);
        result.Should().AllSatisfy(f => f.Should().EndWith(".dll"));
    }

    [Fact]
    public void SelectFiles_RecursivePattern_MatchesNestedFiles()
    {
        // Arrange
        CreateTestFile("root.log");
        CreateTestFile("sub1/file1.log");
        CreateTestFile("sub1/sub2/file2.log");
        CreateTestFile("sub1/sub2/sub3/file3.log");

        // Act
        var result = _selector.SelectFiles(_testDir, new[] { "**/*.log" }).ToList();

        // Assert
        result.Should().HaveCount(4);
    }

    [Fact]
    public void SelectFiles_DirectoryPattern_MatchesAllInDirectory()
    {
        // Arrange
        CreateTestFile("bin/Release/app.dll");
        CreateTestFile("bin/Release/app.exe");
        CreateTestFile("bin/Release/sub/lib.dll");
        CreateTestFile("obj/Debug/temp.dll");

        // Act
        var result = _selector.SelectFiles(_testDir, new[] { "bin/Release/**/*" }).ToList();

        // Assert
        result.Should().HaveCount(3);
        result.Should().AllSatisfy(f => f.Should().StartWith("bin/Release"));
    }

    #endregion

    #region Exclusion Pattern Tests

    [Fact]
    public void SelectFiles_ExclusionPattern_ExcludesMatching()
    {
        // Arrange
        CreateTestFile("keep.dll");
        CreateTestFile("keep.exe");
        CreateTestFile("skip.tmp");
        CreateTestFile("skip2.tmp");

        // Act
        var result = _selector.SelectFiles(_testDir, new[] { "*.*", "!*.tmp" }).ToList();

        // Assert
        result.Should().HaveCount(2);
        result.Should().NotContain(f => f.EndsWith(".tmp"));
    }

    [Fact]
    public void SelectFiles_ExclusionWithRecursive_ExcludesRecursively()
    {
        // Arrange
        CreateTestFile("src/main.cs");
        CreateTestFile("src/test.cs");
        CreateTestFile("src/obj/temp.cs");
        CreateTestFile("src/bin/output.dll");

        // Act
        var result = _selector.SelectFiles(_testDir, new[] { "**/*.cs", "!**/obj/**" }).ToList();

        // Assert
        result.Should().HaveCount(2);
        result.Should().NotContain(f => f.Contains("obj"));
    }

    [Fact]
    public void SelectFiles_MultipleExclusions_ExcludesAll()
    {
        // Arrange
        CreateTestFile("keep.dll");
        CreateTestFile("skip1.tmp");
        CreateTestFile("skip2.log");
        CreateTestFile("skip3.bak");

        // Act
        var result = _selector.SelectFiles(_testDir, new[] { "*.*", "!*.tmp", "!*.log", "!*.bak" }).ToList();

        // Assert
        result.Should().ContainSingle();
        result[0].Should().EndWith("keep.dll");
    }

    #endregion

    #region Multiple Pattern Tests

    [Fact]
    public void SelectFiles_MultipleIncludePatterns_MatchesAll()
    {
        // Arrange
        CreateTestFile("file.dll");
        CreateTestFile("file.exe");
        CreateTestFile("file.txt");

        // Act
        var result = _selector.SelectFiles(_testDir, new[] { "*.dll", "*.exe" }).ToList();

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(f => f.EndsWith(".dll"));
        result.Should().Contain(f => f.EndsWith(".exe"));
    }

    [Fact]
    public void SelectFiles_CombinedIncludeAndExclude_WorksCorrectly()
    {
        // Arrange
        CreateTestFile("file.dll");
        CreateTestFile("file.exe");
        CreateTestFile("file.test.dll");
        CreateTestFile("other.txt");

        // Act
        var result = _selector.SelectFiles(_testDir, new[] { "*.dll", "*.exe", "!*.test.dll" }).ToList();

        // Assert
        result.Should().HaveCount(2);
        result.Should().Contain(f => f == "file.dll");
        result.Should().Contain(f => f == "file.exe");
        result.Should().NotContain(f => f.Contains(".test.dll"));
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void SelectFiles_NoMatches_ReturnsEmpty()
    {
        // Arrange
        CreateTestFile("file.txt");

        // Act
        var result = _selector.SelectFiles(_testDir, new[] { "*.dll" }).ToList();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void SelectFiles_EmptyDirectory_ReturnsEmpty()
    {
        // Act
        var result = _selector.SelectFiles(_testDir, new[] { "*.*" }).ToList();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void SelectFiles_EmptyPatterns_ReturnsEmpty()
    {
        // Arrange
        CreateTestFile("file.txt");

        // Act
        var result = _selector.SelectFiles(_testDir, Array.Empty<string>()).ToList();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void SelectFiles_NonExistentDirectory_ReturnsEmpty()
    {
        // Act
        var result = _selector.SelectFiles(Path.Combine(_testDir, "nonexistent"), new[] { "*.*" }).ToList();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void SelectFiles_NullBasePath_ThrowsArgumentException()
    {
        // Act & Assert
        var act = () => _selector.SelectFiles(null!, new[] { "*.*" });
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SelectFiles_ReturnsRelativePaths()
    {
        // Arrange
        CreateTestFile("subdir/file.txt");

        // Act
        var result = _selector.SelectFiles(_testDir, new[] { "**/*" }).ToList();

        // Assert
        result.Should().ContainSingle();
        result[0].Should().NotContain(_testDir); // Should be relative
        result[0].Should().Be("subdir/file.txt");
    }

    #endregion

    #region Matches Tests

    [Theory]
    [InlineData("test.dll", "*.dll", true)]
    [InlineData("test.dll", "*.exe", false)]
    [InlineData("src/test.cs", "**/*.cs", true)]
    [InlineData("src/obj/test.cs", "**/obj/**", true)]
    [InlineData("test.txt", "test.*", true)]
    public void Matches_VariousPatterns_ReturnsExpected(string filePath, string pattern, bool expected)
    {
        // Act
        var result = _selector.Matches(filePath, pattern);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void Matches_NullFilePath_ReturnsFalse()
    {
        // Act
        var result = _selector.Matches(null!, "*.dll");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Matches_EmptyPattern_ReturnsFalse()
    {
        // Act
        var result = _selector.Matches("test.dll", "");

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Cross-Platform Path Tests

    [Fact]
    public void SelectFiles_BackslashInPattern_NormalizedToForwardSlash()
    {
        // Arrange
        CreateTestFile("src/main.cs");

        // Act
        var result = _selector.SelectFiles(_testDir, new[] { @"src\*.cs" }).ToList();

        // Assert
        result.Should().ContainSingle();
    }

    [Fact]
    public void SelectFiles_ResultsUseForwardSlash()
    {
        // Arrange
        CreateTestFile("src/sub/file.txt");

        // Act
        var result = _selector.SelectFiles(_testDir, new[] { "**/*.txt" }).ToList();

        // Assert
        result.Should().ContainSingle();
        result[0].Should().NotContain("\\");
        result[0].Should().Contain("/");
    }

    #endregion

    #region Directory And File Path Patterns

    [Theory]
    [InlineData("dist")]
    [InlineData("dist/")]
    [InlineData("./dist")]
    [InlineData("./dist/")]
    public void SelectFiles_BareDirectoryPattern_MatchesWholeTree(string pattern)
    {
        // Arrange
        CreateTestFile("dist/index.html");
        CreateTestFile("dist/js/app.js");
        CreateTestFile("dist/js/vendor/lib.js");
        CreateTestFile("other/file.txt");

        // Act
        var result = _selector.SelectFiles(_testDir, new[] { pattern }).ToList();

        // Assert
        result.Should().BeEquivalentTo(new[] { "dist/index.html", "dist/js/app.js", "dist/js/vendor/lib.js" });
    }

    [Fact]
    public void SelectFiles_PlainFilePath_MatchesThatFile()
    {
        // Arrange
        CreateTestFile("docs/readme.md");
        CreateTestFile("docs/other.md");

        // Act
        var result = _selector.SelectFiles(_testDir, new[] { "docs/readme.md" }).ToList();

        // Assert
        result.Should().ContainSingle().Which.Should().Be("docs/readme.md");
    }

    [Fact]
    public void SelectFiles_DotPattern_MatchesEverything()
    {
        // Arrange
        CreateTestFile("a.txt");
        CreateTestFile("sub/b.txt");

        // Act
        var result = _selector.SelectFiles(_testDir, new[] { "." }).ToList();

        // Assert
        result.Should().BeEquivalentTo(new[] { "a.txt", "sub/b.txt" });
    }

    [Fact]
    public void SelectFiles_BareDirectoryExclusion_ExcludesWholeTree()
    {
        // Arrange
        CreateTestFile("dist/app.js");
        CreateTestFile("dist/vendor/lib.js");
        CreateTestFile("dist/vendor/deep/x.js");

        // Act
        var result = _selector.SelectFiles(_testDir, new[] { "dist", "!dist/vendor" }).ToList();

        // Assert
        result.Should().ContainSingle().Which.Should().Be("dist/app.js");
    }

    [Fact]
    public void SelectFiles_ExclusionsAppliedAfterInclusions_RegardlessOfOrder()
    {
        // Arrange
        CreateTestFile("src/a.cs");
        CreateTestFile("src/gen/b.cs");

        // Act - exclusion listed first
        var result = _selector.SelectFiles(_testDir, new[] { "!src/gen/**", "src/**/*.cs" }).ToList();

        // Assert
        result.Should().ContainSingle().Which.Should().Be("src/a.cs");
    }

    [Fact]
    public void SelectFiles_AbsolutePatternInsideBase_IsRelativized()
    {
        // Arrange
        CreateTestFile("dist/app.js");
        var absolute = Path.Combine(_testDir, "dist", "*.js");

        // Act
        var result = _selector.SelectFiles(_testDir, new[] { absolute }).ToList();

        // Assert
        result.Should().ContainSingle().Which.Should().Be("dist/app.js");
    }

    [Fact]
    public void SelectFiles_AbsolutePatternOutsideBase_IsIgnored()
    {
        // Arrange
        CreateTestFile("dist/app.js");
        var outside = Path.Combine(Path.GetTempPath(), $"pdk-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        try
        {
            File.WriteAllText(Path.Combine(outside, "x.js"), "x");

            // Act
            var result = _selector.SelectFiles(_testDir, new[] { Path.Combine(outside, "*.js") }).ToList();

            // Assert
            result.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    [Fact]
    public void SelectFiles_BlankPatterns_AreIgnored()
    {
        // Arrange
        CreateTestFile("a.txt");

        // Act
        var result = _selector.SelectFiles(_testDir, new[] { "", "   ", "*.txt" }).ToList();

        // Assert
        result.Should().ContainSingle();
    }

    [Fact]
    public void SelectFiles_DuplicateMatches_AreReturnedOnce()
    {
        // Arrange
        CreateTestFile("dist/app.js");

        // Act
        var result = _selector.SelectFiles(_testDir, new[] { "dist", "dist/**/*.js", "**/*.js" }).ToList();

        // Assert
        result.Should().ContainSingle();
    }

    #endregion

    #region Case Sensitivity

    [Fact]
    public void SelectFiles_CaseSensitivity_FollowsPlatform()
    {
        // Arrange
        CreateTestFile("Build/App.DLL");

        // Act
        var result = _selector.SelectFiles(_testDir, new[] { "build/*.dll" }).ToList();

        // Assert
        if (OperatingSystem.IsWindows())
        {
            result.Should().ContainSingle();
        }
        else
        {
            result.Should().BeEmpty("matching is case-sensitive on Linux/macOS");
            _selector.SelectFiles(_testDir, new[] { "Build/*.DLL" }).Should().ContainSingle();
        }
    }

    [Fact]
    public void Matches_CaseSensitivity_FollowsPlatform()
    {
        var expected = OperatingSystem.IsWindows();
        _selector.Matches("Build/App.DLL", "build/*.dll").Should().Be(expected);
    }

    [Theory]
    [InlineData("obj/x.cs", "obj", true)]
    [InlineData("obj/deep/x.cs", "obj/", true)]
    [InlineData("object/x.cs", "obj", false)]
    [InlineData("src/x.cs", "!src", false)]
    [InlineData("docs/x.cs", "!src", true)]
    public void Matches_DirectoryPrefixPattern_MatchesTree(string filePath, string pattern, bool expected)
    {
        _selector.Matches(filePath, pattern).Should().Be(expected);
    }

    #endregion

    #region Helper Methods

    private void CreateTestFile(string relativePath)
    {
        var normalizedPath = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.Combine(_testDir, normalizedPath);
        var directory = Path.GetDirectoryName(fullPath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, $"Test content for {relativePath}");
    }

    #endregion
}
