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

namespace PDK.Tests.Performance.Benchmarks;

/// <summary>
/// Benchmarks measuring the impact of performance optimizations.
/// REQ-10-032: Optimization impact measurement.
/// </summary>
[MemoryDiagnoser]
[Config(typeof(ExecutionBenchmarkConfig))]
public class OptimizationBenchmarks
{
    private Job _multiStepJob = null!;
    private string _workspacePath = null!;

    // Runners with different optimization configurations
    private DockerJobRunner _noOptimizationsRunner = null!;
    private DockerJobRunner _imageCachingRunner = null!;
    private DockerJobRunner _containerReuseRunner = null!;
    private DockerJobRunner _allOptimizationsRunner = null!;

    [GlobalSetup]
    public void Setup()
    {
        var parser = new GitHubActionsParser();
        var workflow = parser.Parse(GenerateMultiStepWorkflow());
        _multiStepJob = workflow.Jobs.Values.First();

        _workspacePath = BenchmarkWorkspaceSetup.CreateTempWorkspace();

        // Setup runners with different optimization configurations
        _noOptimizationsRunner = CreateRunnerWithConfig(new PerformanceConfig
        {
            ReuseContainers = false,
            CacheImages = false,
            ParallelSteps = false
        });

        _imageCachingRunner = CreateRunnerWithConfig(new PerformanceConfig
        {
            ReuseContainers = false,
            CacheImages = true,
            ParallelSteps = false
        });

        _containerReuseRunner = CreateRunnerWithConfig(new PerformanceConfig
        {
            ReuseContainers = true,
            CacheImages = false,
            ParallelSteps = false
        });

        _allOptimizationsRunner = CreateRunnerWithConfig(new PerformanceConfig
        {
            ReuseContainers = true,
            CacheImages = true,
            ParallelSteps = false  // Parallel requires specific workflow structure
        });
    }

    private DockerJobRunner CreateRunnerWithConfig(PerformanceConfig config)
    {
        var logger = NullLogger<DockerJobRunner>.Instance;

        // Create mock container manager with configurable delays
        var mockContainerManager = new MockContainerManager
        {
            // Slower container creation to make optimization impact visible
            SimulatedContainerCreationTime = config.ReuseContainers
                ? TimeSpan.FromMilliseconds(10)  // Faster when reusing
                : TimeSpan.FromMilliseconds(100), // Slower without reuse
            SimulatedImagePullTime = config.CacheImages
                ? TimeSpan.FromMilliseconds(5)   // Cache hit
                : TimeSpan.FromMilliseconds(200), // Cache miss (pull)
            SimulatedCommandExecutionTime = TimeSpan.FromMilliseconds(20)
        };

        var mockImageMapper = new Mock<IImageMapper>();
        mockImageMapper.Setup(x => x.MapRunnerToImage(It.IsAny<string>()))
            .Returns("mcr.microsoft.com/dotnet/sdk:8.0");

        var executorFactory = new StepExecutorFactory(CreateMockStepExecutors());
        var performanceTracker = new PerformanceTracker();

        var mockVariableResolver = new Mock<IVariableResolver>();
        mockVariableResolver.Setup(x => x.Resolve(It.IsAny<string>())).Returns((string?)null);
        mockVariableResolver.Setup(x => x.GetAllVariables()).Returns(new Dictionary<string, string>());

        var mockVariableExpander = new Mock<IVariableExpander>();
        mockVariableExpander.Setup(x => x.Expand(It.IsAny<string>(), It.IsAny<IVariableResolver>()))
            .Returns<string, IVariableResolver>((s, _) => s);

        var mockSecretMasker = new Mock<ISecretMasker>();
        mockSecretMasker.Setup(x => x.MaskSecrets(It.IsAny<string>())).Returns<string>(s => s);

        return new DockerJobRunner(
            mockContainerManager,
            mockImageMapper.Object,
            executorFactory,
            logger,
            mockVariableResolver.Object,
            mockVariableExpander.Object,
            mockSecretMasker.Object,
            performanceTracker: performanceTracker,
            performanceConfig: config);
    }

    private static IEnumerable<IStepExecutor> CreateMockStepExecutors()
    {
        var stepTypes = new[] { "script", "checkout", "bash", "dotnet", "npm" };
        foreach (var stepType in stepTypes)
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
                    Duration = TimeSpan.FromMilliseconds(20),
                    StartTime = DateTimeOffset.Now,
                    EndTime = DateTimeOffset.Now.AddMilliseconds(20)
                });
            yield return mock.Object;
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        BenchmarkWorkspaceSetup.CleanupWorkspace(_workspacePath);
    }

    [Benchmark(Baseline = true, Description = "No optimizations")]
    [BenchmarkCategory("Optimization")]
    public async Task<JobExecutionResult> NoOptimizations()
    {
        return await _noOptimizationsRunner.RunJobAsync(_multiStepJob, _workspacePath);
    }

    [Benchmark(Description = "Image caching enabled")]
    [BenchmarkCategory("Optimization", "ImageCache")]
    public async Task<JobExecutionResult> WithImageCaching()
    {
        return await _imageCachingRunner.RunJobAsync(_multiStepJob, _workspacePath);
    }

    [Benchmark(Description = "Container reuse enabled")]
    [BenchmarkCategory("Optimization", "ContainerReuse")]
    public async Task<JobExecutionResult> WithContainerReuse()
    {
        return await _containerReuseRunner.RunJobAsync(_multiStepJob, _workspacePath);
    }

    [Benchmark(Description = "All optimizations enabled")]
    [BenchmarkCategory("Optimization", "All")]
    public async Task<JobExecutionResult> AllOptimizations()
    {
        return await _allOptimizationsRunner.RunJobAsync(_multiStepJob, _workspacePath);
    }

    private static string GenerateMultiStepWorkflow()
    {
        return """
            name: Multi-Step Build
            on: push

            jobs:
              build:
                runs-on: ubuntu-latest
                steps:
                  - name: Checkout code
                    uses: actions/checkout@v4

                  - name: Setup environment
                    run: echo "Setting up environment"

                  - name: Install dependencies
                    run: echo "Installing dependencies"

                  - name: Build project
                    run: echo "Building project"

                  - name: Run tests
                    run: echo "Running tests"

                  - name: Package artifacts
                    run: echo "Packaging artifacts"
            """;
    }
}
