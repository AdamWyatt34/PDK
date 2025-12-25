using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PDK.Core.Configuration;
using PDK.Core.Logging;
using PDK.Core.Models;
using PDK.Core.Performance;
using PDK.Core.Variables;
using PDK.Providers.GitHub;
using PDK.Runners;
using PDK.Runners.Docker;
using PDK.Runners.StepExecutors;
using PDK.Tests.Performance.Config;
using PDK.Tests.Performance.Infrastructure;
using ExecutionContext = PDK.Runners.ExecutionContext;
using HostExecutionContext = PDK.Runners.HostExecutionContext;

namespace PDK.Tests.Performance.Benchmarks;

/// <summary>
/// Benchmarks comparing Docker mode vs Host mode execution.
/// REQ-10-031: Execution mode comparison benchmarks.
/// </summary>
[MemoryDiagnoser]
[Config(typeof(ExecutionBenchmarkConfig))]
public class ExecutionModeBenchmarks
{
    private Job _simpleJob = null!;
    private Job _multiStepJob = null!;
    private string _workspacePath = null!;

    // Runners with mocked dependencies
    private DockerJobRunner _dockerRunner = null!;
    private HostJobRunner _hostRunner = null!;

    [GlobalSetup]
    public void Setup()
    {
        // Parse test workflows to get realistic job structures
        var parser = new GitHubActionsParser();

        var simpleWorkflow = parser.Parse(GenerateSimpleWorkflow());
        var dotnetWorkflow = parser.Parse(GenerateDotnetBuildWorkflow());

        _simpleJob = simpleWorkflow.Jobs.Values.First();
        _multiStepJob = dotnetWorkflow.Jobs.Values.First();

        // Create workspace
        _workspacePath = BenchmarkWorkspaceSetup.CreateTempWorkspace();

        // Setup runners with mock dependencies
        SetupDockerRunner();
        SetupHostRunner();
    }

