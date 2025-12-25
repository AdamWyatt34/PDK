using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PDK.Core.Configuration;
using PDK.Core.Logging;
using PDK.Core.Models;
using PDK.Core.Performance;
using PDK.Core.Variables;
using PDK.Providers.GitHub;
using PDK.Runners;
using PDK.Runners.StepExecutors;
using PDK.Tests.Performance.Config;
using PDK.Tests.Performance.Infrastructure;
using ExecutionContext = PDK.Runners.ExecutionContext;
using HostExecutionContext = PDK.Runners.HostExecutionContext;

namespace PDK.Tests.Performance.Benchmarks;

/// <summary>
/// Benchmarks using realistic workflow scenarios.
/// REQ-10-033: Real-world workflow benchmarks.
/// </summary>
[MemoryDiagnoser]
[Config(typeof(ExecutionBenchmarkConfig))]
public class RealWorldBenchmarks
{
    private Pipeline _dotnetBuildPipeline = null!;
    private Pipeline _npmBuildPipeline = null!;
    private Pipeline _multiJobPipeline = null!;
    private string _workspacePath = null!;
    private DockerJobRunner _dockerRunner = null!;
    private HostJobRunner _hostRunner = null!;

    [GlobalSetup]
    public void Setup()
    {
        var parser = new GitHubActionsParser();
        var workflowsPath = Path.Combine(AppContext.BaseDirectory, "Workflows");

        _dotnetBuildPipeline = LoadOrGeneratePipeline(parser, workflowsPath, "dotnet-build.yml",
            GenerateDotnetBuildWorkflow());
        _npmBuildPipeline = LoadOrGeneratePipeline(parser, workflowsPath, "npm-build.yml",
            GenerateNpmBuildWorkflow());
        _multiJobPipeline = LoadOrGeneratePipeline(parser, workflowsPath, "multi-job.yml",
            GenerateMultiJobWorkflow());

        _workspacePath = BenchmarkWorkspaceSetup.CreateTempWorkspace();

        SetupRunners();
    }

    private static Pipeline LoadOrGeneratePipeline(GitHubActionsParser parser, string basePath,
        string fileName, string defaultContent)
    {
        var filePath = Path.Combine(basePath, fileName);
        var content = File.Exists(filePath) ? File.ReadAllText(filePath) : defaultContent;
        return parser.Parse(content);
    }

    private void SetupRunners()
    {
        // Docker runner setup
        var dockerLogger = NullLogger<DockerJobRunner>.Instance;
        var mockContainerManager = new MockContainerManager
        {
            SimulatedContainerCreationTime = TimeSpan.FromMilliseconds(50),
            SimulatedCommandExecutionTime = TimeSpan.FromMilliseconds(30),
            SimulatedImagePullTime = TimeSpan.FromMilliseconds(10)
        };

        var mockImageMapper = new Mock<IImageMapper>();
        mockImageMapper.Setup(x => x.MapRunnerToImage(It.IsAny<string>()))
            .Returns("ubuntu:22.04");

        var dockerExecutorFactory = new StepExecutorFactory(CreateMockStepExecutors());

        var mockVariableResolver = new Mock<IVariableResolver>();
        mockVariableResolver.Setup(x => x.Resolve(It.IsAny<string>())).Returns((string?)null);
        mockVariableResolver.Setup(x => x.GetAllVariables()).Returns(new Dictionary<string, string>());

        var mockVariableExpander = new Mock<IVariableExpander>();
        mockVariableExpander.Setup(x => x.Expand(It.IsAny<string>(), It.IsAny<IVariableResolver>()))
            .Returns<string, IVariableResolver>((s, _) => s);

        var mockSecretMasker = new Mock<ISecretMasker>();
        mockSecretMasker.Setup(x => x.MaskSecrets(It.IsAny<string>())).Returns<string>(s => s);

        var performanceConfig = new PerformanceConfig
        {
            ReuseContainers = true,
            CacheImages = true,
            ParallelSteps = false
        };

        _dockerRunner = new DockerJobRunner(
            mockContainerManager,
            mockImageMapper.Object,
            dockerExecutorFactory,
            dockerLogger,
            mockVariableResolver.Object,
            mockVariableExpander.Object,
            mockSecretMasker.Object,
            performanceConfig: performanceConfig);

        // Host runner setup
        var hostLogger = NullLogger<HostJobRunner>.Instance;
        var mockProcessExecutor = new MockProcessExecutor
        {
            SimulatedExecutionTime = TimeSpan.FromMilliseconds(15)
        };

        var hostExecutorFactory = new HostStepExecutorFactory(CreateMockHostStepExecutors());

        _hostRunner = new HostJobRunner(
            mockProcessExecutor,
            hostExecutorFactory,
            hostLogger,
            mockVariableResolver.Object,
            mockVariableExpander.Object,
            mockSecretMasker.Object,
            showSecurityWarning: false);
    }

