using System.Diagnostics;
using PDK.CLI.Diagnostics;
using PDK.CLI.UI;
using PDK.Core.Models;
using PDK.Core.Progress;
using PDK.Runners;
using Spectre.Console;

namespace PDK.CLI;

/// <summary>
/// Orchestrates pipeline execution, handling parsing, Docker checks, and job running.
/// </summary>
public class PipelineExecutor
{
    private readonly PipelineParserFactory _parserFactory;
    private readonly PDK.Runners.IContainerManager _containerManager;
    private readonly PDK.Runners.IJobRunner _jobRunner;
    private readonly IConsoleOutput _output;
    private readonly IProgressReporter _progressReporter;
    private readonly IAnsiConsole _console;

    /// <summary>
    /// Initializes a new instance of <see cref="PipelineExecutor"/>.
    /// </summary>
    /// <param name="parserFactory">Factory for getting pipeline parsers.</param>
    /// <param name="containerManager">Container manager for Docker operations.</param>
    /// <param name="jobRunner">Job runner for executing jobs.</param>
    /// <param name="output">Console output service.</param>
    /// <param name="progressReporter">Progress reporter for UI feedback.</param>
    /// <param name="console">Spectre.Console instance for rich output.</param>
    public PipelineExecutor(
        PipelineParserFactory parserFactory,
        PDK.Runners.IContainerManager containerManager,
        PDK.Runners.IJobRunner jobRunner,
        IConsoleOutput output,
        IProgressReporter progressReporter,
        IAnsiConsole console)
    {
        _parserFactory = parserFactory ?? throw new ArgumentNullException(nameof(parserFactory));
        _containerManager = containerManager ?? throw new ArgumentNullException(nameof(containerManager));
        _jobRunner = jobRunner ?? throw new ArgumentNullException(nameof(jobRunner));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _progressReporter = progressReporter ?? throw new ArgumentNullException(nameof(progressReporter));
        _console = console ?? throw new ArgumentNullException(nameof(console));
    }

    /// <summary>
    /// Executes a pipeline based on the provided options.
    /// </summary>
    /// <param name="options">Execution options including file path, job selection, etc.</param>
    public async Task Execute(ExecutionOptions options)
    {
        var pipelineStartTime = Stopwatch.StartNew();

        // Configure progress reporter output mode based on options
        ConfigureProgressReporterMode(options);

        // Parse pipeline
        var parser = _parserFactory.GetParser(options.FilePath);
        var pipeline = await parser.ParseFile(options.FilePath);

        if (options.ValidateOnly)
        {
            _output.WriteSuccess("Pipeline validation successful");
            return;
        }

        // Check Docker availability if Docker mode is enabled (REQ-DK-007)
        if (options.UseDocker)
        {
            var dockerStatus = await _containerManager.GetDockerStatusAsync();

            if (!dockerStatus.IsAvailable)
            {
                _output.WriteLine();
                DockerDiagnostics.DisplayQuickError(dockerStatus);
                Environment.Exit(1);
            }

            if (options.Verbose)
            {
                _output.WriteDebug($"Using Docker version {dockerStatus.Version}");
            }
        }

        // Determine which jobs to run
        var jobsToRun = string.IsNullOrEmpty(options.JobName)
            ? pipeline.Jobs.Values.ToList()
            : [pipeline.Jobs[options.JobName]];

        // Execute jobs and collect results for summary
        var workspacePath = Directory.GetCurrentDirectory();
        var allJobsSucceeded = true;
        var totalJobs = jobsToRun.Count;
        var jobResults = new List<JobExecutionResult>();

        for (int i = 0; i < jobsToRun.Count; i++)
        {
            var job = jobsToRun[i];
            var jobNumber = i + 1;

            // Report job start
            await _progressReporter.ReportJobStartAsync(job.Name, jobNumber, totalJobs);

            var stopwatch = Stopwatch.StartNew();

            // Execute the job
            var result = await _jobRunner.RunJobAsync(job, workspacePath);
            jobResults.Add(result);

            stopwatch.Stop();

            // Report job completion
            await _progressReporter.ReportJobCompleteAsync(job.Name, result.Success, stopwatch.Elapsed);

            if (!result.Success)
            {
                allJobsSucceeded = false;
            }
        }

        pipelineStartTime.Stop();

        // Display execution summary (REQ-06-013)
        var summaryData = BuildExecutionSummary(pipeline, jobResults, pipelineStartTime.Elapsed, allJobsSucceeded);
        var summaryDisplay = new ExecutionSummaryDisplay(_console);
        summaryDisplay.Display(summaryData);

        // Display error context for failed steps (REQ-06-014)
        if (!allJobsSucceeded)
        {
            var failedSteps = GetFailedSteps(jobResults);
            if (failedSteps.Any())
            {
                summaryDisplay.DisplayErrorContext(failedSteps);
            }
        }

        _output.WriteLine();
        if (allJobsSucceeded)
        {
            _output.WriteSuccess("Pipeline execution complete!");
        }
        else
        {
            _output.WriteError("Pipeline execution failed!");
            Environment.Exit(1);
        }
    }