    private void SetupDockerRunner()
    {
        var logger = NullLogger<DockerJobRunner>.Instance;
        var mockContainerManager = new MockContainerManager();

        var mockImageMapper = new Mock<IImageMapper>();
        mockImageMapper.Setup(x => x.MapRunnerToImage(It.IsAny<string>()))
            .Returns("mcr.microsoft.com/dotnet/sdk:8.0");

        // Mock step executor that completes instantly
        var mockStepExecutor = new Mock<IStepExecutor>();
        mockStepExecutor.Setup(x => x.StepType).Returns("script");
        mockStepExecutor.Setup(x => x.ExecuteAsync(
            It.IsAny<Step>(),
            It.IsAny<ExecutionContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new StepExecutionResult
            {
                StepName = "mock-step",
                Success = true,
                ExitCode = 0,
                Duration = TimeSpan.FromMilliseconds(10),
                StartTime = DateTimeOffset.Now,
                EndTime = DateTimeOffset.Now.AddMilliseconds(10)
            });

        // Add executor for all common step types
        var mockCheckoutExecutor = CreateMockStepExecutor("checkout");
        var mockBashExecutor = CreateMockStepExecutor("bash");
        var mockDotnetExecutor = CreateMockStepExecutor("dotnet");
        var mockNpmExecutor = CreateMockStepExecutor("npm");

        var executorFactory = new StepExecutorFactory(new[]
        {
            mockStepExecutor.Object,
            mockCheckoutExecutor.Object,
            mockBashExecutor.Object,
            mockDotnetExecutor.Object,
            mockNpmExecutor.Object
        });

        var mockVariableResolver = new Mock<IVariableResolver>();
        mockVariableResolver.Setup(x => x.Resolve(It.IsAny<string>())).Returns((string?)null);
        mockVariableResolver.Setup(x => x.GetAllVariables()).Returns(new Dictionary<string, string>());

        var mockVariableExpander = new Mock<IVariableExpander>();
        mockVariableExpander.Setup(x => x.Expand(It.IsAny<string>(), It.IsAny<IVariableResolver>()))
            .Returns<string, IVariableResolver>((s, _) => s);

        var mockSecretMasker = new Mock<ISecretMasker>();
        mockSecretMasker.Setup(x => x.MaskSecrets(It.IsAny<string>())).Returns<string>(s => s);

        _dockerRunner = new DockerJobRunner(
            mockContainerManager,
            mockImageMapper.Object,
            executorFactory,
            logger,
            mockVariableResolver.Object,
            mockVariableExpander.Object,
            mockSecretMasker.Object);
    }

    private void SetupHostRunner()
    {
        var logger = NullLogger<HostJobRunner>.Instance;
        var mockProcessExecutor = new MockProcessExecutor();

        // Mock host step executor
        var mockHostExecutor = new Mock<IHostStepExecutor>();
        mockHostExecutor.Setup(x => x.StepType).Returns("script");
        mockHostExecutor.Setup(x => x.ExecuteAsync(
            It.IsAny<Step>(),
            It.IsAny<HostExecutionContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new StepExecutionResult
            {
                StepName = "mock-step",
                Success = true,
                ExitCode = 0,
                Duration = TimeSpan.FromMilliseconds(5),
                StartTime = DateTimeOffset.Now,
                EndTime = DateTimeOffset.Now.AddMilliseconds(5)
            });

        var mockCheckoutExecutor = CreateMockHostStepExecutor("checkout");
        var mockBashExecutor = CreateMockHostStepExecutor("bash");
        var mockDotnetExecutor = CreateMockHostStepExecutor("dotnet");
        var mockNpmExecutor = CreateMockHostStepExecutor("npm");

        var hostExecutorFactory = new HostStepExecutorFactory(new[]
        {
            mockHostExecutor.Object,
            mockCheckoutExecutor.Object,
            mockBashExecutor.Object,
            mockDotnetExecutor.Object,
            mockNpmExecutor.Object
        });

        var mockVariableResolver = new Mock<IVariableResolver>();
        mockVariableResolver.Setup(x => x.Resolve(It.IsAny<string>())).Returns((string?)null);
        mockVariableResolver.Setup(x => x.GetAllVariables()).Returns(new Dictionary<string, string>());

        var mockVariableExpander = new Mock<IVariableExpander>();
        mockVariableExpander.Setup(x => x.Expand(It.IsAny<string>(), It.IsAny<IVariableResolver>()))
            .Returns<string, IVariableResolver>((s, _) => s);

        var mockSecretMasker = new Mock<ISecretMasker>();
        mockSecretMasker.Setup(x => x.MaskSecrets(It.IsAny<string>())).Returns<string>(s => s);

        _hostRunner = new HostJobRunner(
            mockProcessExecutor,
            hostExecutorFactory,
            logger,
            mockVariableResolver.Object,
            mockVariableExpander.Object,
            mockSecretMasker.Object,
            showSecurityWarning: false);
    }

    private static Mock<IStepExecutor> CreateMockStepExecutor(string stepType)
    {
        var mock = new Mock<IStepExecutor>();
        mock.Setup(x => x.StepType).Returns(stepType);
        mock.Setup(x => x.ExecuteAsync(
            It.IsAny<Step>(),
            It.IsAny<ExecutionContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new StepExecutionResult
            {
                StepName = $"mock-{stepType}-step",
                Success = true,
                ExitCode = 0,
                Duration = TimeSpan.FromMilliseconds(10),
                StartTime = DateTimeOffset.Now,
                EndTime = DateTimeOffset.Now.AddMilliseconds(10)
            });
        return mock;
    }

    private static Mock<IHostStepExecutor> CreateMockHostStepExecutor(string stepType)
    {
        var mock = new Mock<IHostStepExecutor>();
        mock.Setup(x => x.StepType).Returns(stepType);
        mock.Setup(x => x.ExecuteAsync(
            It.IsAny<Step>(),
            It.IsAny<HostExecutionContext>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new StepExecutionResult
            {
                StepName = $"mock-{stepType}-step",
                Success = true,
                ExitCode = 0,
                Duration = TimeSpan.FromMilliseconds(5),
                StartTime = DateTimeOffset.Now,
                EndTime = DateTimeOffset.Now.AddMilliseconds(5)
            });
        return mock;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        BenchmarkWorkspaceSetup.CleanupWorkspace(_workspacePath);
    }

    // ============ Docker Mode Benchmarks ============

    [Benchmark(Baseline = true, Description = "Docker: Simple job (1 step)")]
    [BenchmarkCategory("Docker")]
    public async Task<JobExecutionResult> Docker_SimpleJob()
    {
        return await _dockerRunner.RunJobAsync(_simpleJob, _workspacePath);
    }

    [Benchmark(Description = "Docker: Multi-step job (5 steps)")]
    [BenchmarkCategory("Docker")]
    public async Task<JobExecutionResult> Docker_MultiStepJob()
    {
        return await _dockerRunner.RunJobAsync(_multiStepJob, _workspacePath);
    }

    // ============ Host Mode Benchmarks ============

    [Benchmark(Description = "Host: Simple job (1 step)")]
    [BenchmarkCategory("Host")]
    public async Task<JobExecutionResult> Host_SimpleJob()
    {
        return await _hostRunner.RunJobAsync(_simpleJob, _workspacePath);
    }

    [Benchmark(Description = "Host: Multi-step job (5 steps)")]
    [BenchmarkCategory("Host")]
    public async Task<JobExecutionResult> Host_MultiStepJob()
    {
        return await _hostRunner.RunJobAsync(_multiStepJob, _workspacePath);
    }

    #region Workflow Generators

    private static string GenerateSimpleWorkflow()
    {
        return """
            name: Simple Echo
            on: push

            jobs:
              echo:
                runs-on: ubuntu-latest
                steps:
                  - name: Echo message
                    run: echo "Hello, World!"
            """;
    }

    private static string GenerateDotnetBuildWorkflow()
    {
        return """
            name: .NET Build
            on: push

            jobs:
              build:
                runs-on: ubuntu-latest
                steps:
                  - name: Checkout code
                    uses: actions/checkout@v4

                  - name: Setup .NET
                    uses: actions/setup-dotnet@v4
                    with:
                      dotnet-version: 8.0.x

                  - name: Restore dependencies
                    run: dotnet restore

                  - name: Build
                    run: dotnet build --no-restore

                  - name: Run tests
                    run: dotnet test --no-build
            """;
    }

    #endregion
}
