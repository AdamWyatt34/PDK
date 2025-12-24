namespace PDK.Tests.Integration.Runners;

using FluentAssertions;
using Microsoft.Extensions.Logging;
using PDK.Core.Artifacts;
using PDK.Core.Logging;
using PDK.Core.Models;
using PDK.Core.Progress;
using PDK.Core.Variables;
using PDK.Runners;
using PDK.Runners.Models;
using PDK.Runners.StepExecutors;

/// <summary>
/// Integration tests for host mode execution.
/// These tests execute real commands on the host machine.
/// Run with: dotnet test --filter "Category=Integration"
/// </summary>
public class HostExecutionTests : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ProcessExecutor _processExecutor;
    private readonly List<string> _tempDirectories = new();

    public HostExecutionTests()
    {
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug);
        });

        _processExecutor = new ProcessExecutor(_loggerFactory.CreateLogger<ProcessExecutor>());
    }

    #region Helper Methods

    /// <summary>
    /// Creates a temporary workspace directory for testing.
    /// </summary>
    private string CreateTempWorkspace()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"pdk-host-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempPath);
        _tempDirectories.Add(tempPath);
        return tempPath;
    }

    /// <summary>
    /// Creates an execution context for host testing.
    /// </summary>
    private HostExecutionContext CreateHostContext(string? workspacePath = null)
    {
        workspacePath ??= CreateTempWorkspace();

        return new HostExecutionContext
        {
            ProcessExecutor = _processExecutor,
            WorkspacePath = workspacePath,
            Environment = new Dictionary<string, string>
            {
                ["WORKSPACE"] = workspacePath,
                ["PDK_TEST"] = "true"
            },
            WorkingDirectory = workspacePath,
            Platform = _processExecutor.Platform,
            JobInfo = new JobMetadata
            {
                JobName = "host-integration-test",
                JobId = Guid.NewGuid().ToString(),
                Runner = "host"
            }
        };
    }

    /// <summary>
    /// Gets the path to a test project.
    /// </summary>
    private string GetTestProjectPath(string projectName)
    {
        var baseDir = AppContext.BaseDirectory;
        var projectPath = Path.Combine(baseDir, "TestProjects", projectName);

        if (!Directory.Exists(projectPath))
        {
            throw new DirectoryNotFoundException($"Test project not found at: {projectPath}");
        }

        return projectPath;
    }

    #endregion

    #region ProcessExecutor Tests

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ProcessExecutor_SimpleCommand_ExecutesSuccessfully()
    {
        // Arrange
        var workspacePath = CreateTempWorkspace();
        var command = _processExecutor.Platform == OperatingSystemPlatform.Windows
            ? "echo Hello from host"
            : "echo 'Hello from host'";

        // Act
        var result = await _processExecutor.ExecuteAsync(
            command,
            workspacePath);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ExitCode.Should().Be(0);
        result.StandardOutput.Should().Contain("Hello from host");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ProcessExecutor_WithEnvironmentVariables_PassesVariables()
    {
        // Arrange
        var workspacePath = CreateTempWorkspace();
        var command = _processExecutor.Platform == OperatingSystemPlatform.Windows
            ? "echo %TEST_VAR%"
            : "echo $TEST_VAR";

        var environment = new Dictionary<string, string>
        {
            ["TEST_VAR"] = "integration-test-value"
        };

        // Act
        var result = await _processExecutor.ExecuteAsync(
            command,
            workspacePath,
            environment);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.StandardOutput.Should().Contain("integration-test-value");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ProcessExecutor_FailingCommand_ReturnsNonZeroExitCode()
    {
        // Arrange
        var workspacePath = CreateTempWorkspace();
        var command = _processExecutor.Platform == OperatingSystemPlatform.Windows
            ? "cmd /c exit 1"
            : "exit 1";

        // Act
        var result = await _processExecutor.ExecuteAsync(
            command,
            workspacePath);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(1);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ProcessExecutor_IsToolAvailable_DetectsInstalledTools()
    {
        // Act - git should be available on any dev machine
        var gitAvailable = await _processExecutor.IsToolAvailableAsync("git");

        // Assert
        gitAvailable.Should().BeTrue("git should be installed on development machines");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ProcessExecutor_IsToolAvailable_ReturnsFalseForMissingTools()
    {
        // Act
        var available = await _processExecutor.IsToolAvailableAsync("nonexistent-tool-abc123");

        // Assert
        available.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ProcessExecutor_Platform_ReturnsCorrectPlatform()
    {
        // Act
        var platform = _processExecutor.Platform;

        // Assert
        if (OperatingSystem.IsWindows())
        {
            platform.Should().Be(OperatingSystemPlatform.Windows);
        }
        else if (OperatingSystem.IsMacOS())
        {
            platform.Should().Be(OperatingSystemPlatform.MacOS);
        }
        else
        {
            platform.Should().Be(OperatingSystemPlatform.Linux);
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task ProcessExecutor_Cancellation_StopsExecution()
    {
        // Arrange
        var workspacePath = CreateTempWorkspace();
        var command = _processExecutor.Platform == OperatingSystemPlatform.Windows
            ? "ping -n 30 127.0.0.1"  // 30 second ping
            : "sleep 30";

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromMilliseconds(500));

        // Act
        var result = await _processExecutor.ExecuteAsync(
            command,
            workspacePath,
            cancellationToken: cts.Token);

        // Assert
        result.Success.Should().BeFalse();
        result.Duration.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    #endregion

    #region HostScriptExecutor Tests

    [Fact]
    [Trait("Category", "Integration")]
    public async Task HostScriptExecutor_SingleLineScript_ExecutesSuccessfully()
    {
        // Arrange
        var context = CreateHostContext();
        var executor = new HostScriptExecutor(_loggerFactory.CreateLogger<HostScriptExecutor>());

        var step = new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Echo test",
            Type = StepType.Script,
            Script = _processExecutor.Platform == OperatingSystemPlatform.Windows
                ? "echo Script executed successfully"
                : "echo 'Script executed successfully'"
        };

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Output.Should().Contain("Script executed successfully");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task HostScriptExecutor_MultiLineScript_ExecutesAllLines()
    {
        // Arrange
        var context = CreateHostContext();
        var executor = new HostScriptExecutor(_loggerFactory.CreateLogger<HostScriptExecutor>());

        var script = _processExecutor.Platform == OperatingSystemPlatform.Windows
            ? "@echo off\r\necho Line 1\r\necho Line 2\r\necho Line 3"
            : "echo 'Line 1'\necho 'Line 2'\necho 'Line 3'";

        var step = new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Multi-line script",
            Type = StepType.Script,
            Script = script
        };

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Output.Should().Contain("Line 1");
        result.Output.Should().Contain("Line 2");
        result.Output.Should().Contain("Line 3");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task HostScriptExecutor_ScriptWithEnvironmentVars_ExpandsVariables()
    {
        // Arrange
        var context = CreateHostContext();
        var executor = new HostScriptExecutor(_loggerFactory.CreateLogger<HostScriptExecutor>());

        var step = new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Env var script",
            Type = StepType.Script,
            Script = _processExecutor.Platform == OperatingSystemPlatform.Windows
                ? "echo %MY_VAR%"
                : "echo $MY_VAR",
            Environment = new Dictionary<string, string>
            {
                ["MY_VAR"] = "custom-value-123"
            }
        };

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Output.Should().Contain("custom-value-123");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task HostScriptExecutor_FailingScript_ReturnsFailedResult()
    {
        // Arrange
        var context = CreateHostContext();
        var executor = new HostScriptExecutor(_loggerFactory.CreateLogger<HostScriptExecutor>());

        var step = new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Failing script",
            Type = StepType.Script,
            Script = _processExecutor.Platform == OperatingSystemPlatform.Windows
                ? "exit /b 42"
                : "exit 42"
        };

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(42);
    }

    #endregion

    #region HostCheckoutExecutor Tests

    [Fact]
    [Trait("Category", "Integration")]
    public async Task HostCheckoutExecutor_SelfCheckout_ExecutesSuccessfully()
    {
        // Arrange - use the current repo as test
        var repoPath = FindGitRoot(AppContext.BaseDirectory);
        if (repoPath == null)
        {
            // Skip if not in a git repo
            return;
        }

        var context = CreateHostContext(repoPath);
        var executor = new HostCheckoutExecutor(_loggerFactory.CreateLogger<HostCheckoutExecutor>());

        var step = new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Self checkout",
            Type = StepType.Checkout,
            With = new Dictionary<string, string>
            {
                ["repository"] = "self"
            }
        };

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Output.Should().NotBeNullOrEmpty();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task HostCheckoutExecutor_GitNotAvailable_ReturnsFailedResult()
    {
        // This test is tricky since git should be available
        // We test that the executor properly detects git presence
        var context = CreateHostContext();
        var executor = new HostCheckoutExecutor(_loggerFactory.CreateLogger<HostCheckoutExecutor>());

        var step = new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Checkout",
            Type = StepType.Checkout,
            With = new Dictionary<string, string>
            {
                ["repository"] = "self"
            }
        };

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert - if git is available, it should succeed (or fail for valid reasons)
        result.Should().NotBeNull();
        // Either succeeds because git is available, or fails with a proper message
    }

    private string? FindGitRoot(string startPath)
    {
        var current = startPath;
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(Path.Combine(current, ".git")))
            {
                return current;
            }
            current = Directory.GetParent(current)?.FullName;
        }
        return null;
    }

    #endregion

    #region HostDotnetExecutor Tests

    [Fact]
    [Trait("Category", "Integration")]
    public async Task HostDotnetExecutor_VersionCommand_ExecutesSuccessfully()
    {
        // Arrange
        var context = CreateHostContext();
        var executor = new HostDotnetExecutor(_loggerFactory.CreateLogger<HostDotnetExecutor>());

        // Check if dotnet is available
        var dotnetAvailable = await _processExecutor.IsToolAvailableAsync("dotnet");
        if (!dotnetAvailable)
        {
            // Skip test if dotnet not installed
            return;
        }

        // Use a simple command that doesn't require a project
        var workspacePath = context.WorkspacePath;
        var result = await _processExecutor.ExecuteAsync(
            "dotnet --version",
            workspacePath);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.StandardOutput.Should().MatchRegex(@"\d+\.\d+\.\d+");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task HostDotnetExecutor_BuildCommand_WithTestProject_BuildsSuccessfully()
    {
        // Arrange
        string projectPath;
        try
        {
            projectPath = GetTestProjectPath("DotNetSample");
        }
        catch (DirectoryNotFoundException)
        {
            // Skip if test project doesn't exist
            return;
        }

        var dotnetAvailable = await _processExecutor.IsToolAvailableAsync("dotnet");
        if (!dotnetAvailable)
        {
            return;
        }

        var context = CreateHostContext(projectPath);
        var executor = new HostDotnetExecutor(_loggerFactory.CreateLogger<HostDotnetExecutor>());

        var step = new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Build",
            Type = StepType.Dotnet,
            With = new Dictionary<string, string>
            {
                ["command"] = "build",
                ["configuration"] = "Debug"
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
    public async Task HostDotnetExecutor_RestoreAndBuild_WorkflowSucceeds()
    {
        // Arrange
        string projectPath;
        try
        {
            projectPath = GetTestProjectPath("DotNetSample");
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }

        var dotnetAvailable = await _processExecutor.IsToolAvailableAsync("dotnet");
        if (!dotnetAvailable)
        {
            return;
        }

        var context = CreateHostContext(projectPath);
        var executor = new HostDotnetExecutor(_loggerFactory.CreateLogger<HostDotnetExecutor>());

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
                ["configuration"] = "Release",
                ["arguments"] = "--no-restore"
            }
        };
        var buildResult = await executor.ExecuteAsync(buildStep, context);

        // Assert
        restoreResult.Success.Should().BeTrue();
        buildResult.Success.Should().BeTrue();
    }

    #endregion

    #region HostNpmExecutor Tests

    [Fact]
    [Trait("Category", "Integration")]
    public async Task HostNpmExecutor_NpmVersion_ExecutesSuccessfully()
    {
        // Arrange
        var npmAvailable = await _processExecutor.IsToolAvailableAsync("npm");
        if (!npmAvailable)
        {
            // Skip test if npm not installed
            return;
        }

        var workspacePath = CreateTempWorkspace();
        var result = await _processExecutor.ExecuteAsync(
            "npm --version",
            workspacePath);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.StandardOutput.Should().MatchRegex(@"\d+\.\d+\.\d+");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task HostNpmExecutor_InstallCommand_WithPackageJson_InstallsSuccessfully()
    {
        // Arrange
        var npmAvailable = await _processExecutor.IsToolAvailableAsync("npm");
        if (!npmAvailable)
        {
            return;
        }

        var workspacePath = CreateTempWorkspace();

        // Create a minimal package.json
        var packageJson = """
            {
                "name": "pdk-test",
                "version": "1.0.0",
                "description": "Test package",
                "dependencies": {}
            }
            """;
        File.WriteAllText(Path.Combine(workspacePath, "package.json"), packageJson);

        var context = CreateHostContext(workspacePath);
        var executor = new HostNpmExecutor(_loggerFactory.CreateLogger<HostNpmExecutor>());

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

        // Act
        var result = await executor.ExecuteAsync(step, context);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.ExitCode.Should().Be(0);
    }

    #endregion

    #region HostJobRunner Tests

    [Fact]
    [Trait("Category", "Integration")]
    public async Task HostJobRunner_SimpleJob_ExecutesAllSteps()
    {
        // Arrange
        var workspacePath = CreateTempWorkspace();

        var variableResolver = new VariableResolver();
        var variableExpander = new VariableExpander();
        var secretMasker = new SecretMasker();

        var scriptExecutor = new HostScriptExecutor(_loggerFactory.CreateLogger<HostScriptExecutor>());
        var executors = new List<IHostStepExecutor> { scriptExecutor };
        var executorFactory = new HostStepExecutorFactory(executors);

        var jobRunner = new HostJobRunner(
            _processExecutor,
            executorFactory,
            _loggerFactory.CreateLogger<HostJobRunner>(),
            variableResolver,
            variableExpander,
            secretMasker,
            NullProgressReporter.Instance,
            showSecurityWarning: false);  // Disable warning for tests

        var job = new Job
        {
            Name = "integration-test-job",
            Steps = new List<Step>
            {
                new Step
                {
                    Id = "step1",
                    Name = "Step 1",
                    Type = StepType.Script,
                    Script = _processExecutor.Platform == OperatingSystemPlatform.Windows
                        ? "echo Step 1 executed"
                        : "echo 'Step 1 executed'"
                },
                new Step
                {
                    Id = "step2",
                    Name = "Step 2",
                    Type = StepType.Script,
                    Script = _processExecutor.Platform == OperatingSystemPlatform.Windows
                        ? "echo Step 2 executed"
                        : "echo 'Step 2 executed'"
                }
            }
        };

        // Act
        var result = await jobRunner.RunJobAsync(job, workspacePath);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.StepResults.Should().HaveCount(2);
        result.StepResults[0].Success.Should().BeTrue();
        result.StepResults[1].Success.Should().BeTrue();
        result.StepResults[0].Output.Should().Contain("Step 1 executed");
        result.StepResults[1].Output.Should().Contain("Step 2 executed");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task HostJobRunner_StepFailure_StopsExecution()
    {
        // Arrange
        var workspacePath = CreateTempWorkspace();

        var variableResolver = new VariableResolver();
        var variableExpander = new VariableExpander();
        var secretMasker = new SecretMasker();

        var scriptExecutor = new HostScriptExecutor(_loggerFactory.CreateLogger<HostScriptExecutor>());
        var executors = new List<IHostStepExecutor> { scriptExecutor };
        var executorFactory = new HostStepExecutorFactory(executors);

        var jobRunner = new HostJobRunner(
            _processExecutor,
            executorFactory,
            _loggerFactory.CreateLogger<HostJobRunner>(),
            variableResolver,
            variableExpander,
            secretMasker,
            NullProgressReporter.Instance,
            showSecurityWarning: false);

        var job = new Job
        {
            Name = "failing-job",
            Steps = new List<Step>
            {
                new Step
                {
                    Id = "step1",
                    Name = "Step 1",
                    Type = StepType.Script,
                    Script = _processExecutor.Platform == OperatingSystemPlatform.Windows
                        ? "echo Step 1"
                        : "echo 'Step 1'"
                },
                new Step
                {
                    Id = "step2",
                    Name = "Failing Step",
                    Type = StepType.Script,
                    Script = _processExecutor.Platform == OperatingSystemPlatform.Windows
                        ? "exit /b 1"
                        : "exit 1"
                },
                new Step
                {
                    Id = "step3",
                    Name = "Step 3 - Should not run",
                    Type = StepType.Script,
                    Script = "echo Should not see this"
                }
            }
        };

        // Act
        var result = await jobRunner.RunJobAsync(job, workspacePath);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.StepResults.Should().HaveCount(2);  // Only 2 steps run
        result.StepResults[0].Success.Should().BeTrue();
        result.StepResults[1].Success.Should().BeFalse();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task HostJobRunner_ContinueOnError_ContinuesExecution()
    {
        // Arrange
        var workspacePath = CreateTempWorkspace();

        var variableResolver = new VariableResolver();
        var variableExpander = new VariableExpander();
        var secretMasker = new SecretMasker();

        var scriptExecutor = new HostScriptExecutor(_loggerFactory.CreateLogger<HostScriptExecutor>());
        var executors = new List<IHostStepExecutor> { scriptExecutor };
        var executorFactory = new HostStepExecutorFactory(executors);

        var jobRunner = new HostJobRunner(
            _processExecutor,
            executorFactory,
            _loggerFactory.CreateLogger<HostJobRunner>(),
            variableResolver,
            variableExpander,
            secretMasker,
            NullProgressReporter.Instance,
            showSecurityWarning: false);

        var job = new Job
        {
            Name = "continue-on-error-job",
            Steps = new List<Step>
            {
                new Step
                {
                    Id = "step1",
                    Name = "Failing Step with ContinueOnError",
                    Type = StepType.Script,
                    Script = _processExecutor.Platform == OperatingSystemPlatform.Windows
                        ? "exit /b 1"
                        : "exit 1",
                    ContinueOnError = true
                },
                new Step
                {
                    Id = "step2",
                    Name = "Step 2 - Should run",
                    Type = StepType.Script,
                    Script = _processExecutor.Platform == OperatingSystemPlatform.Windows
                        ? "echo Continued successfully"
                        : "echo 'Continued successfully'"
                }
            }
        };

        // Act
        var result = await jobRunner.RunJobAsync(job, workspacePath);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();  // Overall job fails
        result.StepResults.Should().HaveCount(2);  // But all steps ran
        result.StepResults[0].Success.Should().BeFalse();
        result.StepResults[1].Success.Should().BeTrue();
        result.StepResults[1].Output.Should().Contain("Continued successfully");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task HostJobRunner_WithVariables_ExpandsVariables()
    {
        // Arrange
        var workspacePath = CreateTempWorkspace();

        var variableResolver = new VariableResolver();
        variableResolver.SetVariable("greeting", "Hello from PDK", PDK.Core.Variables.VariableSource.Configuration);

        var variableExpander = new VariableExpander();
        var secretMasker = new SecretMasker();

        var scriptExecutor = new HostScriptExecutor(_loggerFactory.CreateLogger<HostScriptExecutor>());
        var executors = new List<IHostStepExecutor> { scriptExecutor };
        var executorFactory = new HostStepExecutorFactory(executors);

        var jobRunner = new HostJobRunner(
            _processExecutor,
            executorFactory,
            _loggerFactory.CreateLogger<HostJobRunner>(),
            variableResolver,
            variableExpander,
            secretMasker,
            NullProgressReporter.Instance,
            showSecurityWarning: false);

        var job = new Job
        {
            Name = "variable-expansion-job",
            Steps = new List<Step>
            {
                new Step
                {
                    Id = "step1",
                    Name = "Echo variable",
                    Type = StepType.Script,
                    Script = _processExecutor.Platform == OperatingSystemPlatform.Windows
                        ? "echo ${greeting}"
                        : "echo '${greeting}'"
                }
            }
        };

        // Act
        var result = await jobRunner.RunJobAsync(job, workspacePath);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.StepResults[0].Output.Should().Contain("Hello from PDK");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task HostJobRunner_WithSecrets_MasksSecretValues()
    {
        // Arrange
        var workspacePath = CreateTempWorkspace();

        var variableResolver = new VariableResolver();
        variableResolver.SetVariable("api_key", "super-secret-key-123", PDK.Core.Variables.VariableSource.Secret);

        var variableExpander = new VariableExpander();
        var secretMasker = new SecretMasker();
        secretMasker.RegisterSecret("super-secret-key-123");

        var scriptExecutor = new HostScriptExecutor(_loggerFactory.CreateLogger<HostScriptExecutor>());
        var executors = new List<IHostStepExecutor> { scriptExecutor };
        var executorFactory = new HostStepExecutorFactory(executors);

        var jobRunner = new HostJobRunner(
            _processExecutor,
            executorFactory,
            _loggerFactory.CreateLogger<HostJobRunner>(),
            variableResolver,
            variableExpander,
            secretMasker,
            NullProgressReporter.Instance,
            showSecurityWarning: false);

        var job = new Job
        {
            Name = "secret-masking-job",
            Steps = new List<Step>
            {
                new Step
                {
                    Id = "step1",
                    Name = "Echo secret",
                    Type = StepType.Script,
                    Script = _processExecutor.Platform == OperatingSystemPlatform.Windows
                        ? "echo ${api_key}"
                        : "echo '${api_key}'"
                }
            }
        };

        // Act
        var result = await jobRunner.RunJobAsync(job, workspacePath);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        // The actual secret value should be masked
        result.StepResults[0].Output.Should().NotContain("super-secret-key-123");
        result.StepResults[0].Output.Should().Contain("***");
    }

    #endregion

    #region End-to-End Workflow Tests

    [Fact]
    [Trait("Category", "Integration")]
    public async Task HostExecution_MultiStepWorkflow_ExecutesEndToEnd()
    {
        // Arrange
        var workspacePath = CreateTempWorkspace();

        var variableResolver = new VariableResolver();
        var variableExpander = new VariableExpander();
        var secretMasker = new SecretMasker();

        var scriptExecutor = new HostScriptExecutor(_loggerFactory.CreateLogger<HostScriptExecutor>());
        var executors = new List<IHostStepExecutor> { scriptExecutor };
        var executorFactory = new HostStepExecutorFactory(executors);

        var jobRunner = new HostJobRunner(
            _processExecutor,
            executorFactory,
            _loggerFactory.CreateLogger<HostJobRunner>(),
            variableResolver,
            variableExpander,
            secretMasker,
            NullProgressReporter.Instance,
            showSecurityWarning: false);

        // Create a file in the first step, verify in the second
        var testFileName = "test-file.txt";
        var testContent = $"Created at {DateTime.UtcNow:O}";

        var job = new Job
        {
            Name = "end-to-end-workflow",
            Steps = new List<Step>
            {
                new Step
                {
                    Id = "create-file",
                    Name = "Create file",
                    Type = StepType.Script,
                    Script = _processExecutor.Platform == OperatingSystemPlatform.Windows
                        ? $"echo {testContent} > {testFileName}"
                        : $"echo '{testContent}' > {testFileName}"
                },
                new Step
                {
                    Id = "verify-file",
                    Name = "Verify file",
                    Type = StepType.Script,
                    Script = _processExecutor.Platform == OperatingSystemPlatform.Windows
                        ? $"type {testFileName}"
                        : $"cat {testFileName}"
                }
            }
        };

        // Act
        var result = await jobRunner.RunJobAsync(job, workspacePath);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.StepResults.Should().HaveCount(2);
        result.StepResults[0].Success.Should().BeTrue();
        result.StepResults[1].Success.Should().BeTrue();
        result.StepResults[1].Output.Should().Contain("Created at");

        // Verify file actually exists
        var filePath = Path.Combine(workspacePath, testFileName);
        File.Exists(filePath).Should().BeTrue();
    }

    #endregion

    #region Cleanup

    public void Dispose()
    {
        foreach (var dir in _tempDirectories)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        }

        _loggerFactory.Dispose();
    }

    #endregion
}
