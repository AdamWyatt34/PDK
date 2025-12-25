namespace PDK.Tests.Integration.Sprint11;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PDK.CLI;
using PDK.CLI.DryRun;
using PDK.CLI.WatchMode;
using PDK.Core.Filtering;
using PDK.Core.Logging;
using PDK.Core.Models;
using PDK.Core.Validation;
using PDK.Core.Validation.Phases;
using PDK.Core.Variables;
using PDK.Providers.GitHub;
using PDK.Providers.AzureDevOps;
using PDK.Runners;
using Xunit.Abstractions;

/// <summary>
/// Base class for Sprint 11 integration tests providing common infrastructure.
/// Configures services for Watch Mode, Dry-Run, Logging, and Step Filtering.
/// </summary>
public abstract class Sprint11IntegrationTestBase : IDisposable
{
    protected readonly string TestDir;
    protected readonly ServiceProvider ServiceProvider;
    protected readonly ILogger Logger;
    protected readonly ITestOutputHelper Output;

    protected Sprint11IntegrationTestBase(ITestOutputHelper output)
    {
        Output = output;
        TestDir = Path.Combine(Path.GetTempPath(), $"pdk-sprint11-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(TestDir);

        var services = new ServiceCollection();
        ConfigureServices(services);
        ServiceProvider = services.BuildServiceProvider();

        var loggerFactory = ServiceProvider.GetRequiredService<ILoggerFactory>();
        Logger = loggerFactory.CreateLogger(GetType());
    }

    public virtual void Dispose()
    {
        ServiceProvider.Dispose();
        CleanupTestDirectory();
        GC.SuppressFinalize(this);
    }

    protected virtual void ConfigureServices(ServiceCollection services)
    {
        // Logging
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug);
        });

        // Parsers
        services.AddSingleton<IPipelineParser, GitHubActionsParser>();
        services.AddSingleton<IPipelineParser, AzureDevOpsParser>();
        services.AddSingleton<IPipelineParserFactory, PipelineParserFactory>();
        services.AddSingleton<PipelineParserFactory>();

        // Variables
        services.AddSingleton<IBuiltInVariables, BuiltInVariables>();
        services.AddSingleton<IVariableResolver, VariableResolver>();
        services.AddSingleton<IVariableExpander, VariableExpander>();

        // Secrets & Logging
        services.AddSingleton<ISecretMasker, SecretMasker>();

        // Validation phases (for dry-run)
        services.AddSingleton<IValidationPhase, SchemaValidationPhase>();
        services.AddSingleton<IValidationPhase, ExecutorValidationPhase>();
        services.AddSingleton<IValidationPhase, VariableValidationPhase>();
        services.AddSingleton<IValidationPhase, DependencyValidationPhase>();

        // Step Filtering
        services.AddTransient<IStepFilter, NoOpFilter>();
        services.AddTransient<StepFilterBuilder>();
    }

    /// <summary>
    /// Creates a test pipeline file with the given YAML content.
    /// </summary>
    protected string CreatePipelineFile(string fileName, string yamlContent)
    {
        var filePath = Path.Combine(TestDir, fileName);
        File.WriteAllText(filePath, yamlContent);
        return filePath;
    }

    /// <summary>
    /// Creates a standard 5-step GitHub Actions pipeline for testing.
    /// </summary>
    protected string CreateStandardPipeline(string fileName = "ci.yml")
    {
        var yamlContent = """
            name: CI Pipeline
            on: push
            jobs:
              build:
                runs-on: ubuntu-latest
                steps:
                  - name: Checkout
                    uses: actions/checkout@v4
                  - name: Setup
                    run: echo "Setting up..."
                  - name: Build
                    run: echo "Building..."
                  - name: Test
                    run: echo "Testing..."
                  - name: Deploy
                    run: echo "Deploying..."
            """;
        return CreatePipelineFile(fileName, yamlContent);
    }

    /// <summary>
    /// Creates a multi-job pipeline with dependencies.
    /// </summary>
    protected string CreateMultiJobPipeline(string fileName = "multi-job.yml")
    {
        var yamlContent = """
            name: Multi-Job Pipeline
            on: push
            jobs:
              build:
                runs-on: ubuntu-latest
                steps:
                  - name: Build
                    run: echo "Building..."
              test:
                runs-on: ubuntu-latest
                needs: build
                steps:
                  - name: Test
                    run: echo "Testing..."
              deploy:
                runs-on: ubuntu-latest
                needs: test
                steps:
                  - name: Deploy
                    run: echo "Deploying..."
            """;
        return CreatePipelineFile(fileName, yamlContent);
    }

    /// <summary>
    /// Creates a pipeline with variables for logging tests.
    /// </summary>
    protected string CreatePipelineWithSecrets(string fileName = "secrets.yml")
    {
        var yamlContent = """
            name: Secrets Pipeline
            on: push
            jobs:
              build:
                runs-on: ubuntu-latest
                env:
                  API_KEY: ${{ secrets.API_KEY }}
                  DATABASE_URL: ${{ secrets.DATABASE_URL }}
                steps:
                  - name: Build with secrets
                    run: echo "Using API_KEY and DATABASE_URL"
            """;
        return CreatePipelineFile(fileName, yamlContent);
    }

    /// <summary>
    /// Creates a file watcher for testing watch mode.
    /// </summary>
    protected FileWatcher CreateFileWatcher()
    {
        var loggerFactory = ServiceProvider.GetRequiredService<ILoggerFactory>();
        return new FileWatcher(loggerFactory.CreateLogger<FileWatcher>());
    }

    /// <summary>
    /// Creates a debounce engine for testing.
    /// </summary>
    protected DebounceEngine CreateDebounceEngine(int debounceMs = 100)
    {
        var loggerFactory = ServiceProvider.GetRequiredService<ILoggerFactory>();
        return new DebounceEngine(loggerFactory.CreateLogger<DebounceEngine>())
        {
            DebounceMs = debounceMs
        };
    }

    /// <summary>
    /// Creates an execution queue for testing.
    /// </summary>
    protected ExecutionQueue CreateExecutionQueue()
    {
        var loggerFactory = ServiceProvider.GetRequiredService<ILoggerFactory>();
        return new ExecutionQueue(loggerFactory.CreateLogger<ExecutionQueue>());
    }

    /// <summary>
    /// Parses a pipeline file and returns the pipeline model.
    /// </summary>
    protected async Task<Pipeline> ParsePipelineAsync(string filePath)
    {
        var parserFactory = ServiceProvider.GetRequiredService<PipelineParserFactory>();
        var parser = parserFactory.GetParser(filePath);
        return await parser.ParseFile(filePath);
    }

    /// <summary>
    /// Creates a validation context for dry-run testing.
    /// </summary>
    protected ValidationContext CreateValidationContext(string runnerType = "auto")
    {
        var variableResolver = ServiceProvider.GetRequiredService<IVariableResolver>();
        return new ValidationContext
        {
            VariableResolver = variableResolver,
            RunnerType = runnerType
        };
    }

    /// <summary>
    /// Runs all validation phases on a pipeline.
    /// </summary>
    protected async Task<List<DryRunValidationError>> ValidatePipelineAsync(Pipeline pipeline, ValidationContext? context = null)
    {
        context ??= CreateValidationContext();
        var phases = ServiceProvider.GetServices<IValidationPhase>().OrderBy(p => p.Order);
        var allErrors = new List<DryRunValidationError>();

        foreach (var phase in phases)
        {
            var errors = await phase.ValidateAsync(pipeline, context);
            allErrors.AddRange(errors);
        }

        return allErrors;
    }

    /// <summary>
    /// Creates a step filter from options.
    /// </summary>
    protected IStepFilter CreateStepFilter(FilterOptions options, Pipeline pipeline)
    {
        var builder = ServiceProvider.GetRequiredService<StepFilterBuilder>();
        return builder.Build(options, pipeline);
    }

    /// <summary>
    /// Triggers a file change by modifying a file.
    /// </summary>
    protected async Task TriggerFileChangeAsync(string filePath, string? newContent = null)
    {
        newContent ??= $"# Modified at {DateTime.UtcNow:O}\n" + await File.ReadAllTextAsync(filePath);
        await File.WriteAllTextAsync(filePath, newContent);
    }

    /// <summary>
    /// Creates a temporary file in the test directory.
    /// </summary>
    protected async Task<string> CreateTempFileAsync(string relativePath, string content)
    {
        var fullPath = Path.Combine(TestDir, relativePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        await File.WriteAllTextAsync(fullPath, content);
        return fullPath;
    }

    /// <summary>
    /// Captures log output for verification.
    /// </summary>
    protected StringWriter CaptureLogOutput()
    {
        var writer = new StringWriter();
        Console.SetOut(writer);
        return writer;
    }

    /// <summary>
    /// Restores standard console output.
    /// </summary>
    protected void RestoreConsoleOutput()
    {
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
    }

    private void CleanupTestDirectory()
    {
        try
        {
            if (Directory.Exists(TestDir))
            {
                // Retry with delay for file system operations
                for (int i = 0; i < 3; i++)
                {
                    try
                    {
                        Directory.Delete(TestDir, recursive: true);
                        break;
                    }
                    catch (IOException) when (i < 2)
                    {
                        Thread.Sleep(100);
                    }
                }
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}

/// <summary>
/// No-op filter that allows all steps to execute.
/// </summary>
public class NoOpFilter : IStepFilter
{
    public static readonly NoOpFilter Instance = new();

    public FilterResult ShouldExecute(Step step, int stepIndex, Job job)
    {
        return FilterResult.Execute("No filter applied");
    }
}
