namespace PDK.Tests.Integration.Runners;

using FluentAssertions;
using Microsoft.Extensions.Logging;
using PDK.Core.Models;
using PDK.Runners;
using PDK.Runners.Docker;
using PDK.Runners.Models;
using PDK.Runners.StepExecutors;

/// <summary>
/// Integration tests for NpmStepExecutor.
/// These tests require Docker to be running and will execute real npm commands in real containers.
/// Run with: dotnet test --filter "Category=Integration"
/// Skip with: dotnet test --filter "Category!=RequiresDocker"
/// </summary>
public class NpmExecutionTests : IAsyncDisposable
{
    private readonly DockerContainerManager _containerManager;
    private readonly ILogger<NpmExecutionTests> _logger;
    private readonly List<string> _containersToCleanup = new();

    public NpmExecutionTests()
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug);
        });

        _logger = loggerFactory.CreateLogger<NpmExecutionTests>();
        _containerManager = new DockerContainerManager(loggerFactory.CreateLogger<DockerContainerManager>());
    }

    #region Helper Methods

    /// <summary>
    /// Creates an execution context for npm testing.
    /// </summary>
    private ExecutionContext CreateNpmContext(string containerId, string workspacePath)
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
                JobName = "npm-integration-test",
                JobId = Guid.NewGuid().ToString(),
                Runner = "node:18"
            }
        };
    }

    /// <summary>
    /// Gets the path to the test project.
    /// </summary>
    private string GetTestProjectPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var projectPath = Path.Combine(baseDir, "TestProjects", "NodeSample");

        if (!Directory.Exists(projectPath))
        {
            throw new DirectoryNotFoundException($"Test project not found at: {projectPath}");
        }

        return projectPath;
    }

    #endregion

    #region Install Tests

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task NpmInstall_WithPackageJson_InstallsSuccessfully()
    {
        // Arrange
        var projectPath = GetTestProjectPath();
        await _containerManager.PullImageIfNeededAsync("node:18");

        var containerId = await _containerManager.CreateContainerAsync(
            "node:18",
            new ContainerOptions
            {
                WorkspacePath = projectPath
            });

        _containersToCleanup.Add(containerId);

        var context = CreateNpmContext(containerId, projectPath);
        var executor = new NpmStepExecutor();

        var step = new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Install dependencies",
            Type = StepType.Npm,
            With = new Dictionary<string, string>
            {
                ["command"] = "install"
            }
        };

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ExitCode.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task NpmInstall_DefaultCommand_UsesInstall()
    {
        // Arrange - Test that npm defaults to "install" when no command specified
        var projectPath = GetTestProjectPath();
        await _containerManager.PullImageIfNeededAsync("node:18");

        var containerId = await _containerManager.CreateContainerAsync(
            "node:18",
            new ContainerOptions
            {
                WorkspacePath = projectPath
            });

        _containersToCleanup.Add(containerId);

        var context = CreateNpmContext(containerId, projectPath);
        var executor = new NpmStepExecutor();

        var step = new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = "npm (default)",
            Type = StepType.Npm,
            With = new Dictionary<string, string>()  // No command specified
        };

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ExitCode.Should().Be(0);
    }

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task NpmInstall_MissingPackageJson_Fails()
    {
        // Arrange - Use a directory without package.json
        var tempPath = Path.Combine(Path.GetTempPath(), $"pdk-npm-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempPath);

        try
        {
            await _containerManager.PullImageIfNeededAsync("node:18");

            var containerId = await _containerManager.CreateContainerAsync(
                "node:18",
                new ContainerOptions
                {
                    WorkspacePath = tempPath
                });

            _containersToCleanup.Add(containerId);

            var context = CreateNpmContext(containerId, tempPath);
            var executor = new NpmStepExecutor();

            var step = new Step
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Install without package.json",
                Type = StepType.Npm,
                With = new Dictionary<string, string>
                {
                    ["command"] = "install"
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
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }
        }
    }

    #endregion

    #region Build Tests

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task NpmRunBuild_WithBuildScript_BuildsSuccessfully()
    {
        // Arrange
        var projectPath = GetTestProjectPath();
        await _containerManager.PullImageIfNeededAsync("node:18");

        var containerId = await _containerManager.CreateContainerAsync(
            "node:18",
            new ContainerOptions
            {
                WorkspacePath = projectPath
            });

        _containersToCleanup.Add(containerId);

        var context = CreateNpmContext(containerId, projectPath);
        var executor = new NpmStepExecutor();

        // First install dependencies (if any)
        var installStep = new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Install",
            Type = StepType.Npm,
            With = new Dictionary<string, string>
            {
                ["command"] = "install"
            }
        };
        await executor.ExecuteAsync(installStep, context);

        // Then build
        var buildStep = new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Build",
            Type = StepType.Npm,
            With = new Dictionary<string, string>
            {
                ["command"] = "build"
            }
        };

        // Act
        var result = await executor.ExecuteAsync(buildStep, context);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ExitCode.Should().Be(0);
        result.Output.Should().Contain("Building");
    }

    #endregion

    #region Test Tests

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task NpmTest_WithTestScript_RunsTests()
    {
        // Arrange
        var projectPath = GetTestProjectPath();
        await _containerManager.PullImageIfNeededAsync("node:18");

        var containerId = await _containerManager.CreateContainerAsync(
            "node:18",
            new ContainerOptions
            {
                WorkspacePath = projectPath
            });

        _containersToCleanup.Add(containerId);

        var context = CreateNpmContext(containerId, projectPath);
        var executor = new NpmStepExecutor();

        // First install dependencies
        var installStep = new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Install",
            Type = StepType.Npm,
            With = new Dictionary<string, string>
            {
                ["command"] = "install"
            }
        };
        await executor.ExecuteAsync(installStep, context);

        // Then test
        var testStep = new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Test",
            Type = StepType.Npm,
            With = new Dictionary<string, string>
            {
                ["command"] = "test"
            }
        };

        // Act
        var result = await executor.ExecuteAsync(testStep, context);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ExitCode.Should().Be(0);
        result.Output.Should().Contain("✓ Test passed");
    }

    #endregion

    #region Custom Script Tests

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task NpmRunCustomScript_ExecutesSuccessfully()
    {
        // Arrange
        var projectPath = GetTestProjectPath();
        await _containerManager.PullImageIfNeededAsync("node:18");

        var containerId = await _containerManager.CreateContainerAsync(
            "node:18",
            new ContainerOptions
            {
                WorkspacePath = projectPath
            });

        _containersToCleanup.Add(containerId);

        var context = CreateNpmContext(containerId, projectPath);
        var executor = new NpmStepExecutor();

        var step = new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Run start script",
            Type = StepType.Npm,
            With = new Dictionary<string, string>
            {
                ["command"] = "run",
                ["script"] = "start"
            }
        };

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ExitCode.Should().Be(0);
        result.Output.Should().Contain("Hello from PDK test project!");
    }

    #endregion

    #region Tool Validation Tests

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task NpmNotInstalled_ThrowsToolNotFoundException()
    {
        // Arrange - Use Alpine image without npm
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

        var executor = new NpmStepExecutor();

        var step = new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Install",
            Type = StepType.Npm,
            With = new Dictionary<string, string>
            {
                ["command"] = "install"
            }
        };

        // Act & Assert
        await executor.Invoking(e => e.ExecuteAsync(step, context))
            .Should().ThrowAsync<ToolNotFoundException>()
            .WithMessage("*npm*not found*");
    }

    #endregion

    #region CI Tests

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task NpmCi_WithPackageLock_InstallsSuccessfully()
    {
        // Arrange
        var projectPath = GetTestProjectPath();

        // Create a package-lock.json for ci command
        var packageLockPath = Path.Combine(projectPath, "package-lock.json");
        var packageLockContent = @"{
  ""name"": ""pdk-node-sample"",
  ""version"": ""1.0.0"",
  ""lockfileVersion"": 3,
  ""requires"": true,
  ""packages"": {
    """": {
      ""name"": ""pdk-node-sample"",
      ""version"": ""1.0.0"",
      ""license"": ""MIT""
    }
  }
}";
        File.WriteAllText(packageLockPath, packageLockContent);

        try
        {
            await _containerManager.PullImageIfNeededAsync("node:18");

            var containerId = await _containerManager.CreateContainerAsync(
                "node:18",
                new ContainerOptions
                {
                    WorkspacePath = projectPath
                });

            _containersToCleanup.Add(containerId);

            var context = CreateNpmContext(containerId, projectPath);
            var executor = new NpmStepExecutor();

            var step = new Step
            {
                Id = Guid.NewGuid().ToString(),
                Name = "Clean install",
                Type = StepType.Npm,
                With = new Dictionary<string, string>
                {
                    ["command"] = "ci"
                }
            };

            // Act
            var result = await executor.ExecuteAsync(step, context);

            // Assert
            result.Should().NotBeNull();
            result.Success.Should().BeTrue();
            result.ExitCode.Should().Be(0);
        }
        finally
        {
            if (File.Exists(packageLockPath))
            {
                File.Delete(packageLockPath);
            }
        }
    }

    #endregion

    #region End-to-End Workflow Tests

    [Fact]
    [Trait("Category", "Integration")]
    [Trait("Category", "RequiresDocker")]
    public async Task NpmWorkflow_InstallBuildTest_SucceedsEndToEnd()
    {
        // Arrange
        var projectPath = GetTestProjectPath();
        await _containerManager.PullImageIfNeededAsync("node:18");

        var containerId = await _containerManager.CreateContainerAsync(
            "node:18",
            new ContainerOptions
            {
                WorkspacePath = projectPath
            });

        _containersToCleanup.Add(containerId);

        var context = CreateNpmContext(containerId, projectPath);
        var executor = new NpmStepExecutor();

        // Act - Install
        var installStep = new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Install",
            Type = StepType.Npm,
            With = new Dictionary<string, string>
            {
                ["command"] = "install"
            }
        };
        var installResult = await executor.ExecuteAsync(installStep, context);

        // Act - Build
        var buildStep = new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Build",
            Type = StepType.Npm,
            With = new Dictionary<string, string>
            {
                ["command"] = "build"
            }
        };
        var buildResult = await executor.ExecuteAsync(buildStep, context);

        // Act - Test
        var testStep = new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Test",
            Type = StepType.Npm,
            With = new Dictionary<string, string>
            {
                ["command"] = "test"
            }
        };
        var testResult = await executor.ExecuteAsync(testStep, context);

        // Assert
        installResult.Success.Should().BeTrue();
        buildResult.Success.Should().BeTrue();
        testResult.Success.Should().BeTrue();
        testResult.Output.Should().Contain("All tests passed!");
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
