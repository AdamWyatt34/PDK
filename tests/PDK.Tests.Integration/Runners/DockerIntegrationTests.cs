using FluentAssertions;
using PDK.Runners;
using PDK.Runners.Docker;
using PDK.Runners.Models;

namespace PDK.Tests.Integration.Runners;

/// <summary>
/// Integration tests for Docker container management.
/// These tests require Docker to be running on the host machine.
/// Run with: dotnet test --filter "Category=Integration"
/// Skip with: dotnet test --filter "Category!=RequiresDocker"
/// </summary>
public class DockerIntegrationTests
{
    #region Docker Availability Tests

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task IsDockerAvailable_WithDockerRunning_ReturnsTrue()
    {
        // Arrange
        var containerManager = new DockerContainerManager();

        try
        {
            // Act
            var result = await containerManager.IsDockerAvailableAsync();

            // Assert
            result.Should().BeTrue("Docker should be available for integration tests");
        }
        finally
        {
            await containerManager.DisposeAsync();
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task IsDockerAvailable_CanConnectToDockerSocket()
    {
        // Arrange
        var containerManager = new DockerContainerManager();

        try
        {
            // Act
            var result = await containerManager.IsDockerAvailableAsync();

            // Assert
            result.Should().BeTrue();

            // Verify we can actually use Docker by pulling an image
            await containerManager.PullImageIfNeededAsync("alpine:latest");
        }
        finally
        {
            await containerManager.DisposeAsync();
        }
    }

    #endregion

    #region Image Pulling Tests

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task PullImage_StandardImage_PullsSuccessfully()
    {
        // Arrange
        var containerManager = new DockerContainerManager();

        // Use a less common image tag to increase chances of a fresh pull
        const string testImage = "alpine:3.17";

        try
        {
            // Remove the image if it exists to force a fresh pull
            var removeProcess = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = $"rmi {testImage}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            removeProcess.Start();
            await removeProcess.WaitForExitAsync();

            // Act - Pull the image
            var progressMessages = new List<string>();
            var progress = new Progress<string>(msg => progressMessages.Add(msg));
            await containerManager.PullImageIfNeededAsync(testImage, progress);

            // Assert - Should complete without throwing
            // Progress messages are expected when pulling a new image
            progressMessages.Should().NotBeEmpty("progress should be reported during pull");
        }
        finally
        {
            await containerManager.DisposeAsync();
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task PullImage_AlreadyExists_SkipsPull()
    {
        // Arrange
        var containerManager = new DockerContainerManager();

        try
        {
            // Pull once to ensure it exists
            await containerManager.PullImageIfNeededAsync("alpine:latest");

            // Act - Pull again
            var progressMessages = new List<string>();
            var progress = new Progress<string>(msg => progressMessages.Add(msg));
            await containerManager.PullImageIfNeededAsync("alpine:latest", progress);

            // Assert - Should not pull (or minimal progress if it verifies)
            // Either no progress or minimal progress messages
            progressMessages.Should().BeEmpty("image already exists locally");
        }
        finally
        {
            await containerManager.DisposeAsync();
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task PullImage_InvalidImage_ThrowsException()
    {
        // Arrange
        var containerManager = new DockerContainerManager();

        try
        {
            // Act
            Func<Task> act = async () => await containerManager.PullImageIfNeededAsync("nonexistent-image-12345:invalid");

            // Assert
            await act.Should().ThrowAsync<ContainerException>()
                .WithMessage("*not found*");
        }
        finally
        {
            await containerManager.DisposeAsync();
        }
    }

    #endregion

    #region Container Lifecycle Tests

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task CreateContainer_AlpineImage_CreatesSuccessfully()
    {
        // Arrange
        var containerManager = new DockerContainerManager();
        var options = new ContainerOptions
        {
            Name = "test-create-container"
        };

        try
        {
            // Ensure image is available
            await containerManager.PullImageIfNeededAsync("alpine:latest");

            // Act
            var containerId = await containerManager.CreateContainerAsync("alpine:latest", options);

            // Assert
            containerId.Should().NotBeNullOrEmpty();
            containerId.Should().MatchRegex("^[a-f0-9]+$", "container ID should be hexadecimal");
        }
        finally
        {
            await containerManager.DisposeAsync();
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task CreateContainer_WithAllOptions_AppliesSettings()
    {
        // Arrange
        var containerManager = new DockerContainerManager();
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        var options = new ContainerOptions
        {
            Name = "test-options",
            WorkingDirectory = "/workspace",
            WorkspacePath = tempDir,
            Environment = new Dictionary<string, string>
            {
                ["TEST_VAR"] = "test_value",
                ["ANOTHER_VAR"] = "another_value"
            }
        };

        try
        {
            await containerManager.PullImageIfNeededAsync("alpine:latest");

            // Act
            var containerId = await containerManager.CreateContainerAsync("alpine:latest", options);

            // Assert
            containerId.Should().NotBeNullOrEmpty();

            // Verify environment variables
            var result = await containerManager.ExecuteCommandAsync(
                containerId,
                "sh -c 'echo $TEST_VAR'");

            result.StandardOutput.Trim().Should().Contain("test_value");
        }
        finally
        {
            await containerManager.DisposeAsync();
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task RemoveContainer_AfterCreation_RemovesCleanly()
    {
        // Arrange
        var containerManager = new DockerContainerManager();
        var options = new ContainerOptions { Name = "test-remove" };

        try
        {
            await containerManager.PullImageIfNeededAsync("alpine:latest");
            var containerId = await containerManager.CreateContainerAsync("alpine:latest", options);

            // Act - Remove container explicitly
            await containerManager.RemoveContainerAsync(containerId);

            // Assert - Try to execute command (should fail as container is gone)
            Func<Task> act = async () => await containerManager.ExecuteCommandAsync(
                containerId,
                "echo test");

            await act.Should().ThrowAsync<ContainerException>();
        }
        finally
        {
            await containerManager.DisposeAsync();
        }
    }

    #endregion

    #region Command Execution Tests

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task ExecuteCommand_SimpleEcho_ReturnsOutput()
    {
        // Arrange
        var containerManager = new DockerContainerManager();
        var options = new ContainerOptions();

        try
        {
            await containerManager.PullImageIfNeededAsync("alpine:latest");
            var containerId = await containerManager.CreateContainerAsync("alpine:latest", options);

            // Act
            var result = await containerManager.ExecuteCommandAsync(
                containerId,
                "echo 'Hello from Docker'");

            // Assert
            result.Should().NotBeNull();
            result.ExitCode.Should().Be(0);
            result.Success.Should().BeTrue();
            result.StandardOutput.Should().Contain("Hello from Docker");
            result.StandardError.Should().BeEmpty();
            result.Duration.Should().BeGreaterThan(TimeSpan.Zero);
        }
        finally
        {
            await containerManager.DisposeAsync();
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task ExecuteCommand_ExitCodeZero_SuccessIsTrue()
    {
        // Arrange
        var containerManager = new DockerContainerManager();

        try
        {
            await containerManager.PullImageIfNeededAsync("alpine:latest");
            var containerId = await containerManager.CreateContainerAsync(
                "alpine:latest",
                new ContainerOptions());

            // Act
            var result = await containerManager.ExecuteCommandAsync(
                containerId,
                "sh -c 'exit 0'");

            // Assert
            result.ExitCode.Should().Be(0);
            result.Success.Should().BeTrue();
        }
        finally
        {
            await containerManager.DisposeAsync();
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task ExecuteCommand_FailingCommand_ReturnsNonZeroExitCode()
    {
        // Arrange
        var containerManager = new DockerContainerManager();

        try
        {
            await containerManager.PullImageIfNeededAsync("alpine:latest");
            var containerId = await containerManager.CreateContainerAsync(
                "alpine:latest",
                new ContainerOptions());

            // Act
            var result = await containerManager.ExecuteCommandAsync(
                containerId,
                "sh -c 'exit 42'");

            // Assert
            result.ExitCode.Should().Be(42);
            result.Success.Should().BeFalse();
        }
        finally
        {
            await containerManager.DisposeAsync();
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task ExecuteCommand_NonExistentCommand_ReturnsError()
    {
        // Arrange
        var containerManager = new DockerContainerManager();

        try
        {
            await containerManager.PullImageIfNeededAsync("alpine:latest");
            var containerId = await containerManager.CreateContainerAsync(
                "alpine:latest",
                new ContainerOptions());

            // Act
            var result = await containerManager.ExecuteCommandAsync(
                containerId,
                "nonexistent-command-xyz");

            // Assert
            result.ExitCode.Should().NotBe(0);
            result.Success.Should().BeFalse();
            result.StandardError.Should().NotBeEmpty();
        }
        finally
        {
            await containerManager.DisposeAsync();
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task ExecuteCommand_WithEnvironmentVars_VariablesAvailable()
    {
        // Arrange
        var containerManager = new DockerContainerManager();

        try
        {
            await containerManager.PullImageIfNeededAsync("alpine:latest");
            var containerId = await containerManager.CreateContainerAsync(
                "alpine:latest",
                new ContainerOptions());

            var environment = new Dictionary<string, string>
            {
                ["CUSTOM_VAR"] = "custom_value",
                ["NUMBER_VAR"] = "12345"
            };

            // Act
            var result = await containerManager.ExecuteCommandAsync(
                containerId,
                "sh -c 'echo $CUSTOM_VAR-$NUMBER_VAR'",
                environment: environment);

            // Assert
            result.ExitCode.Should().Be(0);
            result.StandardOutput.Trim().Should().Contain("custom_value-12345");
        }
        finally
        {
            await containerManager.DisposeAsync();
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task ExecuteCommand_WithWorkingDir_UsesCorrectDirectory()
    {
        // Arrange
        var containerManager = new DockerContainerManager();

        try
        {
            await containerManager.PullImageIfNeededAsync("alpine:latest");
            var containerId = await containerManager.CreateContainerAsync(
                "alpine:latest",
                new ContainerOptions());

            // Act
            var result = await containerManager.ExecuteCommandAsync(
                containerId,
                "pwd",
                workingDirectory: "/tmp");

            // Assert
            result.ExitCode.Should().Be(0);
            result.StandardOutput.Trim().Should().Be("/tmp");
        }
        finally
        {
            await containerManager.DisposeAsync();
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task ExecuteCommand_CapturesStdout_AndStderr_Separately()
    {
        // Arrange
        var containerManager = new DockerContainerManager();

        try
        {
            await containerManager.PullImageIfNeededAsync("alpine:latest");
            var containerId = await containerManager.CreateContainerAsync(
                "alpine:latest",
                new ContainerOptions());

            // Act - Command that writes to both stdout and stderr
            var result = await containerManager.ExecuteCommandAsync(
                containerId,
                "sh -c 'echo stdout-message && echo stderr-message >&2'");

            // Assert
            result.ExitCode.Should().Be(0);
            result.StandardOutput.Should().Contain("stdout-message");
            result.StandardError.Should().Contain("stderr-message");
        }
        finally
        {
            await containerManager.DisposeAsync();
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task ExecuteCommand_MultipleCommands_AllSucceed()
    {
        // Arrange
        var containerManager = new DockerContainerManager();

        try
        {
            await containerManager.PullImageIfNeededAsync("alpine:latest");
            var containerId = await containerManager.CreateContainerAsync(
                "alpine:latest",
                new ContainerOptions());

            // Act - Execute multiple commands in sequence
            var result1 = await containerManager.ExecuteCommandAsync(
                containerId,
                "echo 'First command'");

            var result2 = await containerManager.ExecuteCommandAsync(
                containerId,
                "echo 'Second command'");

            var result3 = await containerManager.ExecuteCommandAsync(
                containerId,
                "echo 'Third command'");

            // Assert
            result1.Success.Should().BeTrue();
            result1.StandardOutput.Should().Contain("First command");

            result2.Success.Should().BeTrue();
            result2.StandardOutput.Should().Contain("Second command");

            result3.Success.Should().BeTrue();
            result3.StandardOutput.Should().Contain("Third command");
        }
        finally
        {
            await containerManager.DisposeAsync();
        }
    }

    #endregion

    #region Volume Mounting Tests

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task VolumeMount_CreateFileInContainer_AppearsOnHost()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"pdk-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        var containerManager = new DockerContainerManager();
        var options = new ContainerOptions
        {
            WorkspacePath = tempDir,
            WorkingDirectory = "/workspace"
        };

        try
        {
            await containerManager.PullImageIfNeededAsync("alpine:latest");
            var containerId = await containerManager.CreateContainerAsync("alpine:latest", options);

            // Act - Create file in container
            var result = await containerManager.ExecuteCommandAsync(
                containerId,
                "sh -c 'echo \"test content from container\" > /workspace/test.txt'");

            result.ExitCode.Should().Be(0);

            // Assert - File exists on host
            var hostFilePath = Path.Combine(tempDir, "test.txt");
            File.Exists(hostFilePath).Should().BeTrue("file created in container should appear on host");

            var content = await File.ReadAllTextAsync(hostFilePath);
            content.Should().Contain("test content from container");
        }
        finally
        {
            await containerManager.DisposeAsync();
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task VolumeMount_ModifyFileInContainer_ChangesOnHost()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"pdk-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        var hostFilePath = Path.Combine(tempDir, "existing-file.txt");
        await File.WriteAllTextAsync(hostFilePath, "original content");

        var containerManager = new DockerContainerManager();
        var options = new ContainerOptions
        {
            WorkspacePath = tempDir,
            WorkingDirectory = "/workspace"
        };

        try
        {
            await containerManager.PullImageIfNeededAsync("alpine:latest");
            var containerId = await containerManager.CreateContainerAsync("alpine:latest", options);

            // Act - Modify file in container
            var result = await containerManager.ExecuteCommandAsync(
                containerId,
                "sh -c 'echo \"modified content\" >> /workspace/existing-file.txt'");

            result.ExitCode.Should().Be(0);

            // Assert - File modified on host
            var content = await File.ReadAllTextAsync(hostFilePath);
            content.Should().Contain("original content");
            content.Should().Contain("modified content");
        }
        finally
        {
            await containerManager.DisposeAsync();
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task VolumeMount_HostFileExists_VisibleInContainer()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"pdk-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        var hostFilePath = Path.Combine(tempDir, "host-file.txt");
        await File.WriteAllTextAsync(hostFilePath, "content from host");

        var containerManager = new DockerContainerManager();
        var options = new ContainerOptions
        {
            WorkspacePath = tempDir,
            WorkingDirectory = "/workspace"
        };

        try
        {
            await containerManager.PullImageIfNeededAsync("alpine:latest");
            var containerId = await containerManager.CreateContainerAsync("alpine:latest", options);

            // Act - Read file in container
            var result = await containerManager.ExecuteCommandAsync(
                containerId,
                "cat /workspace/host-file.txt");

            // Assert
            result.ExitCode.Should().Be(0);
            result.StandardOutput.Should().Contain("content from host");
        }
        finally
        {
            await containerManager.DisposeAsync();
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    #endregion

    #region Cleanup and Disposal Tests

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task Dispose_RemovesAllContainers()
    {
        // Arrange
        var containerManager = new DockerContainerManager();
        var containerIds = new List<string>();

        try
        {
            await containerManager.PullImageIfNeededAsync("alpine:latest");

            // Create multiple containers
            for (int i = 0; i < 3; i++)
            {
                var containerId = await containerManager.CreateContainerAsync(
                    "alpine:latest",
                    new ContainerOptions { Name = $"test-dispose-{i}" });

                containerIds.Add(containerId);
            }

            containerIds.Should().HaveCount(3);

            // Act - Dispose should clean up all containers
            await containerManager.DisposeAsync();

            // Assert - Try to execute command in first container (should fail)
            var newManager = new DockerContainerManager();
            try
            {
                Func<Task> act = async () => await newManager.ExecuteCommandAsync(
                    containerIds[0],
                    "echo test");

                await act.Should().ThrowAsync<Exception>("container should be removed");
            }
            finally
            {
                await newManager.DisposeAsync();
            }
        }
        catch
        {
            // If test fails, try to clean up manually
            await containerManager.DisposeAsync();
            throw;
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task Exception_DuringExecution_StillCleansUp()
    {
        // Arrange
        var containerManager = new DockerContainerManager();
        string? containerId = null;

        try
        {
            await containerManager.PullImageIfNeededAsync("alpine:latest");
            containerId = await containerManager.CreateContainerAsync(
                "alpine:latest",
                new ContainerOptions());

            // Act - Cause an exception
            try
            {
                // This should cause an error but container should still be cleaned up
                await containerManager.ExecuteCommandAsync(
                    containerId,
                    "sh -c 'exit 1'");
            }
            catch
            {
                // Ignore the error, we're testing cleanup
            }
        }
        finally
        {
            // Assert - Dispose should still work despite errors
            await containerManager.DisposeAsync();
        }

        // Verify container is gone
        if (containerId != null)
        {
            var newManager = new DockerContainerManager();
            try
            {
                Func<Task> act = async () => await newManager.ExecuteCommandAsync(
                    containerId,
                    "echo test");

                await act.Should().ThrowAsync<Exception>();
            }
            finally
            {
                await newManager.DisposeAsync();
            }
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task MultipleContainers_AllRemovedOnDispose()
    {
        // Arrange
        var containerManager = new DockerContainerManager();
        var containerIds = new List<string>();

        try
        {
            await containerManager.PullImageIfNeededAsync("alpine:latest");

            // Create 5 containers
            for (int i = 0; i < 5; i++)
            {
                var containerId = await containerManager.CreateContainerAsync(
                    "alpine:latest",
                    new ContainerOptions());

                containerIds.Add(containerId);
            }

            // Execute commands in each to verify they're working
            foreach (var containerId in containerIds)
            {
                var result = await containerManager.ExecuteCommandAsync(
                    containerId,
                    "echo 'Container is running'");

                result.Success.Should().BeTrue();
            }

            // Act
            await containerManager.DisposeAsync();

            // Assert - All containers should be removed
            var newManager = new DockerContainerManager();
            try
            {
                foreach (var containerId in containerIds)
                {
                    Func<Task> act = async () => await newManager.ExecuteCommandAsync(
                        containerId,
                        "echo test");

                    await act.Should().ThrowAsync<Exception>($"container {containerId} should be removed");
                }
            }
            finally
            {
                await newManager.DisposeAsync();
            }
        }
        catch
        {
            await containerManager.DisposeAsync();
            throw;
        }
    }

    #endregion

    #region End-to-End Workflow Tests

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task CompleteWorkflow_CreateExecuteCleanup_Success()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), $"pdk-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        var containerManager = new DockerContainerManager();
        var options = new ContainerOptions
        {
            Name = "test-workflow",
            WorkspacePath = tempDir,
            WorkingDirectory = "/workspace",
            Environment = new Dictionary<string, string>
            {
                ["PROJECT_NAME"] = "test-project",
                ["BUILD_NUMBER"] = "123"
            }
        };

        try
        {
            // Act - Complete workflow

            // 1. Pull image
            await containerManager.PullImageIfNeededAsync("alpine:latest");

            // 2. Create container
            var containerId = await containerManager.CreateContainerAsync("alpine:latest", options);
            containerId.Should().NotBeNullOrEmpty();

            // 3. Execute setup commands
            var setupResult = await containerManager.ExecuteCommandAsync(
                containerId,
                "sh -c 'echo Setup complete'");
            setupResult.Success.Should().BeTrue();

            // 4. Execute build command (simulated)
            var buildResult = await containerManager.ExecuteCommandAsync(
                containerId,
                "sh -c 'echo Building $PROJECT_NAME build $BUILD_NUMBER'");
            buildResult.Success.Should().BeTrue();
            buildResult.StandardOutput.Should().Contain("Building test-project build 123");

            // 5. Create output file
            var outputResult = await containerManager.ExecuteCommandAsync(
                containerId,
                "sh -c 'echo Build output > /workspace/build-output.txt'");
            outputResult.Success.Should().BeTrue();

            // 6. Verify output file on host
            var outputPath = Path.Combine(tempDir, "build-output.txt");
            File.Exists(outputPath).Should().BeTrue();

            // 7. Execute teardown
            var teardownResult = await containerManager.ExecuteCommandAsync(
                containerId,
                "sh -c 'echo Teardown complete'");
            teardownResult.Success.Should().BeTrue();

            // Assert - All operations succeeded
            setupResult.Success.Should().BeTrue();
            buildResult.Success.Should().BeTrue();
            outputResult.Success.Should().BeTrue();
            teardownResult.Success.Should().BeTrue();
        }
        finally
        {
            // Cleanup
            await containerManager.DisposeAsync();
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    #endregion
}