    /// <summary>
    /// Configures the progress reporter output mode based on execution options.
    /// </summary>
    private void ConfigureProgressReporterMode(ExecutionOptions options)
    {
        if (_progressReporter is ConsoleProgressReporter consoleReporter)
        {
            if (options.Quiet)
            {
                consoleReporter.SetOutputMode(ConsoleProgressReporter.OutputMode.Quiet);
            }
            else if (options.Verbose)
            {
                consoleReporter.SetOutputMode(ConsoleProgressReporter.OutputMode.Verbose);
            }
        }
    }

    /// <summary>
    /// Builds the execution summary data from job results.
    /// </summary>
    private static ExecutionSummaryData BuildExecutionSummary(
        Pipeline pipeline,
        List<JobExecutionResult> jobResults,
        TimeSpan totalDuration,
        bool allJobsSucceeded)
    {
        var jobSummaries = new List<JobSummary>();
        var totalSteps = 0;
        var successfulSteps = 0;
        var failedSteps = 0;
        var skippedSteps = 0;

        foreach (var jobResult in jobResults)
        {
            var stepSummaries = new List<StepSummary>();

            foreach (var stepResult in jobResult.StepResults)
            {
                totalSteps++;
                if (stepResult.Success)
                {
                    successfulSteps++;
                }
                else
                {
                    failedSteps++;
                }

                stepSummaries.Add(new StepSummary
                {
                    Name = stepResult.StepName,
                    Success = stepResult.Success,
                    Duration = stepResult.Duration,
                    ExitCode = stepResult.Success ? null : stepResult.ExitCode,
                    Output = stepResult.Output,
                    ErrorOutput = stepResult.ErrorOutput
                });
            }

            jobSummaries.Add(new JobSummary
            {
                Name = jobResult.JobName,
                Success = jobResult.Success,
                Duration = jobResult.Duration,
                Steps = stepSummaries
            });
        }

        return new ExecutionSummaryData
        {
            PipelineName = pipeline.Name,
            OverallSuccess = allJobsSucceeded,
            TotalDuration = totalDuration,
            TotalJobs = jobResults.Count,
            SuccessfulJobs = jobResults.Count(j => j.Success),
            FailedJobs = jobResults.Count(j => !j.Success),
            TotalSteps = totalSteps,
            SuccessfulSteps = successfulSteps,
            FailedSteps = failedSteps,
            SkippedSteps = skippedSteps,
            Jobs = jobSummaries
        };
    }

    /// <summary>
    /// Gets all failed steps from job results for error context display.
    /// </summary>
    private static IEnumerable<StepSummary> GetFailedSteps(List<JobExecutionResult> jobResults)
    {
        return jobResults
            .SelectMany(j => j.StepResults)
            .Where(s => !s.Success)
            .Select(s => new StepSummary
            {
                Name = s.StepName,
                Success = s.Success,
                Duration = s.Duration,
                ExitCode = s.ExitCode,
                Output = s.Output,
                ErrorOutput = s.ErrorOutput
            });
    }
}