namespace PDK.Tests.Integration.Runners;

using FluentAssertions;
using Microsoft.Extensions.Logging;
using PDK.Core.Models;
using PDK.Runners;
using PDK.Runners.Docker;
using PDK.Runners.Models;
using PDK.Runners.StepExecutors;

/// <summary>
/// Integration tests for DockerStepExecutor.
/// These tests require Docker to be running and will execute real Docker commands in real containers.
/// Run with: dotnet test --filter "Category=Integration"
/// Skip with: dotnet test --filter "Category!=RequiresDocker"
/// </summary>
public class DockerExecutionTests : IAsyncDisposable
{
    private readonly DockerContainerManager _containerManager;
    private readonly ILogger<DockerExecutionTests> _logger;
    private readonly List<string> _containersToCleanup = new();
    private readonly List<string> _imagesToCleanup = new();

    public DockerExecutionTests()
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug);
        });

        _logger = loggerFactory.CreateLogger<DockerExecutionTests>();
        _containerManager = new DockerContainerManager(loggerFactory.CreateLogger<DockerContainerManager>());
    }

    #region Helper Methods

    /// <summary>
    /// Creates an execution context for Docker testing.
    /// </summary>
    private ExecutionContext CreateDockerContext(string containerId, string workspacePath)
    {
        return new ExecutionContext
        {
            ContainerId = containerId,
            ContainerManager = _containerManager,
            WorkspacePath = workspacePath,
            ContainerWorkspacePath = "/workspace",
            Environment = new Dictionary<string, string>(),
            WorkingDirectory = ".",
            JobInfo = new JobMetadata
            {
                JobName = "docker-integration-test",
                JobId = Guid.NewGuid().ToString(),
                Runner = "docker:latest"
            }
        };
    }

    /// <summary>
    /// Gets the path to the test project.
    /// </summary>
    private string GetTestProjectPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var projectPath = Path.Combine(baseDir, "TestProjects", "DockerSample");

        if (!Directory.Exists(projectPath))
        {
            throw new DirectoryNotFoundException($"Test project not found at: {projectPath}");
        }

        return projectPath;
    }

    #endregion

    #region Build Tests

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task DockerBuild_WithDockerfile_BuildsSuccessfully()
    {
        // Arrange
        var projectPath = GetTestProjectPath();
        var imageName = $"pdk-test-{Guid.NewGuid():N}";
        _imagesToCleanup.Add($"{imageName}:latest");

        // Pull base docker image with Docker CLI
        await _containerManager.PullImageIfNeededAsync("docker:latest");

        var containerId = await _containerManager.CreateContainerAsync(
            "docker:latest",
            new ContainerOptions
            {
                WorkspacePath = projectPath,
                MountDockerSocket = true
            });

        _containersToCleanup.Add(containerId);

        var context = CreateDockerContext(containerId, projectPath);
        var executor = new DockerStepExecutor();

        var step = new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Build image",
            Type = StepType.Docker,
            With = new Dictionary<string, string>
            {
                ["command"] = "build",
                ["Dockerfile"] = "Dockerfile",
                ["tags"] = $"{imageName}:latest",
                ["context"] = "."
            }
        };

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ExitCode.Should().Be(0);
        // Docker buildkit uses "naming to" format, older versions use "Successfully tagged"
        result.Output.Should().Match(output =>
            output.Contains($"Successfully tagged {imageName}:latest") ||
            output.Contains($"naming to docker.io/library/{imageName}:latest"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task DockerBuild_WithMultipleTags_TagsCorrectly()
    {
        // Arrange
        var projectPath = GetTestProjectPath();
        var imageName = $"pdk-test-{Guid.NewGuid():N}";
        _imagesToCleanup.Add($"{imageName}:latest");
        _imagesToCleanup.Add($"{imageName}:v1.0");
        _imagesToCleanup.Add($"{imageName}:prod");

        await _containerManager.PullImageIfNeededAsync("docker:latest");

        var containerId = await _containerManager.CreateContainerAsync(
            "docker:latest",
            new ContainerOptions
            {
                WorkspacePath = projectPath,
                MountDockerSocket = true
            });

        _containersToCleanup.Add(containerId);

        var context = CreateDockerContext(containerId, projectPath);
        var executor = new DockerStepExecutor();

        var step = new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Build with multiple tags",
            Type = StepType.Docker,
            With = new Dictionary<string, string>
            {
                ["command"] = "build",
                ["Dockerfile"] = "Dockerfile",
                ["tags"] = $"{imageName}:latest,{imageName}:v1.0,{imageName}:prod",
                ["context"] = "."
            }
        };

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ExitCode.Should().Be(0);
        // Docker buildkit uses "naming to" format, older versions use "Successfully tagged"
        result.Output.Should().Match(output =>
            output.Contains($"Successfully tagged {imageName}:latest") ||
            output.Contains($"naming to docker.io/library/{imageName}:latest"));
        result.Output.Should().Match(output =>
            output.Contains($"Successfully tagged {imageName}:v1.0") ||
            output.Contains($"naming to docker.io/library/{imageName}:v1.0"));
        result.Output.Should().Match(output =>
            output.Contains($"Successfully tagged {imageName}:prod") ||
            output.Contains($"naming to docker.io/library/{imageName}:prod"));
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task DockerBuild_WithBuildArgs_PassesArguments()
    {
        // Arrange
        var projectPath = GetTestProjectPath();

        // Create a Dockerfile that uses build args
        var dockerfilePath = Path.Combine(projectPath, "Dockerfile.args");
        var dockerfileContent = @"FROM node:18-alpine
ARG VERSION=unknown
ARG BUILD_DATE=unknown
WORKDIR /app
COPY app.js .
RUN echo ""Version: $VERSION, Build Date: $BUILD_DATE""
CMD [""node"", ""app.js""]
";
        File.WriteAllText(dockerfilePath, dockerfileContent);

        var imageName = $"pdk-test-{Guid.NewGuid():N}";
        _imagesToCleanup.Add($"{imageName}:latest");

        try
        {
            await _containerManager.PullImageIfNeededAsync("docker:latest");

            var containerId = await _containerManager.CreateContainerAsync(
                "docker:latest",
                new ContainerOptions
                {
                    WorkspacePath = projectPath,
                    MountDockerSocket = true
                });

            _containersToCleanup.Add(containerId);

            var context = CreateDockerContext(containerId, projectPath);
            var executor = new DockerStepExecutor();

            var step = new Step
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Build with args",
                Type = StepType.Docker,
                With = new Dictionary<string, string>
                {
                    ["command"] = "build",
                    ["Dockerfile"] = "Dockerfile.args",
                    ["tags"] = $"{imageName}:latest",
                    ["buildArgs"] = "VERSION=1.0.0,BUILD_DATE=2024-11-30",
                    ["context"] = "."
                }
            };

            // Act
            var result = await executor.ExecuteAsync(step, context);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.ExitCode.Should().Be(0);
            result.Output.Should().Contain("Version: 1.0.0");
            result.Output.Should().Contain("Build Date: 2024-11-30");
        }
        finally
        {
            if (File.Exists(dockerfilePath))
            {
                File.Delete(dockerfilePath);
            }
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task DockerBuild_InvalidDockerfile_Fails()
    {
        // Arrange
        var projectPath = GetTestProjectPath();

        // Create an invalid Dockerfile
        var dockerfilePath = Path.Combine(projectPath, "Dockerfile.invalid");
        var dockerfileContent = @"FROM nonexistent-image:latest
INVALID_INSTRUCTION
";
        File.WriteAllText(dockerfilePath, dockerfileContent);

        var imageName = $"pdk-test-{Guid.NewGuid():N}";

        try
        {
            await _containerManager.PullImageIfNeededAsync("docker:latest");

            var containerId = await _containerManager.CreateContainerAsync(
                "docker:latest",
                new ContainerOptions
                {
                    WorkspacePath = projectPath,
                    MountDockerSocket = true
                });

            _containersToCleanup.Add(containerId);

            var context = CreateDockerContext(containerId, projectPath);
            var executor = new DockerStepExecutor();

            var step = new Step
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Build invalid",
                Type = StepType.Docker,
                With = new Dictionary<string, string>
                {
                    ["command"] = "build",
                    ["Dockerfile"] = "Dockerfile.invalid",
                    ["tags"] = $"{imageName}:latest",
                    ["context"] = "."
                }
            };

            // Act
            var result = await executor.ExecuteAsync(step, context);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeFalse();
            result.ExitCode.Should().NotBe(0);
        }
        finally
        {
            if (File.Exists(dockerfilePath))
            {
                File.Delete(dockerfilePath);
            }
        }
    }

    #endregion

    #region Tag Tests

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task DockerTag_ExistingImage_TagsSuccessfully()
    {
        // Arrange
        var projectPath = GetTestProjectPath();
        var imageName = $"pdk-test-{Guid.NewGuid():N}";
        var sourceTag = $"{imageName}:source";
        var targetTag = $"{imageName}:target";

        _imagesToCleanup.Add(sourceTag);
        _imagesToCleanup.Add(targetTag);

        await _containerManager.PullImageIfNeededAsync("docker:latest");

        var containerId = await _containerManager.CreateContainerAsync(
            "docker:latest",
            new ContainerOptions
            {
                WorkspacePath = projectPath,
                MountDockerSocket = true
            });

        _containersToCleanup.Add(containerId);

        var context = CreateDockerContext(containerId, projectPath);
        var executor = new DockerStepExecutor();

        // First build an image
        var buildStep = new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Build source image",
            Type = StepType.Docker,
            With = new Dictionary<string, string>
            {
                ["command"] = "build",
                ["Dockerfile"] = "Dockerfile",
                ["tags"] = sourceTag,
                ["context"] = "."
            }
        };
        await executor.ExecuteAsync(buildStep, context);

        // Then tag it
        var tagStep = new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Tag image",
            Type = StepType.Docker,
            With = new Dictionary<string, string>
            {
                ["command"] = "tag",
                ["sourceImage"] = sourceTag,
                ["targetTag"] = targetTag
            }
        };

        // Act
        var result = await executor.ExecuteAsync(tagStep, context);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ExitCode.Should().Be(0);
    }

    #endregion

    #region Run Tests

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task DockerRun_BuiltImage_RunsSuccessfully()
    {
        // Arrange
        var projectPath = GetTestProjectPath();
        var imageName = $"pdk-test-{Guid.NewGuid():N}";
        _imagesToCleanup.Add($"{imageName}:latest");

        await _containerManager.PullImageIfNeededAsync("docker:latest");

        var containerId = await _containerManager.CreateContainerAsync(
            "docker:latest",
            new ContainerOptions
            {
                WorkspacePath = projectPath,
                MountDockerSocket = true
            });

        _containersToCleanup.Add(containerId);

        var context = CreateDockerContext(containerId, projectPath);
        var executor = new DockerStepExecutor();

        // First build an image
        var buildStep = new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Build image",
            Type = StepType.Docker,
            With = new Dictionary<string, string>
            {
                ["command"] = "build",
                ["Dockerfile"] = "Dockerfile",
                ["tags"] = $"{imageName}:latest",
                ["context"] = "."
            }
        };
        await executor.ExecuteAsync(buildStep, context);

        // Then run it
        var runStep = new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Run container",
            Type = StepType.Docker,
            With = new Dictionary<string, string>
            {
                ["command"] = "run",
                ["image"] = $"{imageName}:latest",
                ["arguments"] = "--rm"
            }
        };

        // Act
        var result = await executor.ExecuteAsync(runStep, context);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ExitCode.Should().Be(0);
        result.Output.Should().Contain("Hello from Docker container!");
    }

    #endregion

    #region Tool Validation Tests

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task DockerNotInstalled_ThrowsToolNotFoundException()
    {
        // Arrange - Use Alpine image without Docker CLI
        await _containerManager.PullImageIfNeededAsync("alpine:latest");

        var containerId = await _containerManager.CreateContainerAsync(
            "alpine:latest",
            new ContainerOptions());

        _containersToCleanup.Add(containerId);

        var context = new ExecutionContext
        {
            ContainerId = containerId,
            ContainerManager = _containerManager,
            WorkspacePath = "/workspace",
            ContainerWorkspacePath = "/workspace",
            Environment = new Dictionary<string, string>(),
            WorkingDirectory = ".",
            JobInfo = new JobMetadata
            {
                JobName = "test",
                JobId = Guid.NewGuid().ToString(),
                Runner = "alpine:latest"
            }
        };

        var executor = new DockerStepExecutor();

        var step = new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Build",
            Type = StepType.Docker,
            With = new Dictionary<string, string>
            {
                ["command"] = "build"
            }
        };

        // Act & Assert
        await executor.Invoking(e => e.ExecuteAsync(step, context))
            .Should().ThrowAsync<ToolNotFoundException>()
            .WithMessage("*docker*not found*");
    }

    #endregion

    #region End-to-End Workflow Tests

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task DockerWorkflow_BuildTagRun_SucceedsEndToEnd()
    {
        // Arrange
        var projectPath = GetTestProjectPath();
        var imageName = $"pdk-test-{Guid.NewGuid():N}";
        _imagesToCleanup.Add($"{imageName}:latest");
        _imagesToCleanup.Add($"{imageName}:v1.0");

        await _containerManager.PullImageIfNeededAsync("docker:latest");

        var containerId = await _containerManager.CreateContainerAsync(
            "docker:latest",
            new ContainerOptions
            {
                WorkspacePath = projectPath,
                MountDockerSocket = true
            });

        _containersToCleanup.Add(containerId);

        var context = CreateDockerContext(containerId, projectPath);
        var executor = new DockerStepExecutor();

        // Act - Build
        var buildStep = new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Build",
            Type = StepType.Docker,
            With = new Dictionary<string, string>
            {
                ["command"] = "build",
                ["Dockerfile"] = "Dockerfile",
                ["tags"] = $"{imageName}:latest",
                ["context"] = "."
            }
        };
        var buildResult = await executor.ExecuteAsync(buildStep, context);

        // Act - Tag
        var tagStep = new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Tag",
            Type = StepType.Docker,
            With = new Dictionary<string, string>
            {
                ["command"] = "tag",
                ["sourceImage"] = $"{imageName}:latest",
                ["targetTag"] = $"{imageName}:v1.0"
            }
        };
        var tagResult = await executor.ExecuteAsync(tagStep, context);

        // Act - Run
        var runStep = new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Run",
            Type = StepType.Docker,
            With = new Dictionary<string, string>
            {
                ["command"] = "run",
                ["image"] = $"{imageName}:v1.0",
                ["arguments"] = "--rm"
            }
        };
        var runResult = await executor.ExecuteAsync(runStep, context);

        // Assert
        buildResult.Success.Should().BeTrue();
        tagResult.Success.Should().BeTrue();
        runResult.Success.Should().BeTrue();
        runResult.Output.Should().Contain("Hello from Docker container!");
    }

    #endregion

    #region Cleanup

    public async ValueTask DisposeAsync()
    {
        // Clean up containers first
        foreach (var containerId in _containersToCleanup)
        {
            try
            {
                await _containerManager.RemoveContainerAsync(containerId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to remove container {ContainerId}", containerId);
            }
        }

        // Clean up images
        foreach (var image in _imagesToCleanup)
        {
            try
            {
                var result = await _containerManager.ExecuteCommandAsync(
                    null!,  // No container needed for this
                    $"docker rmi {image} || true",
                    "/",
                    new Dictionary<string, string>(),
                    CancellationToken.None);

                _logger.LogDebug("Cleaned up image {Image}: {Output}", image, result.StandardOutput);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to remove image {Image}", image);
            }
        }

        await _containerManager.DisposeAsync();
    }

    #endregion
}
