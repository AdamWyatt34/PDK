// File: src/PDK.CLI/Program.cs
using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PDK.CLI;
using PDK.Core.Models;
using PDK.Providers.AzureDevOps;
using PDK.Providers.GitHub;
using Spectre.Console;

var services = new ServiceCollection();
ConfigureServices(services);
var serviceProvider = services.BuildServiceProvider();

var rootCommand = new RootCommand("PDK - Pipeline Development Kit");

// Run command
var runCommand = new Command("run", "Run a pipeline locally");
var fileOption = new Option<FileInfo>(
    aliases: ["--file", "-f"],
    description: "Path to the pipeline file")
{
    IsRequired = true
};
fileOption.AddValidator(result =>
{
    var file = result.GetValueForOption(fileOption);
    if (file?.Exists == false)
    {
        result.ErrorMessage = $"File not found: {file.FullName}";
    }
});

var jobOption = new Option<string?>(
    aliases: ["--job", "-j"],
    description: "Specific job to run (runs all if not specified)");

var stepOption = new Option<string?>(
    aliases: ["--step", "-s"],
    description: "Specific step to run within a job");

var hostOption = new Option<bool>(
    aliases: ["--host"],
    description: "Run directly on host machine instead of Docker",
    getDefaultValue: () => false);

var validateOption = new Option<bool>(
    aliases: ["--validate"],
    description: "Validate pipeline without executing",
    getDefaultValue: () => false);

var verboseOption = new Option<bool>(
    aliases: ["--verbose", "-v"],
    description: "Enable verbose logging",
    getDefaultValue: () => false);

runCommand.AddOption(fileOption);
runCommand.AddOption(jobOption);
runCommand.AddOption(stepOption);
runCommand.AddOption(hostOption);
runCommand.AddOption(validateOption);
runCommand.AddOption(verboseOption);

runCommand.SetHandler(async (file, job, step, host, validate, verbose) =>
{
    try
    {
        var executor = serviceProvider.GetRequiredService<PipelineExecutor>();
        await executor.Execute(new ExecutionOptions
        {
            FilePath = file.FullName,
            JobName = job,
            StepName = step,
            UseDocker = !host,
            ValidateOnly = validate,
            Verbose = verbose
        });
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
        if (verbose)
        {
            AnsiConsole.WriteException(ex);
        }
        Environment.Exit(1);
    }
}, fileOption, jobOption, stepOption, hostOption, validateOption, verboseOption);

// List command
var listCommand = new Command("list", "List jobs in a pipeline");
listCommand.AddOption(fileOption);

listCommand.SetHandler(async file =>
{
    try
    {
        var parserFactory = serviceProvider.GetRequiredService<PipelineParserFactory>();
        var parser = parserFactory.GetParser(file.FullName);
        var pipeline = await parser.ParseFile(file.FullName);

        AnsiConsole.MarkupLine($"[bold]Pipeline:[/] {pipeline.Name}");
        AnsiConsole.MarkupLine($"[bold]Provider:[/] {pipeline.Provider}");
        AnsiConsole.WriteLine();

        var table = new Table();
        table.AddColumn("Job ID");
        table.AddColumn("Name");
        table.AddColumn("Runs On");
        table.AddColumn("Steps");

        foreach (var (jobId, jobDef) in pipeline.Jobs)
        {
            table.AddRow(
                jobId,
                jobDef.Name,
                jobDef.RunsOn,
                jobDef.Steps.Count.ToString()
            );
        }

        AnsiConsole.Write(table);
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
        Environment.Exit(1);
    }
}, fileOption);

// Validate command
var validateCommand = new Command("validate", "Validate a pipeline file");
validateCommand.AddOption(fileOption);

validateCommand.SetHandler(async file =>
{
    try
    {
        var parserFactory = serviceProvider.GetRequiredService<PipelineParserFactory>();
        var parser = parserFactory.GetParser(file.FullName);
        var pipeline = await parser.ParseFile(file.FullName);

        AnsiConsole.MarkupLine($"[green]✓[/] Pipeline is valid");
        AnsiConsole.MarkupLine($"  Provider: {pipeline.Provider}");
        AnsiConsole.MarkupLine($"  Jobs: {pipeline.Jobs.Count}");
        AnsiConsole.MarkupLine($"  Total Steps: {pipeline.Jobs.Values.Sum(j => j.Steps.Count)}");
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[red]✗ Pipeline validation failed[/]");
        AnsiConsole.MarkupLine($"  {ex.Message}");
        Environment.Exit(1);
    }
}, fileOption);

// Version command
var versionCommand = new Command("version", "Show version information");
versionCommand.SetHandler(() =>
{
    var version = typeof(Program).Assembly.GetName().Version;
    AnsiConsole.MarkupLine($"[bold]PDK[/] v{version}");
    AnsiConsole.MarkupLine("Pipeline Development Kit");
});

rootCommand.AddCommand(runCommand);
rootCommand.AddCommand(listCommand);
rootCommand.AddCommand(validateCommand);
rootCommand.AddCommand(versionCommand);

return await rootCommand.InvokeAsync(args);

static void ConfigureServices(ServiceCollection services)
{
    services.AddLogging(builder =>
    {
        builder.AddConsole();
    });

    // Register parsers
    services.AddSingleton<IPipelineParser, GitHubActionsParser>();
    services.AddSingleton<IPipelineParser, AzureDevOpsParser>();

    // Register services
    services.AddSingleton<PipelineParserFactory>();
    services.AddSingleton<PipelineExecutor>();

    // TODO: Register runners as they're implemented
    // services.AddSingleton<IJobRunner, DockerRunner>();
}