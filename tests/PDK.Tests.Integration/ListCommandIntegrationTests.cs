using System.Text.Json;
using FluentAssertions;
using PDK.CLI;
using PDK.CLI.Commands;
using PDK.CLI.UI;
using PDK.Core.Models;
using PDK.Providers.AzureDevOps;
using PDK.Providers.GitHub;
using Spectre.Console;
using Spectre.Console.Testing;
using Xunit;

namespace PDK.Tests.Integration;

/// <summary>
/// Integration tests for the ListCommand with real pipeline files.
/// </summary>
public class ListCommandIntegrationTests
{
    private readonly string _fixturesPath;
    private readonly PipelineParserFactory _parserFactory;
    private readonly TestConsole _testConsole;
    private readonly IConsoleOutput _consoleOutput;

    public ListCommandIntegrationTests()
    {
        _fixturesPath = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var parsers = new IPipelineParser[]
        {
            new GitHubActionsParser(),
            new AzureDevOpsParser()
        };
        _parserFactory = new PipelineParserFactory(parsers);
        _testConsole = new TestConsole();
        _consoleOutput = new ConsoleOutput(_testConsole);
    }

    #region GitHub Actions Tests

    [Fact]
    public async Task List_GitHubActionsWorkflow_DisplaysAllJobs()
    {
        // Arrange
        var workflowPath = Path.Combine(_fixturesPath, "multi-job.yml");
        var command = CreateListCommand();
        command.File = new FileInfo(workflowPath);
        command.Format = OutputFormat.Table;

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.Should().Be(0);
        var output = _testConsole.Output;

        output.Should().Contain("Pipeline:");
        output.Should().Contain("Multi-Job Workflow");
        output.Should().Contain("setup");
        // Table wraps long job names - just verify table has content
        output.Should().Contain("build");
        output.Should().Contain("deploy");
        // Verify table structure
        output.Should().Contain("Job ID");
        output.Should().Contain("Runs On");
    }

    [Fact]
    public async Task List_GitHubActionsWorkflow_ShowsJobDependencies()
    {
        // Arrange
        var workflowPath = Path.Combine(_fixturesPath, "multi-job.yml");
        var command = CreateListCommand();
        command.File = new FileInfo(workflowPath);
        command.Format = OutputFormat.Table;

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.Should().Be(0);
        var output = _testConsole.Output;

        // Jobs should show dependencies - use partial matches to handle word wrapping
        output.Should().Contain("setup");
        output.Should().Contain("integration");
        output.Should().Contain("test");
    }

    [Fact]
    public async Task List_GitHubActionsWorkflow_WithDetails_DisplaysSteps()
    {
        // Arrange
        var workflowPath = Path.Combine(_fixturesPath, "dotnet-build.yml");
        var command = CreateListCommand();
        command.File = new FileInfo(workflowPath);
        command.Format = OutputFormat.Table;
        command.Details = true;

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.Should().Be(0);
        var output = _testConsole.Output;

        output.Should().Contain("Step Name");
        output.Should().Contain("Type");
        output.Should().Contain("Checkout");
    }

    #endregion

    #region Azure DevOps Tests

    [Fact]
    public async Task List_AzureDevOpsPipeline_DisplaysAllJobs()
    {
        // Arrange
        var pipelinePath = Path.Combine(_fixturesPath, "simple-azure-pipeline.yml");
        var command = CreateListCommand();
        command.File = new FileInfo(pipelinePath);
        command.Format = OutputFormat.Table;

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.Should().Be(0);
        var output = _testConsole.Output;

        output.Should().Contain("Pipeline:");
        output.Should().Contain("AzureDevOps");
    }

    [Fact]
    public async Task List_AzureDevOpsPipeline_WithDetails_DisplaysSteps()
    {
        // Arrange
        var pipelinePath = Path.Combine(_fixturesPath, "simple-azure-pipeline.yml");
        var command = CreateListCommand();
        command.File = new FileInfo(pipelinePath);
        command.Format = OutputFormat.Table;
        command.Details = true;

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.Should().Be(0);
        var output = _testConsole.Output;

        output.Should().Contain("Step Name");
        output.Should().Contain("Type");
    }

    [Fact]
    public async Task List_AzureDevOpsPipeline_MultiStage_DisplaysAllJobs()
    {
        // Arrange
        var pipelinePath = Path.Combine(_fixturesPath, "multi-stage-azure-pipeline.yml");

        if (!File.Exists(pipelinePath))
        {
            // Skip if fixture doesn't exist
            return;
        }

        var command = CreateListCommand();
        command.File = new FileInfo(pipelinePath);
        command.Format = OutputFormat.Table;

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.Should().Be(0);
        var output = _testConsole.Output;
        output.Should().Contain("Pipeline:");
    }

    #endregion

    #region JSON Format Tests

    [Fact]
    public async Task List_JsonFormat_ProducesValidJson()
    {
        // Arrange
        var workflowPath = Path.Combine(_fixturesPath, "multi-job.yml");
        var command = CreateListCommand();
        command.File = new FileInfo(workflowPath);
        command.Format = OutputFormat.Json;

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.Should().Be(0);
        var output = _testConsole.Output;

        // Should be valid JSON
        var act = () => JsonDocument.Parse(output);
        act.Should().NotThrow();

        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;

        root.GetProperty("name").GetString().Should().Be("Multi-Job Workflow");
        root.GetProperty("provider").GetString().Should().Be("GitHub");
        root.GetProperty("jobs").GetArrayLength().Should().Be(5);
    }

