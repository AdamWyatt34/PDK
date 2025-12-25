namespace PDK.Tests.Integration.DryRun;

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PDK.CLI;
using PDK.Core.Logging;
using PDK.Core.Models;
using PDK.Core.Validation;
using PDK.Core.Validation.Phases;
using PDK.Core.Variables;
using PDK.Providers.GitHub;
using PDK.Providers.AzureDevOps;
using Xunit;

/// <summary>
/// Integration tests for the dry-run functionality.
/// Tests end-to-end validation and execution plan generation.
/// </summary>
public class DryRunIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ServiceProvider _serviceProvider;

    public DryRunIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pdk-dryrun-tests-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private static void ConfigureServices(ServiceCollection services)
    {
        services.AddLogging();

        // Parsers
        services.AddSingleton<IPipelineParser, GitHubActionsParser>();
        services.AddSingleton<IPipelineParser, AzureDevOpsParser>();
        services.AddSingleton<PipelineParserFactory>();

        // Variables
        services.AddSingleton<IBuiltInVariables, BuiltInVariables>();
        services.AddSingleton<IVariableResolver, VariableResolver>();
        services.AddSingleton<IVariableExpander, VariableExpander>();

        // Secrets
        services.AddSingleton<ISecretMasker, SecretMasker>();

        // Validation phases
        services.AddSingleton<IValidationPhase, SchemaValidationPhase>();
        services.AddSingleton<IValidationPhase, ExecutorValidationPhase>();
        services.AddSingleton<IValidationPhase, VariableValidationPhase>();
        services.AddSingleton<IValidationPhase, DependencyValidationPhase>();
    }

    #region GitHub Actions Pipeline Tests

    [Fact]
    public async Task ValidatePipeline_ValidGitHubActionsWorkflow_Passes()
    {
        // Arrange
        var pipelineFile = Path.Combine(_tempDir, "ci.yml");
        File.WriteAllText(pipelineFile, """
            name: CI
            on: push
            jobs:
              build:
                runs-on: ubuntu-latest
                steps:
                  - uses: actions/checkout@v4
                  - run: echo "Hello, World!"
            """);

        var parserFactory = _serviceProvider.GetRequiredService<PipelineParserFactory>();
        var phases = _serviceProvider.GetServices<IValidationPhase>().OrderBy(p => p.Order);
        var variableResolver = _serviceProvider.GetRequiredService<IVariableResolver>();

        // Parse
        var parser = parserFactory.GetParser(pipelineFile);
        var pipeline = await parser.ParseFile(pipelineFile);

        // Validate
        var context = new ValidationContext
        {
            VariableResolver = variableResolver,
            RunnerType = "auto"
        };

        var allErrors = new List<DryRunValidationError>();
        foreach (var phase in phases)
        {
            var errors = await phase.ValidateAsync(pipeline, context);
            allErrors.AddRange(errors);
        }

        // Assert
        allErrors.Where(e => e.Severity == ValidationSeverity.Error).Should().BeEmpty();
        pipeline.Provider.Should().Be(PipelineProvider.GitHub);
        pipeline.Jobs.Should().ContainKey("build");
    }

    [Fact]
    public async Task ValidatePipeline_WorkflowWithEmptySteps_ThrowsParseException()
    {
        // Arrange - Has runs-on but empty steps list (parser catches this)
        var pipelineFile = Path.Combine(_tempDir, "empty-steps.yml");
        File.WriteAllText(pipelineFile, """
            name: EmptySteps
            on: push
            jobs:
              build:
                runs-on: ubuntu-latest
                steps: []
            """);

        var parserFactory = _serviceProvider.GetRequiredService<PipelineParserFactory>();

        // Parse - should throw for empty steps
        var parser = parserFactory.GetParser(pipelineFile);

        // Act & Assert - Parser catches empty steps during parsing
        var exception = await Assert.ThrowsAsync<PipelineParseException>(
            async () => await parser.ParseFile(pipelineFile));

        exception.Message.Should().Contain("step");
    }

    [Fact]
    public async Task ValidatePipeline_CircularDependencies_ThrowsParseException()
    {
        // Arrange
        var pipelineFile = Path.Combine(_tempDir, "circular.yml");
        File.WriteAllText(pipelineFile, """
            name: Circular
            on: push
            jobs:
              a:
                runs-on: ubuntu-latest
                needs: c
                steps:
                  - run: echo A
              b:
                runs-on: ubuntu-latest
                needs: a
                steps:
                  - run: echo B
              c:
                runs-on: ubuntu-latest
                needs: b
                steps:
                  - run: echo C
            """);

        var parserFactory = _serviceProvider.GetRequiredService<PipelineParserFactory>();

        // Parse - should throw for circular dependencies
        var parser = parserFactory.GetParser(pipelineFile);

        // Act & Assert - Parser catches circular dependencies during parsing
        var exception = await Assert.ThrowsAsync<PipelineParseException>(
            async () => await parser.ParseFile(pipelineFile));

        exception.Message.Should().Contain("Circular dependency");
    }

    #endregion

    #region Azure DevOps Pipeline Tests

    [Fact]
    public async Task ValidatePipeline_ValidAzurePipeline_Passes()
    {
        // Arrange
        var pipelineFile = Path.Combine(_tempDir, "azure-pipelines.yml");
        File.WriteAllText(pipelineFile, """
            trigger:
              - main
            pool:
              vmImage: 'ubuntu-latest'
            steps:
              - script: echo Hello
                displayName: 'Hello Step'
            """);

        var parserFactory = _serviceProvider.GetRequiredService<PipelineParserFactory>();
        var phases = _serviceProvider.GetServices<IValidationPhase>().OrderBy(p => p.Order);
        var variableResolver = _serviceProvider.GetRequiredService<IVariableResolver>();

        // Parse
        var parser = parserFactory.GetParser(pipelineFile);
        var pipeline = await parser.ParseFile(pipelineFile);

        // Validate
        var context = new ValidationContext
        {
            VariableResolver = variableResolver,
            RunnerType = "auto"
        };

        var allErrors = new List<DryRunValidationError>();
        foreach (var phase in phases)
        {
            var errors = await phase.ValidateAsync(pipeline, context);
            allErrors.AddRange(errors);
        }

        // Assert
        allErrors.Where(e => e.Severity == ValidationSeverity.Error).Should().BeEmpty();
        pipeline.Provider.Should().Be(PipelineProvider.AzureDevOps);
    }

    #endregion

    #region Variable Validation Tests

    [Fact]
    public async Task ValidatePipeline_UndefinedVariable_ReturnsWarning()
    {
        // Arrange
        var pipelineFile = Path.Combine(_tempDir, "vars.yml");
        File.WriteAllText(pipelineFile, """
            name: Variables
            on: push
            jobs:
              build:
                runs-on: ubuntu-latest
                steps:
                  - run: echo ${UNDEFINED_VAR}
            """);

        var parserFactory = _serviceProvider.GetRequiredService<PipelineParserFactory>();
        var variablePhase = _serviceProvider.GetServices<IValidationPhase>()
            .OfType<VariableValidationPhase>().First();
        var variableResolver = _serviceProvider.GetRequiredService<IVariableResolver>();

        // Parse
        var parser = parserFactory.GetParser(pipelineFile);
        var pipeline = await parser.ParseFile(pipelineFile);

        // Validate
        var context = new ValidationContext
        {
            VariableResolver = variableResolver,
            RunnerType = "auto"
        };

        var errors = await variablePhase.ValidateAsync(pipeline, context);

        // Assert
        errors.Should().Contain(e =>
            e.Message.Contains("UNDEFINED_VAR") &&
            e.Severity == ValidationSeverity.Warning);
    }

    [Fact]
    public async Task ValidatePipeline_DefinedVariable_NoWarning()
    {
        // Arrange
        var pipelineFile = Path.Combine(_tempDir, "defined-vars.yml");
        File.WriteAllText(pipelineFile, """
            name: Variables
            on: push
            jobs:
              build:
                runs-on: ubuntu-latest
                steps:
                  - run: echo ${MY_VAR}
            """);

        var parserFactory = _serviceProvider.GetRequiredService<PipelineParserFactory>();
        var variablePhase = _serviceProvider.GetServices<IValidationPhase>()
            .OfType<VariableValidationPhase>().First();
        var variableResolver = _serviceProvider.GetRequiredService<IVariableResolver>();

        // Set the variable
        variableResolver.SetVariable("MY_VAR", "test-value", VariableSource.CliArgument);

        // Parse
        var parser = parserFactory.GetParser(pipelineFile);
        var pipeline = await parser.ParseFile(pipelineFile);

        // Validate
        var context = new ValidationContext
        {
            VariableResolver = variableResolver,
            RunnerType = "auto"
        };

        var errors = await variablePhase.ValidateAsync(pipeline, context);

        // Assert
        errors.Should().NotContain(e => e.Message.Contains("MY_VAR"));
    }

    #endregion

    #region Executor Validation Tests

    [Fact]
    public async Task ValidatePipeline_ExecutorPhase_SkipsWhenNoValidator()
    {
        // Arrange
        var pipelineFile = Path.Combine(_tempDir, "no-validator.yml");
        File.WriteAllText(pipelineFile, """
            name: NoValidator
            on: push
            jobs:
              build:
                runs-on: ubuntu-latest
                steps:
                  - uses: actions/checkout@v4
                  - run: echo "script step"
            """);

        var parserFactory = _serviceProvider.GetRequiredService<PipelineParserFactory>();
        var executorPhase = _serviceProvider.GetServices<IValidationPhase>()
            .OfType<ExecutorValidationPhase>().First();

        // Parse
        var parser = parserFactory.GetParser(pipelineFile);
        var pipeline = await parser.ParseFile(pipelineFile);

        // Validate with no ExecutorValidator - phase should be skipped
        var context = new ValidationContext
        {
            ExecutorValidator = null,
            RunnerType = "auto"
        };

        var errors = await executorPhase.ValidateAsync(pipeline, context);

        // Assert - No errors when validator is not provided (validation skipped)
        errors.Should().BeEmpty();
    }

    #endregion

    #region Execution Plan Tests

    [Fact]
    public async Task ValidatePipeline_ComputesJobExecutionOrder()
    {
        // Arrange
        var pipelineFile = Path.Combine(_tempDir, "execution-order.yml");
        File.WriteAllText(pipelineFile, """
            name: Order
            on: push
            jobs:
              build:
                runs-on: ubuntu-latest
                steps:
                  - run: echo build
              test:
                runs-on: ubuntu-latest
                needs: build
                steps:
                  - run: echo test
              deploy:
                runs-on: ubuntu-latest
                needs: test
                steps:
                  - run: echo deploy
            """);

        var parserFactory = _serviceProvider.GetRequiredService<PipelineParserFactory>();
        var dependencyPhase = _serviceProvider.GetServices<IValidationPhase>()
            .OfType<DependencyValidationPhase>().First();
        var variableResolver = _serviceProvider.GetRequiredService<IVariableResolver>();

        // Parse
        var parser = parserFactory.GetParser(pipelineFile);
        var pipeline = await parser.ParseFile(pipelineFile);

        // Validate
        var context = new ValidationContext
        {
            VariableResolver = variableResolver,
            RunnerType = "auto"
        };

        await dependencyPhase.ValidateAsync(pipeline, context);

        // Assert
        context.JobExecutionOrder.Should().ContainKey("build");
        context.JobExecutionOrder.Should().ContainKey("test");
        context.JobExecutionOrder.Should().ContainKey("deploy");

        // Build should come before test, test before deploy
        context.JobExecutionOrder["build"].Should().BeLessThan(context.JobExecutionOrder["test"]);
        context.JobExecutionOrder["test"].Should().BeLessThan(context.JobExecutionOrder["deploy"]);
    }

    #endregion
}
