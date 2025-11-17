using System.Diagnostics;
using PDK.Core.Models;
using Spectre.Console;

namespace PDK.CLI;

public class PipelineExecutor
{
    private readonly PipelineParserFactory _parserFactory;
    private readonly IJobRunner _runner;

    public PipelineExecutor(PipelineParserFactory parserFactory)
    {
        _parserFactory = parserFactory;
        // TODO: Inject IJobRunner when implemented
        _runner = null!;
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

        // Determine which jobs to run
        var jobsToRun = string.IsNullOrEmpty(options.JobName)
            ? pipeline.Jobs.Values.ToList()
            : [pipeline.Jobs[options.JobName]];

        // Execute jobs
        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                foreach (var job in jobsToRun)
                {
                    var task = ctx.AddTask($"[bold]{job.Name}[/]");
                    
                    AnsiConsole.MarkupLine($"\n[bold blue]Running job:[/] {job.Name}");
                    
                    var stopwatch = Stopwatch.StartNew();
                    
                    // TODO: Actually run the job when IJobRunner is implemented
                    // var result = await _runner.RunJob(job, new RunContext
                    // {
                    //     UseDocker = options.UseDocker,
                    //     SpecificStep = options.StepName
                    // });
                    
                    // Simulate for now
                    await Task.Delay(100);
                    task.Increment(100);
                    
                    stopwatch.Stop();
                    
                    AnsiConsole.MarkupLine($"[green]✓ Job completed in {stopwatch.Elapsed.TotalSeconds:F2}s[/]");
                }
            });

        AnsiConsole.MarkupLine("\n[bold green]Pipeline execution complete![/]");
    }
}