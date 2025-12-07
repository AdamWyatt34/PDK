namespace PDK.Core.Models;

using PDK.Core.ErrorHandling;

/// <summary>
/// Exception thrown when pipeline execution fails.
/// </summary>
public class PipelineExecutionException : PdkException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PipelineExecutionException"/> class with a message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public PipelineExecutionException(string message)
        : base(ErrorCodes.JobExecutionFailed, message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PipelineExecutionException"/> class with a message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public PipelineExecutionException(string message, Exception innerException)
        : base(ErrorCodes.JobExecutionFailed, message, null, null, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PipelineExecutionException"/> class with full error details.
    /// </summary>
    /// <param name="errorCode">The error code.</param>
    /// <param name="message">The error message.</param>
    /// <param name="context">The error context.</param>
    /// <param name="suggestions">Recovery suggestions.</param>
    /// <param name="innerException">The inner exception.</param>
    public PipelineExecutionException(
        string errorCode,
        string message,
        ErrorContext? context = null,
        IEnumerable<string>? suggestions = null,
        Exception? innerException = null)
        : base(errorCode, message, context, suggestions, innerException)
    {
    }

    /// <summary>
    /// Creates a PipelineExecutionException for step execution failures.
    /// </summary>
    /// <param name="stepName">The name of the step that failed.</param>
    /// <param name="exitCode">The exit code.</param>
    /// <param name="output">The standard output.</param>
    /// <param name="errorOutput">The error output.</param>
    /// <param name="jobName">The name of the job containing the step.</param>
    /// <returns>A new PipelineExecutionException.</returns>
    public static PipelineExecutionException StepFailed(
        string stepName,
        int exitCode,
        string? output = null,
        string? errorOutput = null,
        string? jobName = null)
    {
        var context = ErrorContext.FromStepExecution(stepName, exitCode, output, errorOutput);
        if (jobName != null)
        {
            context = context.WithJob(jobName);
        }

        return new PipelineExecutionException(
            ErrorCodes.StepExecutionFailed,
            $"Step '{stepName}' failed with exit code {exitCode}",
            context,
            GetExitCodeSuggestions(exitCode));
    }

    /// <summary>
    /// Creates a PipelineExecutionException for step timeouts.
    /// </summary>
    /// <param name="stepName">The name of the step that timed out.</param>
    /// <param name="timeout">The timeout duration.</param>
    /// <param name="jobName">The name of the job containing the step.</param>
    /// <returns>A new PipelineExecutionException.</returns>
    public static PipelineExecutionException StepTimeout(
        string stepName,
        TimeSpan timeout,
        string? jobName = null)
    {
        var context = new ErrorContext
        {
            StepName = stepName,
            Duration = timeout
        };

        if (jobName != null)
        {
            context = context.WithJob(jobName);
        }

        return new PipelineExecutionException(
            ErrorCodes.StepTimeout,
            $"Step '{stepName}' timed out after {timeout.TotalSeconds:F0} seconds",
            context,
            [
                "Increase the timeout value in your pipeline configuration",
                "Optimize the step to run faster",
                "Split long-running tasks into smaller steps",
                "Check for network issues or slow dependencies"
            ]);
    }

    /// <summary>
    /// Creates a PipelineExecutionException for command not found errors.
    /// </summary>
    /// <param name="command">The command that was not found.</param>
    /// <param name="stepName">The name of the step.</param>
    /// <param name="imageName">The container image name.</param>
    /// <returns>A new PipelineExecutionException.</returns>
    public static PipelineExecutionException CommandNotFound(
        string command,
        string stepName,
        string? imageName = null)
    {
        var context = new ErrorContext
        {
            StepName = stepName,
            Command = command,
            ExitCode = 127,
            ImageName = imageName
        };

        var suggestions = new List<string>
        {
            $"The command '{command}' was not found in the execution environment"
        };

        if (imageName != null)
        {
            suggestions.Add($"Consider using a different container image that includes '{command}'");
            suggestions.Add($"Add a step to install '{command}' before using it");
        }
        else
        {
            suggestions.Add($"Install '{command}' on the host system");
        }

        return new PipelineExecutionException(
            ErrorCodes.CommandNotFound,
            $"Command not found: {command}",
            context,
            suggestions);
    }

    /// <summary>
    /// Creates a PipelineExecutionException for job failures.
    /// </summary>
    /// <param name="jobName">The name of the job that failed.</param>
    /// <param name="failedSteps">The names of the steps that failed.</param>
    /// <param name="pipelineFile">The pipeline file path.</param>
    /// <returns>A new PipelineExecutionException.</returns>
    public static PipelineExecutionException JobFailed(
        string jobName,
        IEnumerable<string> failedSteps,
        string? pipelineFile = null)
    {
        var context = new ErrorContext
        {
            JobName = jobName,
            PipelineFile = pipelineFile
        };

        var steps = string.Join(", ", failedSteps);

        return new PipelineExecutionException(
            ErrorCodes.JobExecutionFailed,
            $"Job '{jobName}' failed. Failed steps: {steps}",
            context,
            [
                "Review the error output from the failed steps",
                "Run with --verbose for more details",
                "Check the step logs for specific error messages"
            ]);
    }

    /// <summary>
    /// Gets suggestions based on an exit code.
    /// </summary>
    /// <param name="exitCode">The exit code.</param>
    /// <returns>A list of suggestions.</returns>
    public static IEnumerable<string> GetExitCodeSuggestions(int exitCode)
    {
        return exitCode switch
        {
            1 => [
                "Exit code 1 indicates a general error",
                "Review the error output above for details",
                "Run with --verbose for more information"
            ],
            2 => [
                "Exit code 2 indicates incorrect usage or command syntax",
                "Check the command arguments and options",
                "Verify the command syntax is correct"
            ],
            126 => [
                "Exit code 126 indicates the command was found but is not executable",
                "Check file permissions",
                "Ensure the file has execute permission (chmod +x)"
            ],
            127 => [
                "Exit code 127 indicates command not found",
                "The tool may not be installed in the container",
                "Consider using a different base image or installing the tool"
            ],
            128 => [
                "Exit code 128 indicates an invalid exit argument",
                "Check your script for invalid exit codes"
            ],
            137 => [
                "Exit code 137 indicates the container was killed (likely out of memory)",
                "Increase available memory for the container",
                "Optimize your process to use less memory",
                "Consider using --memory flag to increase limits"
            ],
            143 => [
                "Exit code 143 indicates the process was terminated (SIGTERM)",
                "The step may have exceeded the timeout",
                "Increase the timeout value if needed"
            ],
            _ when exitCode > 128 => [
                $"Exit code {exitCode} indicates the process was killed by signal {exitCode - 128}",
                "The container may have been forcefully terminated",
                "Check system resources and logs"
            ],
            _ => [
                $"Step failed with exit code {exitCode}",
                "Review the error output for details",
                "Run with --verbose for more information"
            ]
        };
    }
}
