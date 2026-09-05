using FluentAssertions;
using PDK.Runners;
using PDK.Runners.Docker;

namespace PDK.Tests.Unit.Runners.Docker;

public class ImageMapperTests
{
    private readonly ImageMapper _mapper = new();

    #region Standard Runner Mappings

    [Theory]
    [InlineData("ubuntu-latest", "buildpack-deps:noble")]
    [InlineData("ubuntu-24.04", "buildpack-deps:noble")]
    [InlineData("ubuntu-24.04-arm", "buildpack-deps:noble")]
    [InlineData("ubuntu-22.04", "buildpack-deps:jammy")]
    [InlineData("ubuntu-22.04-arm", "buildpack-deps:jammy")]
    [InlineData("ubuntu-20.04", "buildpack-deps:focal")]
    [InlineData("UBUNTU-LATEST", "buildpack-deps:noble")]
    [InlineData("  ubuntu-latest ", "buildpack-deps:noble")]
    public void MapRunnerToImage_LinuxRunners(string runnerName, string expectedImage)
    {
        _mapper.MapRunnerToImage(runnerName).Should().Be(expectedImage);
    }

    [Theory]
    [InlineData("windows-latest")]
    [InlineData("windows-2022")]
    [InlineData("windows-2019")]
    [InlineData("Windows-Latest")]
    public void MapRunnerToImage_WindowsRunnerOnLinuxDaemon_ThrowsCapabilityError(string runnerName)
    {
        Action act = () => _mapper.MapRunnerToImage(runnerName);

        act.Should().Throw<ContainerException>()
            .WithMessage("*not supported in Docker mode*--host*");
    }

    [Theory]
    [InlineData("windows-latest", "mcr.microsoft.com/windows/servercore:ltsc2022")]
    [InlineData("windows-2022", "mcr.microsoft.com/windows/servercore:ltsc2022")]
    [InlineData("windows-2019", "mcr.microsoft.com/windows/servercore:ltsc2019")]
    [InlineData("windows-2025", "mcr.microsoft.com/windows/servercore:ltsc2025")]
    public void MapRunnerToImage_WindowsRunnerOnWindowsDaemon_ReturnsServerCore(string runnerName, string expectedImage)
    {
        _mapper.DaemonOSType = "windows";

        _mapper.MapRunnerToImage(runnerName).Should().Be(expectedImage);
    }

    [Fact]
    public void MapRunnerToImage_ExplicitDaemonOsOverload()
    {
        _mapper.MapRunnerToImage("windows-2019", "Windows").Should().Be("mcr.microsoft.com/windows/servercore:ltsc2019");
    }

    [Theory]
    [InlineData("macos-latest")]
    [InlineData("macos-14")]
    [InlineData("windows-11-arm")]
    public void MapRunnerToImage_UnsupportedPlatformRunner_ThrowsCapabilityError(string runnerName)
    {
        Action act = () => _mapper.MapRunnerToImage(runnerName);

        act.Should().Throw<ContainerException>()
            .WithMessage("*not supported in Docker mode*use --host*");
    }

    [Fact]
    public void MapRunnerToImage_MatrixExpression_DefaultsToUbuntuLatest()
    {
        _mapper.MapRunnerToImage("${{ matrix.os }}").Should().Be("buildpack-deps:noble");
    }

    #endregion

    #region Custom Images

    [Theory]
    [InlineData("node:18")]
    [InlineData("mcr.microsoft.com/dotnet/sdk:8.0")]
    [InlineData("myregistry/myimage")]
    [InlineData("localhost:5000/app:1.0")]
    [InlineData("ubuntu@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    public void MapRunnerToImage_CustomImage_ReturnsUnchanged(string customImage)
    {
        _mapper.MapRunnerToImage(customImage).Should().Be(customImage);
    }

    [Fact]
    public void MapRunnerToImage_InvalidCustomImage_ThrowsArgumentException()
    {
        Action act = () => _mapper.MapRunnerToImage("Node:18");

        act.Should().Throw<ArgumentException>().WithMessage("*not valid*");
    }

    #endregion

    #region Error Cases

    [Fact]
    public void MapRunnerToImage_UnknownRunner_ThrowsArgumentException()
    {
        Action act = () => _mapper.MapRunnerToImage("unknown-runner");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*not recognized*")
            .And.ParamName.Should().Be("runnerName");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MapRunnerToImage_NullOrEmptyRunner_ThrowsArgumentException(string? runnerName)
    {
        Action act = () => _mapper.MapRunnerToImage(runnerName!);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*cannot be null or empty*")
            .And.ParamName.Should().Be("runnerName");
    }

    #endregion

    #region IsValidImage Tests

    [Theory]
    [InlineData("ubuntu")]
    [InlineData("ubuntu:22.04")]
    [InlineData("mcr.microsoft.com/dotnet/sdk:8.0")]
    [InlineData("localhost:5000/app:1.0")]
    [InlineData("registry.example.com:8443/team/app")]
    [InlineData("ubuntu@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    [InlineData("ghcr.io/org/app:1.0@sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    public void IsValidImage_ValidReferences_ReturnsTrue(string imageName)
    {
        _mapper.IsValidImage(imageName).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("ubuntu::")]
    [InlineData("ubuntu/Repo")]
    [InlineData("Ubuntu:22.04")]
    [InlineData("ubuntu@sha256:short")]
    public void IsValidImage_InvalidReferences_ReturnsFalse(string? imageName)
    {
        _mapper.IsValidImage(imageName!).Should().BeFalse();
    }

    #endregion
}
