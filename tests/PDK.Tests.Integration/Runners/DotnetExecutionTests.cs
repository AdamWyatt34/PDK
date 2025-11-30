namespace PDK.Tests.Integration.Runners;

using FluentAssertions;
using Microsoft.Extensions.Logging;
using PDK.Core.Models;
using PDK.Runners;
using PDK.Runners.Docker;
using PDK.Runners.Models;
using PDK.Runners.StepExecutors;

/// <summary>
/// Integration tests for DotnetStepExecutor.
/// These tests require Docker to be running and will execute real .NET CLI commands in real containers.
/// Run with: dotnet test --filter "Category=Integration"
/// Skip with: dotnet test --filter "Category!=RequiresDocker"
/// </summary>
public class DotnetExecutionTests : IAsyncDisposable
{
    private readonly DockerContainerManager _containerManager;
    private readonly ILogger<DotnetExecutionTests> _logger;
    private readonly List<string> _containersToCleanup = new();

    public DotnetExecutionTests()
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug);
        });

        _logger = loggerFactory.CreateLogger<DotnetExecutionTests>();
        _containerManager = new DockerContainerManager(loggerFactory.CreateLogger<DockerContainerManager>());
    }

    #region Helper Methods

    /// <summary>
    /// Creates an execution context for .NET testing.
    /// </summary>
    private ExecutionContext CreateDotnetContext(string containerId, string workspacePath)
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
                JobName = "dotnet-integration-test",
                JobId = Guid.NewGuid().ToString(),
                Runner = "mcr.microsoft.com/dotnet/sdk:8.0"
            }
        };
    }

    /// <summary>
    /// Gets the path to the test project.
    /// </summary>
    private string GetTestProjectPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var projectPath = Path.Combine(baseDir, "TestProjects", "DotNetSample");

        if (!Directory.Exists(projectPath))
        {
            throw new DirectoryNotFoundException($"Test project not found at: {projectPath}");
        }

        return projectPath;
    }

    #endregion

    #region Restore Tests

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task DotnetRestore_WithCsprojFile_RestoresSuccessfully()
    {
        // Arrange
        var projectPath = GetTestProjectPath();
        await _containerManager.PullImageIfNeededAsync("mcr.microsoft.com/dotnet/sdk:8.0");

        var containerId = await _containerManager.CreateContainerAsync(
            "mcr.microsoft.com/dotnet/sdk:8.0",
            new ContainerOptions
            {
                WorkspacePath = projectPath
            });

        _containersToCleanup.Add(containerId);

        var context = CreateDotnetContext(containerId, projectPath);
        var executor = new DotnetStepExecutor();

        var step = new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Restore packages",
            Type = StepType.Dotnet,
            With = new Dictionary<string, string>
            {
                ["command"] = "restore"
            }
        };

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ExitCode.Should().Be(0);
        // Accept either "Restore succeeded" or "All projects are up-to-date" as both are successful outcomes
        result.Output.Should().Match(output =>
            output.Contains("Restore succeeded") ||
            output.Contains("All projects are up-to-date for restore"));
    }

    #endregion

    #region Build Tests

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task DotnetBuild_WithConfiguration_BuildsSuccessfully()
    {
        // Arrange
        var projectPath = GetTestProjectPath();
        await _containerManager.PullImageIfNeededAsync("mcr.microsoft.com/dotnet/sdk:8.0");

        var containerId = await _containerManager.CreateContainerAsync(
            "mcr.microsoft.com/dotnet/sdk:8.0",
            new ContainerOptions
            {
                WorkspacePath = projectPath
            });

        _containersToCleanup.Add(containerId);

        var context = CreateDotnetContext(containerId, projectPath);
        var executor = new DotnetStepExecutor();

        // First restore
        var restoreStep = new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Restore",
            Type = StepType.Dotnet,
            With = new Dictionary<string, string>
            {
                ["command"] = "restore"
            }
        };
        await executor.ExecuteAsync(restoreStep, context);

        // Then build
        var buildStep = new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Build solution",
            Type = StepType.Dotnet,
            With = new Dictionary<string, string>
            {
                ["command"] = "build",
                ["configuration"] = "Release"
            }
        };

        // Act
        var result = await executor.ExecuteAsync(buildStep, context);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ExitCode.Should().Be(0);
        result.Output.Should().Contain("Build succeeded");
        result.Output.Should().Contain("Release");
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task DotnetBuild_Failure_ReturnsNonZeroExitCode()
    {
        // Arrange - Use a project path that doesn't exist
        var projectPath = GetTestProjectPath();
        await _containerManager.PullImageIfNeededAsync("mcr.microsoft.com/dotnet/sdk:8.0");

        var containerId = await _containerManager.CreateContainerAsync(
            "mcr.microsoft.com/dotnet/sdk:8.0",
            new ContainerOptions
            {
                WorkspacePath = projectPath
            });

        _containersToCleanup.Add(containerId);

        var context = CreateDotnetContext(containerId, projectPath);
        var executor = new DotnetStepExecutor();

        var step = new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Build nonexistent",
            Type = StepType.Dotnet,
            With = new Dictionary<string, string>
            {
                ["command"] = "build",
                ["projects"] = "NonExistent.csproj"
            }
        };

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ExitCode.Should().NotBe(0);
    }

    #endregion

    #region Run Tests

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task DotnetRun_WithConsoleApp_RunsSuccessfully()
    {
        // Arrange
        var projectPath = GetTestProjectPath();
        await _containerManager.PullImageIfNeededAsync("mcr.microsoft.com/dotnet/sdk:8.0");

        var containerId = await _containerManager.CreateContainerAsync(
            "mcr.microsoft.com/dotnet/sdk:8.0",
            new ContainerOptions
            {
                WorkspacePath = projectPath
            });

        _containersToCleanup.Add(containerId);

        var context = CreateDotnetContext(containerId, projectPath);
        var executor = new DotnetStepExecutor();

        // First restore
        var restoreStep = new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Restore",
            Type = StepType.Dotnet,
            With = new Dictionary<string, string>
            {
                ["command"] = "restore"
            }
        };
        await executor.ExecuteAsync(restoreStep, context);

        // Then run
        var runStep = new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Run app",
            Type = StepType.Dotnet,
            With = new Dictionary<string, string>
            {
                ["command"] = "run"
            }
        };

        // Act
        var result = await executor.ExecuteAsync(runStep, context);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ExitCode.Should().Be(0);
        result.Output.Should().Contain("Hello from PDK test project!");
    }

    #endregion

    #region Publish Tests

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task DotnetPublish_WithOutputPath_PublishesSuccessfully()
    {
        // Arrange
        var projectPath = GetTestProjectPath();
        await _containerManager.PullImageIfNeededAsync("mcr.microsoft.com/dotnet/sdk:8.0");

        var containerId = await _containerManager.CreateContainerAsync(
            "mcr.microsoft.com/dotnet/sdk:8.0",
            new ContainerOptions
            {
                WorkspacePath = projectPath
            });

        _containersToCleanup.Add(containerId);

        var context = CreateDotnetContext(containerId, projectPath);
        var executor = new DotnetStepExecutor();

        // First restore
        var restoreStep = new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Restore",
            Type = StepType.Dotnet,
            With = new Dictionary<string, string>
            {
                ["command"] = "restore"
            }
        };
        await executor.ExecuteAsync(restoreStep, context);

        // Then publish
        var publishStep = new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Publish",
            Type = StepType.Dotnet,
            With = new Dictionary<string, string>
            {
                ["command"] = "publish",
                ["configuration"] = "Release",
                ["outputPath"] = "publish"
            }
        };

        // Act
        var result = await executor.ExecuteAsync(publishStep, context);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ExitCode.Should().Be(0);
        result.Output.Should().Contain("publish");
    }

    #endregion

    #region Tool Validation Tests

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task DotnetNotInstalled_ThrowsToolNotFoundException()
    {
        // Arrange - Use Alpine image without .NET SDK
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

        var executor = new DotnetStepExecutor();

        var step = new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Build",
            Type = StepType.Dotnet,
            With = new Dictionary<string, string>
            {
                ["command"] = "build"
            }
        };

        // Act & Assert
        await executor.Invoking(e => e.ExecuteAsync(step, context))
            .Should().ThrowAsync<ToolNotFoundException>()
            .WithMessage("*dotnet*not found*");
    }

    #endregion

    #region End-to-End Workflow Tests

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task DotnetWorkflow_RestoreBuildRun_SucceedsEndToEnd()
    {
        // Arrange
        var projectPath = GetTestProjectPath();
        await _containerManager.PullImageIfNeededAsync("mcr.microsoft.com/dotnet/sdk:8.0");

        var containerId = await _containerManager.CreateContainerAsync(
            "mcr.microsoft.com/dotnet/sdk:8.0",
            new ContainerOptions
            {
                WorkspacePath = projectPath
            });

        _containersToCleanup.Add(containerId);

        var context = CreateDotnetContext(containerId, projectPath);
        var executor = new DotnetStepExecutor();

        // Act - Restore
        var restoreStep = new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Restore",
            Type = StepType.Dotnet,
            With = new Dictionary<string, string>
            {
                ["command"] = "restore"
            }
        };
        var restoreResult = await executor.ExecuteAsync(restoreStep, context);

        // Act - Build
        var buildStep = new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Build",
            Type = StepType.Dotnet,
            With = new Dictionary<string, string>
            {
                ["command"] = "build",
                ["configuration"] = "Release"
            }
        };
        var buildResult = await executor.ExecuteAsync(buildStep, context);

        // Act - Run
        var runStep = new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Run",
            Type = StepType.Dotnet,
            With = new Dictionary<string, string>
            {
                ["command"] = "run",
                ["arguments"] = "--no-build --configuration Release"
            }
        };
        var runResult = await executor.ExecuteAsync(runStep, context);

        // Assert
        restoreResult.Success.Should().BeTrue();
        buildResult.Success.Should().BeTrue();
        runResult.Success.Should().BeTrue();
        runResult.Output.Should().Contain("Hello from PDK test project!");
    }

    #endregion

    #region Cleanup

    public async ValueTask DisposeAsync()
    {
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

        await _containerManager.DisposeAsync();
    }

    #endregion
}