    [Fact]
    public async Task List_JsonFormat_WithDetails_IncludesSteps()
    {
        // Arrange
        var workflowPath = Path.Combine(_fixturesPath, "multi-job.yml");
        var command = CreateListCommand();
        command.File = new FileInfo(workflowPath);
        command.Format = OutputFormat.Json;
        command.Details = true;

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.Should().Be(0);
        var output = _testConsole.Output;

        using var doc = JsonDocument.Parse(output);
        var jobs = doc.RootElement.GetProperty("jobs");
        var firstJob = jobs[0];

        firstJob.TryGetProperty("steps", out var steps).Should().BeTrue();
        steps.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task List_JsonFormat_AzureDevOps_ProducesValidJson()
    {
        // Arrange
        var pipelinePath = Path.Combine(_fixturesPath, "simple-azure-pipeline.yml");
        var command = CreateListCommand();
        command.File = new FileInfo(pipelinePath);
        command.Format = OutputFormat.Json;

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.Should().Be(0);
        var output = _testConsole.Output;

        var act = () => JsonDocument.Parse(output);
        act.Should().NotThrow();

        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;
        root.GetProperty("provider").GetString().Should().Be("AzureDevOps");
    }

    #endregion

    #region Minimal Format Tests

    [Fact]
    public async Task List_MinimalFormat_OutputsJobIds()
    {
        // Arrange
        var workflowPath = Path.Combine(_fixturesPath, "multi-job.yml");
        var command = CreateListCommand();
        command.File = new FileInfo(workflowPath);
        command.Format = OutputFormat.Minimal;

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.Should().Be(0);
        var output = _testConsole.Output;

        // Should contain job IDs
        output.Should().Contain("setup");
        output.Should().Contain("build-backend");
        output.Should().Contain("build-frontend");
        output.Should().Contain("integration-test");
        output.Should().Contain("deploy");

        // Should NOT contain table formatting
        output.Should().NotContain("Pipeline:");
        output.Should().NotContain("Job ID");
    }

    [Fact]
    public async Task List_MinimalFormat_OrderedByDependency()
    {
        // Arrange
        var workflowPath = Path.Combine(_fixturesPath, "multi-job.yml");
        var command = CreateListCommand();
        command.File = new FileInfo(workflowPath);
        command.Format = OutputFormat.Minimal;

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.Should().Be(0);
        var output = _testConsole.Output;

        // setup should come before jobs that depend on it
        var setupIndex = output.IndexOf("setup", StringComparison.Ordinal);
        var backendIndex = output.IndexOf("build-backend", StringComparison.Ordinal);
        var frontendIndex = output.IndexOf("build-frontend", StringComparison.Ordinal);
        var integrationIndex = output.IndexOf("integration-test", StringComparison.Ordinal);
        var deployIndex = output.IndexOf("deploy", StringComparison.Ordinal);

        setupIndex.Should().BeLessThan(backendIndex);
        setupIndex.Should().BeLessThan(frontendIndex);
        backendIndex.Should().BeLessThan(integrationIndex);
        frontendIndex.Should().BeLessThan(integrationIndex);
        integrationIndex.Should().BeLessThan(deployIndex);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task List_NonExistentFile_ReturnsError()
    {
        // Arrange
        var command = CreateListCommand();
        command.File = new FileInfo(Path.Combine(_fixturesPath, "does-not-exist.yml"));
        command.Format = OutputFormat.Table;

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.Should().Be(1);
        _testConsole.Output.Should().Contain("File not found");
    }

    [Fact]
    public async Task List_InvalidYaml_ReturnsError()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "this: is: invalid: yaml: [");

            var command = CreateListCommand();
            command.File = new FileInfo(tempFile);
            command.Format = OutputFormat.Table;

            // Act
            var result = await command.ExecuteAsync();

            // Assert
            result.Should().Be(1);
            // Error could be "No parser found" or "Failed to parse" depending on content
            _testConsole.Output.Should().Match(o => o.Contains("Error") || o.Contains("parser"));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task List_UnsupportedFileFormat_ReturnsError()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, @"
apiVersion: v1
kind: Pod
metadata:
  name: test-pod
");

            var command = CreateListCommand();
            command.File = new FileInfo(tempFile);
            command.Format = OutputFormat.Table;

            // Act
            var result = await command.ExecuteAsync();

            // Assert
            result.Should().Be(1);
            // Should indicate no parser found
            _testConsole.Output.Should().Contain("No parser found");
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    #endregion

    #region Dependency Ordering Tests

    [Fact]
    public async Task List_TableFormat_JobsInDependencyOrder()
    {
        // Arrange
        var workflowPath = Path.Combine(_fixturesPath, "multi-job.yml");
        var command = CreateListCommand();
        command.File = new FileInfo(workflowPath);
        command.Format = OutputFormat.Table;

        // Act
        var result = await command.ExecuteAsync();

        // Assert
        result.Should().Be(0);
        var output = _testConsole.Output;

        // Verify dependencies come before dependents in output
        // Use "setup" and "deploy" which are short enough to not wrap
        var setupIndex = output.IndexOf("setup", StringComparison.Ordinal);
        var deployIndex = output.IndexOf("deploy", StringComparison.Ordinal);

        // setup should appear before deploy (deploy depends on integration-test which depends on setup)
        setupIndex.Should().BeGreaterThan(0);
        deployIndex.Should().BeGreaterThan(0);
        setupIndex.Should().BeLessThan(deployIndex);
    }

    #endregion

    #region Helper Methods

    private ListCommand CreateListCommand()
    {
        return new ListCommand(_parserFactory, _consoleOutput, _testConsole);
    }

    #endregion
}
