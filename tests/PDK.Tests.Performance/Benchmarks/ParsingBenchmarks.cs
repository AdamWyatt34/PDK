using BenchmarkDotNet.Attributes;
using PDK.Core.Models;
using PDK.Providers.GitHub;
using PDK.Tests.Performance.Config;

namespace PDK.Tests.Performance.Benchmarks;

/// <summary>
/// Benchmarks for YAML parsing operations.
/// REQ-10-030: Parsing benchmarks for baseline measurement.
/// </summary>
[MemoryDiagnoser]
[Config(typeof(ParsingBenchmarkConfig))]
public class ParsingBenchmarks
{
    private GitHubActionsParser _parser = null!;
    private string _simpleWorkflow = null!;
    private string _dotnetBuildWorkflow = null!;
    private string _npmBuildWorkflow = null!;
    private string _multiJobWorkflow = null!;
    private string _largeWorkflow = null!;

    [GlobalSetup]
    public void Setup()
    {
        _parser = new GitHubActionsParser();

        // Load workflow files from embedded resources
        var workflowsPath = Path.Combine(AppContext.BaseDirectory, "Workflows");

        _simpleWorkflow = LoadWorkflowOrDefault(workflowsPath, "simple.yml", GenerateSimpleWorkflow());
        _dotnetBuildWorkflow = LoadWorkflowOrDefault(workflowsPath, "dotnet-build.yml", GenerateDotnetBuildWorkflow());
        _npmBuildWorkflow = LoadWorkflowOrDefault(workflowsPath, "npm-build.yml", GenerateNpmBuildWorkflow());
        _multiJobWorkflow = LoadWorkflowOrDefault(workflowsPath, "multi-job.yml", GenerateMultiJobWorkflow());
        _largeWorkflow = GenerateLargeWorkflow(50); // 50 steps for stress testing
    }

    private static string LoadWorkflowOrDefault(string basePath, string fileName, string defaultContent)
    {
        var filePath = Path.Combine(basePath, fileName);
        return File.Exists(filePath) ? File.ReadAllText(filePath) : defaultContent;
    }

    [Benchmark(Baseline = true, Description = "Parse simple 1-step workflow")]
    public Pipeline ParseSimpleWorkflow()
    {
        return _parser.Parse(_simpleWorkflow);
    }

    [Benchmark(Description = "Parse .NET build workflow (5 steps)")]
    public Pipeline ParseDotnetBuildWorkflow()
    {
        return _parser.Parse(_dotnetBuildWorkflow);
    }

    [Benchmark(Description = "Parse npm build workflow (4 steps)")]
    public Pipeline ParseNpmBuildWorkflow()
    {
        return _parser.Parse(_npmBuildWorkflow);
    }

    [Benchmark(Description = "Parse multi-job workflow (5 jobs)")]
    public Pipeline ParseMultiJobWorkflow()
    {
        return _parser.Parse(_multiJobWorkflow);
    }

    [Benchmark(Description = "Parse large workflow (50 steps)")]
    public Pipeline ParseLargeWorkflow()
    {
        return _parser.Parse(_largeWorkflow);
    }

    [Benchmark(Description = "Parse all workflows combined")]
    public (Pipeline, Pipeline, Pipeline, Pipeline) ParseAllWorkflows()
    {
        return (
            _parser.Parse(_simpleWorkflow),
            _parser.Parse(_dotnetBuildWorkflow),
            _parser.Parse(_npmBuildWorkflow),
            _parser.Parse(_multiJobWorkflow)
        );
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
                    run: dotnet test --no-build --configuration ${{ env.BUILD_CONFIGURATION }} --verbosity normal
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
                if: github.ref == 'refs/heads/main'
                steps:
                  - uses: actions/checkout@v4
                  - name: Deploy
                    run: echo "Deploying"
            """;
    }

    private static string GenerateLargeWorkflow(int stepCount)
    {
        var steps = new System.Text.StringBuilder();
        for (int i = 1; i <= stepCount; i++)
        {
            steps.AppendLine($"      - name: Step {i}");
            steps.AppendLine($"        run: echo \"Executing step {i}\"");
        }

        return $"""
            name: Large Workflow
            on: push

            jobs:
              large-job:
                runs-on: ubuntu-latest
                steps:
            {steps}
            """;
    }

    #endregion
}
