namespace PDK.Tests.Unit.Runners;

using Microsoft.Extensions.Logging;
using Moq;
using PDK.Core.Models;
using PDK.Runners;
using PDK.Runners.Models;

/// <summary>
/// Base class for runner-related tests, providing common mocking infrastructure and helper methods.
/// </summary>
public abstract class RunnerTestBase
{
    /// <summary>
    /// Gets the mocked container manager.
    /// </summary>
    protected Mock<PDK.Runners.IContainerManager> MockContainerManager { get; }

    /// <summary>
    /// Gets the mocked logger for Docker job runner.
    /// </summary>
    protected Mock<ILogger<DockerJobRunner>> MockLogger { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="RunnerTestBase"/> class.
    /// </summary>
    protected RunnerTestBase()
    {
        MockContainerManager = new Mock<PDK.Runners.IContainerManager>();
        MockLogger = new Mock<ILogger<DockerJobRunner>>();
    }

    /// <summary>
    /// Creates a default test execution context with common test values.
    /// </summary>
    /// <returns>An execution context configured for testing.</returns>
    protected ExecutionContext CreateTestContext()
    {
        return new ExecutionContext
        {
            ContainerId = "test-container-123",
            ContainerManager = MockContainerManager.Object,
            WorkspacePath = "/tmp/workspace",
            ContainerWorkspacePath = "/workspace",
            Environment = new Dictionary<string, string>
            {
                ["TEST_VAR"] = "test-value",
                ["WORKSPACE"] = "/workspace"
            },
            WorkingDirectory = ".",
            JobInfo = new JobMetadata
            {
                JobName = "TestJob",
                JobId = "job-123",
                Runner = "ubuntu-latest"
            }
        };
    }

    /// <summary>
    /// Creates a test job with the specified number of steps.
    /// </summary>
    /// <param name="stepCount">The number of steps to create.</param>
    /// <returns>A job configured for testing.</returns>
    protected Job CreateTestJob(int stepCount = 1)
    {
        var steps = new List<Step>();
        for (int i = 0; i < stepCount; i++)
        {
            steps.Add(CreateTestStep(StepType.Script, $"Step {i + 1}"));
        }

        return new Job
        {
            Id = "job-123",
            Name = "TestJob",
            RunsOn = "ubuntu-latest",
            Steps = steps,
            Environment = new Dictionary<string, string>
            {
                ["JOB_VAR"] = "job-value"
            }
        };
    }

    /// <summary>
    /// Creates a test step with the specified type and name.
    /// </summary>
    /// <param name="type">The step type.</param>
    /// <param name="name">The step name.</param>
    /// <returns>A step configured for testing.</returns>
    protected Step CreateTestStep(StepType type, string name)
    {
        return new Step
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            Type = type,
            Script = "echo 'test'",
            Shell = "bash",
            With = new Dictionary<string, string>(),
            Environment = new Dictionary<string, string>(),
            ContinueOnError = false
        };
    }

    /// <summary>
    /// Creates a successful execution result.
    /// </summary>
    /// <returns>An execution result indicating success.</returns>
    protected ExecutionResult CreateSuccessResult()
    {
        return new ExecutionResult
        {
            ExitCode = 0,
            StandardOutput = "Command executed successfully",
            StandardError = string.Empty,
            Duration = TimeSpan.FromSeconds(1.0)
        };
    }

    /// <summary>
    /// Creates a failed execution result with the specified exit code.
    /// </summary>
    /// <param name="exitCode">The exit code indicating failure.</param>
    /// <returns>An execution result indicating failure.</returns>
    protected ExecutionResult CreateFailureResult(int exitCode = 1)
    {
        return new ExecutionResult
        {
            ExitCode = exitCode,
            StandardOutput = string.Empty,
            StandardError = "Command failed",
            Duration = TimeSpan.FromSeconds(0.5)
        };
    }

    /// <summary>
    /// Creates a step execution result indicating success.
    /// </summary>
    /// <param name="stepName">The name of the step.</param>
    /// <returns>A step execution result indicating success.</returns>
    protected StepExecutionResult CreateSuccessStepResult(string stepName)
    {
        return new StepExecutionResult
        {
            StepName = stepName,
            Success = true,
            ExitCode = 0,
            Output = "Step completed successfully",
            ErrorOutput = string.Empty,
            Duration = TimeSpan.FromSeconds(1.0),
            StartTime = DateTimeOffset.Now,
            EndTime = DateTimeOffset.Now.AddSeconds(1)
        };
    }

    /// <summary>
    /// Creates a step execution result indicating failure.
    /// </summary>
    /// <param name="stepName">The name of the step.</param>
    /// <param name="exitCode">The exit code indicating failure.</param>
    /// <returns>A step execution result indicating failure.</returns>
    protected StepExecutionResult CreateFailureStepResult(string stepName, int exitCode = 1)
    {
        return new StepExecutionResult
        {
            StepName = stepName,
            Success = false,
            ExitCode = exitCode,
            Output = string.Empty,
            ErrorOutput = "Step failed",
            Duration = TimeSpan.FromSeconds(0.5),
            StartTime = DateTimeOffset.Now,
            EndTime = DateTimeOffset.Now.AddSeconds(0.5)
        };
    }
}
