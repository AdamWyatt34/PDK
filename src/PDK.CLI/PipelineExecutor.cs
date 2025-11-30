using System.Diagnostics;
using PDK.CLI.Diagnostics;
using PDK.Core.Models;
using PDK.Runners;
using Spectre.Console;

namespace PDK.CLI;

public class PipelineExecutor
{
    private readonly PipelineParserFactory _parserFactory;
    private readonly PDK.Runners.IContainerManager _containerManager;
    private readonly PDK.Runners.IJobRunner _jobRunner;

    public PipelineExecutor(
        PipelineParserFactory parserFactory,
        PDK.Runners.IContainerManager containerManager,
        PDK.Runners.IJobRunner jobRunner)
    {
        _parserFactory = parserFactory;
        _containerManager = containerManager;
        _jobRunner = jobRunner;
    }

    public async Task Execute(ExecutionOptions options)
    {
        // Parse pipeline
        var parser = _parserFactory.GetParser(options.FilePath);
        var pipeline = await parser.ParseFile(options.FilePath);

        if (options.ValidateOnly)
        {
            AnsiConsole.MarkupLine("[green]✓ Pipeline validation successful[/]");
            return;
        }

        // Check Docker availability if Docker mode is enabled (REQ-DK-007)
        if (options.UseDocker)
        {
            var dockerStatus = await _containerManager.GetDockerStatusAsync();

            if (!dockerStatus.IsAvailable)
            {
                AnsiConsole.WriteLine();
                DockerDiagnostics.DisplayQuickError(dockerStatus);
                Environment.Exit(1);
            }

            if (options.Verbose)
            {
                AnsiConsole.MarkupLine($"[dim]Using Docker version {dockerStatus.Version}[/]");
            }
        }

        // Determine which jobs to run
        var jobsToRun = string.IsNullOrEmpty(options.JobName)
            ? pipeline.Jobs.Values.ToList()
            : [pipeline.Jobs[options.JobName]];

        // Execute jobs
        var workspacePath = Directory.GetCurrentDirectory();
        var allJobsSucceeded = true;

        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                foreach (var job in jobsToRun)
                {
                    var task = ctx.AddTask($"[bold]{job.Name}[/]");

                    AnsiConsole.MarkupLine($"\n[bold blue]Running job:[/] {job.Name}");

                    var stopwatch = Stopwatch.StartNew();

                    // Execute the job
                    var result = await _jobRunner.RunJobAsync(job, workspacePath);

                    task.Increment(100);
                    stopwatch.Stop();

                    if (result.Success)
                    {
                        AnsiConsole.MarkupLine($"[green]✓ Job completed in {stopwatch.Elapsed.TotalSeconds:F2}s[/]");
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[red]✗ Job failed in {stopwatch.Elapsed.TotalSeconds:F2}s[/]");
                        allJobsSucceeded = false;
                    }
                }
            });

        if (allJobsSucceeded)
        {
            AnsiConsole.MarkupLine("\n[bold green]Pipeline execution complete![/]");
        }
        else
        {
            AnsiConsole.MarkupLine("\n[bold red]Pipeline execution failed![/]");
            Environment.Exit(1);
        }
    }
}