    private static IEnumerable<IStepExecutor> CreateMockStepExecutors()
    {
        var stepTypes = new[] { "script", "checkout", "bash", "dotnet", "npm", "python" };
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
                    Duration = TimeSpan.FromMilliseconds(30),
                    StartTime = DateTimeOffset.Now,
                    EndTime = DateTimeOffset.Now.AddMilliseconds(30)
                });
            yield return mock.Object;
        }
    }

    private static IEnumerable<IHostStepExecutor> CreateMockHostStepExecutors()
    {
        var stepTypes = new[] { "script", "checkout", "bash", "dotnet", "npm", "python" };
        foreach (var stepType in stepTypes)
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
                    Duration = TimeSpan.FromMilliseconds(15),
                    StartTime = DateTimeOffset.Now,
                    EndTime = DateTimeOffset.Now.AddMilliseconds(15)
                });
            yield return mock.Object;
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        BenchmarkWorkspaceSetup.CleanupWorkspace(_workspacePath);
    }

    [Benchmark(Baseline = true, Description = ".NET Build Pipeline (Docker)")]
    [BenchmarkCategory("RealWorld", "DotNet")]
    public async Task<JobExecutionResult> DotNetBuildPipeline_Docker()
    {
        var job = _dotnetBuildPipeline.Jobs.Values.First();
        return await _dockerRunner.RunJobAsync(job, _workspacePath);
    }

    [Benchmark(Description = ".NET Build Pipeline (Host)")]
    [BenchmarkCategory("RealWorld", "DotNet")]
    public async Task<JobExecutionResult> DotNetBuildPipeline_Host()
    {
        var job = _dotnetBuildPipeline.Jobs.Values.First();
        return await _hostRunner.RunJobAsync(job, _workspacePath);
    }

    [Benchmark(Description = "npm Build Pipeline (Docker)")]
    [BenchmarkCategory("RealWorld", "Npm")]
    public async Task<JobExecutionResult> NpmBuildPipeline_Docker()
    {
        var job = _npmBuildPipeline.Jobs.Values.First();
        return await _dockerRunner.RunJobAsync(job, _workspacePath);
    }

    [Benchmark(Description = "npm Build Pipeline (Host)")]
    [BenchmarkCategory("RealWorld", "Npm")]
    public async Task<JobExecutionResult> NpmBuildPipeline_Host()
    {
        var job = _npmBuildPipeline.Jobs.Values.First();
        return await _hostRunner.RunJobAsync(job, _workspacePath);
    }

    [Benchmark(Description = "Multi-Job Pipeline (all jobs, Docker)")]
    [BenchmarkCategory("RealWorld", "MultiJob")]
    public async Task<List<JobExecutionResult>> MultiJobPipeline_Docker()
    {
        var results = new List<JobExecutionResult>();
        foreach (var job in _multiJobPipeline.Jobs.Values)
        {
            results.Add(await _dockerRunner.RunJobAsync(job, _workspacePath));
        }
        return results;
    }

    [Benchmark(Description = "Multi-Job Pipeline (all jobs, Host)")]
    [BenchmarkCategory("RealWorld", "MultiJob")]
    public async Task<List<JobExecutionResult>> MultiJobPipeline_Host()
    {
        var results = new List<JobExecutionResult>();
        foreach (var job in _multiJobPipeline.Jobs.Values)
        {
            results.Add(await _hostRunner.RunJobAsync(job, _workspacePath));
        }
        return results;
    }

    [Benchmark(Description = "Full CI simulation (parse + execute)")]
    [BenchmarkCategory("RealWorld", "FullCI")]
    public async Task<(Pipeline, List<JobExecutionResult>)> FullCISimulation()
    {
        var parser = new GitHubActionsParser();
        var pipeline = parser.Parse(GenerateMultiJobWorkflow());

        var results = new List<JobExecutionResult>();
        foreach (var job in pipeline.Jobs.Values)
        {
            results.Add(await _dockerRunner.RunJobAsync(job, _workspacePath));
        }

        return (pipeline, results);
    }

    #region Workflow Generators

    private static string GenerateDotnetBuildWorkflow()
    {
        return """
            name: .NET Build and Test
            on:
              push:
                branches: [main, develop]
              pull_request:
                branches: [main]

            env:
              DOTNET_VERSION: '8.0.x'
              BUILD_CONFIGURATION: Release

            jobs:
              build:
                runs-on: ubuntu-latest
                steps:
                  - name: Checkout code
                    uses: actions/checkout@v4

                  - name: Setup .NET
                    uses: actions/setup-dotnet@v4
                    with:
                      dotnet-version: ${{ env.DOTNET_VERSION }}

                  - name: Restore dependencies
                    run: dotnet restore

                  - name: Build
                    run: dotnet build --no-restore --configuration ${{ env.BUILD_CONFIGURATION }}

                  - name: Run tests
                    run: dotnet test --no-build --configuration ${{ env.BUILD_CONFIGURATION }}
            """;
    }

    private static string GenerateNpmBuildWorkflow()
    {
        return """
            name: npm Build
            on: push

            jobs:
              build:
                runs-on: ubuntu-latest
                steps:
                  - uses: actions/checkout@v4
                  - uses: actions/setup-node@v3
                    with:
                      node-version: 18
                  - run: npm ci
                  - run: npm run build
                  - run: npm test
            """;
    }

    private static string GenerateMultiJobWorkflow()
    {
        return """
            name: Multi-Job Workflow
            on: [push, pull_request]

            jobs:
              setup:
                runs-on: ubuntu-latest
                steps:
                  - uses: actions/checkout@v4
                  - name: Validate setup
                    run: echo "Setup complete"

              build-backend:
                runs-on: ubuntu-latest
                needs: setup
                steps:
                  - uses: actions/checkout@v4
                  - uses: actions/setup-dotnet@v4
                    with:
                      dotnet-version: '8.0'
                  - run: dotnet build ./src/backend

              build-frontend:
                runs-on: ubuntu-latest
                needs: setup
                steps:
                  - uses: actions/checkout@v4
                  - uses: actions/setup-node@v3
                    with:
                      node-version: '18'
                  - run: npm ci

              integration-test:
                runs-on: ubuntu-latest
                needs: [build-backend, build-frontend]
                steps:
                  - uses: actions/checkout@v4
                  - name: Run integration tests
                    run: echo "Running integration tests"

              deploy:
                runs-on: ubuntu-latest
                needs: integration-test
                steps:
                  - uses: actions/checkout@v4
                  - name: Deploy
                    run: echo "Deploying"
            """;
    }

    #endregion
}
