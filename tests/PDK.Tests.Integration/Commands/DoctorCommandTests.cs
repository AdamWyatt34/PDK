using FluentAssertions;
using PDK.Core.Docker;
using PDK.Runners.Docker;
using PDK.Runners.Models;

namespace PDK.Tests.Integration.Commands;

/// <summary>
/// Integration tests for the doctor command that check Docker availability.
/// These tests require a real Docker daemon to be running.
/// </summary>
public class DoctorCommandTests
{
    #region Docker Availability Tests

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task GetDockerStatus_WithDockerRunning_ReturnsSuccessStatus()
    {
        // Arrange
        var containerManager = new DockerContainerManager();

        try
        {
            // Act
            var status = await containerManager.GetDockerStatusAsync();

            // Assert
            status.IsAvailable.Should().BeTrue("Docker should be available for integration tests");
            status.Version.Should().NotBeNullOrEmpty("Docker version should be retrieved");
            status.Platform.Should().NotBeNullOrEmpty("Docker platform should be retrieved");
            status.ErrorType.Should().BeNull("No error should occur when Docker is running");
            status.ErrorMessage.Should().BeNull("No error message should be present");
        }
        finally
        {
            await containerManager.DisposeAsync();
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task GetDockerVersion_WithDockerRunning_ReturnsVersion()
    {
        // Arrange
        var containerManager = new DockerContainerManager();

        try
        {
            // Act
            var version = await containerManager.GetDockerVersionAsync();

            // Assert
            version.Should().NotBeNullOrEmpty("Docker version should be available");
            version.Should().MatchRegex(@"\d+\.\d+", "Version should contain at least major.minor numbers");
        }
        finally
        {
            await containerManager.DisposeAsync();
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task GetDockerStatus_CompletesWithinOneSecond()
    {
        // Arrange
        var containerManager = new DockerContainerManager();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Act
            var status = await containerManager.GetDockerStatusAsync();
            stopwatch.Stop();

            // Assert (REQ-DK-007: Performance requirement < 1 second)
            stopwatch.ElapsedMilliseconds.Should().BeLessThan(1000,
                "Docker availability check should complete in less than 1 second (REQ-DK-007)");
            status.IsAvailable.Should().BeTrue();
        }
        finally
        {
            await containerManager.DisposeAsync();
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task GetDockerStatus_CalledMultipleTimes_ConsistentResults()
    {
        // Arrange
        var containerManager = new DockerContainerManager();

        try
        {
            // Act
            var status1 = await containerManager.GetDockerStatusAsync();
            var status2 = await containerManager.GetDockerStatusAsync();
            var status3 = await containerManager.GetDockerStatusAsync();

            // Assert
            status1.IsAvailable.Should().Be(status2.IsAvailable);
            status2.IsAvailable.Should().Be(status3.IsAvailable);
            status1.Version.Should().Be(status2.Version);
            status2.Version.Should().Be(status3.Version);
        }
        finally
        {
            await containerManager.DisposeAsync();
        }
    }

    #endregion

    #region Platform Detection Tests

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task GetDockerStatus_ReturnsValidPlatformInfo()
    {
        // Arrange
        var containerManager = new DockerContainerManager();

        try
        {
            // Act
            var status = await containerManager.GetDockerStatusAsync();

            // Assert
            status.Platform.Should().NotBeNullOrEmpty();
            status.Platform.Should().MatchRegex(@"^\w+/\w+$",
                "Platform should be in format 'ostype/architecture' (e.g., 'linux/x86_64')");

            // Platform should be one of the expected values
            var validPlatforms = new[]
            {
                "linux/x86_64", "linux/arm64", "linux/amd64",
                "windows/x86_64", "windows/amd64",
                "darwin/x86_64", "darwin/arm64"
            };

            status.Platform.Should().BeOneOf(validPlatforms,
                "Platform should be a recognized Docker platform");
        }
        finally
        {
            await containerManager.DisposeAsync();
        }
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetDockerStatus_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var containerManager = new DockerContainerManager();
        var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        try
        {
            // Act
            Func<Task> act = async () => await containerManager.GetDockerStatusAsync(cts.Token);

            // Assert
            await act.Should().ThrowAsync<OperationCanceledException>(
                "Cancelled operations should throw OperationCanceledException");
        }
        finally
        {
            await containerManager.DisposeAsync();
        }
    }

    #endregion

    #region Docker Availability Status Model Tests

    [Fact]
    public void DockerAvailabilityStatus_CreateSuccess_SetsPropertiesCorrectly()
    {
        // Act
        var status = DockerAvailabilityStatus.CreateSuccess("24.0.6", "linux/x86_64");

        // Assert
        status.IsAvailable.Should().BeTrue();
        status.Version.Should().Be("24.0.6");
        status.Platform.Should().Be("linux/x86_64");
        status.ErrorType.Should().BeNull();
        status.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void DockerAvailabilityStatus_CreateFailure_SetsPropertiesCorrectly()
    {
        // Act
        var status = DockerAvailabilityStatus.CreateFailure(
            DockerErrorType.NotRunning,
            "Docker daemon is not running");

        // Assert
        status.IsAvailable.Should().BeFalse();
        status.ErrorType.Should().Be(DockerErrorType.NotRunning);
        status.ErrorMessage.Should().Be("Docker daemon is not running");
        status.Version.Should().BeNull();
        status.Platform.Should().BeNull();
    }

    #endregion
}
