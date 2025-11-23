using FluentAssertions;
using PDK.Runners.Docker;

namespace PDK.Tests.Unit.Runners.Docker;

public class ImageMapperTests
{
    private readonly ImageMapper _mapper;

    public ImageMapperTests()
    {
        _mapper = new ImageMapper();
    }

    #region Standard Runner Mappings

    [Fact]
    public void MapRunnerToImage_UbuntuLatest_ReturnsUbuntu2204()
    {
        // Arrange
        var runnerName = "ubuntu-latest";

        // Act
        var result = _mapper.MapRunnerToImage(runnerName);

        // Assert
        result.Should().Be("ubuntu:22.04");
    }

    [Fact]
    public void MapRunnerToImage_Ubuntu2004_ReturnsUbuntu2004()
    {
        // Arrange
        var runnerName = "ubuntu-20.04";

        // Act
        var result = _mapper.MapRunnerToImage(runnerName);

        // Assert
        result.Should().Be("ubuntu:20.04");
    }

    [Fact]
    public void MapRunnerToImage_Ubuntu2204_ReturnsUbuntu2204()
    {
        // Arrange
        var runnerName = "ubuntu-22.04";

        // Act
        var result = _mapper.MapRunnerToImage(runnerName);

        // Assert
        result.Should().Be("ubuntu:22.04");
    }

    [Fact]
    public void MapRunnerToImage_WindowsLatest_ReturnsServerCore2022()
    {
        // Arrange
        var runnerName = "windows-latest";

        // Act
        var result = _mapper.MapRunnerToImage(runnerName);

        // Assert
        result.Should().Be("mcr.microsoft.com/windows/servercore:ltsc2022");
    }

    [Fact]
    public void MapRunnerToImage_Windows2022_ReturnsServerCore2022()
    {
        // Arrange
        var runnerName = "windows-2022";

        // Act
        var result = _mapper.MapRunnerToImage(runnerName);

        // Assert
        result.Should().Be("mcr.microsoft.com/windows/servercore:ltsc2022");
    }

    [Fact]
    public void MapRunnerToImage_Windows2019_ReturnsServerCore2019()
    {
        // Arrange
        var runnerName = "windows-2019";

        // Act
        var result = _mapper.MapRunnerToImage(runnerName);

        // Assert
        result.Should().Be("mcr.microsoft.com/windows/servercore:ltsc2019");
    }

    #endregion

    #region Custom Images

    [Fact]
    public void MapRunnerToImage_CustomImageWithTag_ReturnsUnchanged()
    {
        // Arrange
        var customImage = "node:18";

        // Act
        var result = _mapper.MapRunnerToImage(customImage);

        // Assert
        result.Should().Be("node:18");
    }

    [Fact]
    public void MapRunnerToImage_CustomImageWithRegistry_ReturnsUnchanged()
    {
        // Arrange
        var customImage = "mcr.microsoft.com/dotnet/sdk:8.0";

        // Act
        var result = _mapper.MapRunnerToImage(customImage);

        // Assert
        result.Should().Be("mcr.microsoft.com/dotnet/sdk:8.0");
    }

    [Fact]
    public void MapRunnerToImage_CustomImageWithSlash_ReturnsUnchanged()
    {
        // Arrange
        var customImage = "myregistry/myimage";

        // Act
        var result = _mapper.MapRunnerToImage(customImage);

        // Assert
        result.Should().Be("myregistry/myimage");
    }

    #endregion

    #region Case Insensitivity

    [Fact]
    public void MapRunnerToImage_UppercaseRunner_ReturnsCorrectImage()
    {
        // Arrange
        var runnerName = "UBUNTU-LATEST";

        // Act
        var result = _mapper.MapRunnerToImage(runnerName);

        // Assert
        result.Should().Be("ubuntu:22.04");
    }

    [Fact]
    public void MapRunnerToImage_MixedCaseRunner_ReturnsCorrectImage()
    {
        // Arrange
        var runnerName = "Windows-Latest";

        // Act
        var result = _mapper.MapRunnerToImage(runnerName);

        // Assert
        result.Should().Be("mcr.microsoft.com/windows/servercore:ltsc2022");
    }

    #endregion

    #region Error Cases

    [Fact]
    public void MapRunnerToImage_UnknownRunner_ThrowsArgumentException()
    {
        // Arrange
        var unknownRunner = "unknown-runner";

        // Act
        Action act = () => _mapper.MapRunnerToImage(unknownRunner);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*not recognized*")
            .And.ParamName.Should().Be("runnerName");
    }

    [Fact]
    public void MapRunnerToImage_NullRunner_ThrowsArgumentException()
    {
        // Arrange
        string? nullRunner = null;

        // Act
        Action act = () => _mapper.MapRunnerToImage(nullRunner!);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*cannot be null or empty*")
            .And.ParamName.Should().Be("runnerName");
    }

    [Fact]
    public void MapRunnerToImage_EmptyRunner_ThrowsArgumentException()
    {
        // Arrange
        var emptyRunner = "";

        // Act
        Action act = () => _mapper.MapRunnerToImage(emptyRunner);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*cannot be null or empty*")
            .And.ParamName.Should().Be("runnerName");
    }

    #endregion

    #region IsValidImage Tests

    [Fact]
    public void IsValidImage_SimpleImage_ReturnsTrue()
    {
        // Arrange
        var imageName = "ubuntu";

        // Act
        var result = _mapper.IsValidImage(imageName);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsValidImage_ImageWithTag_ReturnsTrue()
    {
        // Arrange
        var imageName = "ubuntu:22.04";

        // Act
        var result = _mapper.IsValidImage(imageName);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsValidImage_ImageWithRegistry_ReturnsTrue()
    {
        // Arrange
        var imageName = "mcr.microsoft.com/dotnet/sdk:8.0";

        // Act
        var result = _mapper.IsValidImage(imageName);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsValidImage_EmptyImage_ReturnsFalse()
    {
        // Arrange
        var imageName = "";

        // Act
        var result = _mapper.IsValidImage(imageName);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsValidImage_NullImage_ReturnsFalse()
    {
        // Arrange
        string? imageName = null;

        // Act
        var result = _mapper.IsValidImage(imageName!);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsValidImage_WhitespaceImage_ReturnsFalse()
    {
        // Arrange
        var imageName = "   ";

        // Act
        var result = _mapper.IsValidImage(imageName);

        // Assert
        result.Should().BeFalse();
    }

    #endregion
}
