// File: src/PDK.CLI/Program.cs
using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PDK.CLI;
using PDK.CLI.Commands;
using PDK.CLI.Diagnostics;
using PDK.CLI.ErrorHandling;
using PDK.CLI.UI;
using PDK.Core.Diagnostics;
using PDK.Core.Logging;
using PDK.Core.Progress;
using PDK.Core.Models;
using PDK.Providers.AzureDevOps;
using PDK.Providers.GitHub;
using PDK.Runners;
using PDK.Runners.Docker;
using PDK.Runners.StepExecutors;
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

var quietOption = new Option<bool>(
    aliases: ["--quiet", "-q"],
    description: "Suppress step output (show only job/step status)",
    getDefaultValue: () => false);

var interactiveOption = new Option<bool>(
    aliases: ["--interactive", "-i"],
    description: "Run in interactive mode for guided pipeline exploration",
    getDefaultValue: () => false);

runCommand.AddOption(fileOption);
runCommand.AddOption(jobOption);
runCommand.AddOption(stepOption);
runCommand.AddOption(hostOption);
runCommand.AddOption(validateOption);
runCommand.AddOption(verboseOption);
runCommand.AddOption(quietOption);
runCommand.AddOption(interactiveOption);

runCommand.SetHandler(async (file, job, step, host, validate, verbose, quiet, interactive) =>
{
    try
    {
        // Interactive mode takes precedence (REQ-06-020)
        if (interactive)
        {
            var cmd = serviceProvider.GetRequiredService<InteractiveCommand>();
            cmd.File = file;
            Environment.ExitCode = await cmd.ExecuteAsync();
            return;
        }

        var executor = serviceProvider.GetRequiredService<PipelineExecutor>();
        await executor.Execute(new ExecutionOptions
        {
            FilePath = file.FullName,
            JobName = job,
            StepName = step,
            UseDocker = !host,
            ValidateOnly = validate,
            Verbose = verbose,
            Quiet = quiet
        });
    }
    catch (Exception ex)
    {
        var errorFormatter = serviceProvider.GetRequiredService<ErrorFormatter>();
        errorFormatter.DisplayError(ex, verbose);
        Environment.Exit(1);
    }
}, fileOption, jobOption, stepOption, hostOption, validateOption, verboseOption, quietOption, interactiveOption);

// List command
var listCommand = new Command("list", "List jobs in a pipeline");

var listFileOption = new Option<FileInfo?>(
    aliases: ["--file", "-f"],
    description: "Path to the pipeline file (auto-detects if not specified)");

var detailsOption = new Option<bool>(
    aliases: ["--details", "-d"],
    description: "Show detailed step information",
    getDefaultValue: () => false);

var formatOption = new Option<OutputFormat>(
    aliases: ["--format"],
    description: "Output format (table, json, minimal)",
    getDefaultValue: () => OutputFormat.Table);

listCommand.AddOption(listFileOption);
listCommand.AddOption(detailsOption);
listCommand.AddOption(formatOption);

listCommand.SetHandler(async (file, details, format) =>
{
    try
    {
        var cmd = serviceProvider.GetRequiredService<ListCommand>();
        cmd.File = file;
        cmd.Details = details;
        cmd.Format = format;
        Environment.ExitCode = await cmd.ExecuteAsync();
    }
    catch (Exception ex)
    {
        var errorFormatter = serviceProvider.GetRequiredService<ErrorFormatter>();
        errorFormatter.DisplayError(ex, verbose: false);
        Environment.Exit(1);
    }
}, listFileOption, detailsOption, formatOption);

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

        AnsiConsole.MarkupLine($"[green]\u2713[/] Pipeline is valid");
        AnsiConsole.MarkupLine($"  Provider: {pipeline.Provider}");
        AnsiConsole.MarkupLine($"  Jobs: {pipeline.Jobs.Count}");
        AnsiConsole.MarkupLine($"  Total Steps: {pipeline.Jobs.Values.Sum(j => j.Steps.Count)}");
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[red]\u2717 Pipeline validation failed[/]");
        var errorFormatter = serviceProvider.GetRequiredService<ErrorFormatter>();
        errorFormatter.DisplayError(ex, verbose: false);
        Environment.Exit(1);
    }
}, fileOption);

// Version command (REQ-06-040 through REQ-06-043)
var versionCommand = new Command("version", "Show version information");

var versionFullOption = new Option<bool>(
    aliases: ["--full", "-f"],
    description: "Show full system information including Docker status, providers, and executors",
    getDefaultValue: () => false);

var versionFormatOption = new Option<VersionOutputFormat>(
    aliases: ["--format"],
    description: "Output format (human, json)",
    getDefaultValue: () => VersionOutputFormat.Human);

var noUpdateCheckOption = new Option<bool>(
    aliases: ["--no-update-check"],
    description: "Skip checking for updates",
    getDefaultValue: () => false);

versionCommand.AddOption(versionFullOption);
versionCommand.AddOption(versionFormatOption);
versionCommand.AddOption(noUpdateCheckOption);

versionCommand.SetHandler(async (full, format, noUpdate) =>
{
    try
    {
        var cmd = serviceProvider.GetRequiredService<VersionCommand>();
        cmd.Full = full;
        cmd.Format = format;
        cmd.NoUpdateCheck = noUpdate;
        Environment.ExitCode = await cmd.ExecuteAsync();
    }
    catch (Exception ex)
    {
        var errorFormatter = serviceProvider.GetRequiredService<ErrorFormatter>();
        errorFormatter.DisplayError(ex, verbose: false);
        Environment.Exit(1);
    }
}, versionFullOption, versionFormatOption, noUpdateCheckOption);

// Doctor command (REQ-DK-007: Docker Availability Detection)
var doctorCommand = new Command("doctor", "Check system requirements and Docker availability");
doctorCommand.SetHandler(async () =>
{
    AnsiConsole.MarkupLine("[bold]PDK Doctor - System Diagnostics[/]");
    AnsiConsole.WriteLine();

    await AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .StartAsync("Checking Docker availability...", async ctx =>
        {
            try
            {
                var containerManager = new DockerContainerManager();
                var status = await containerManager.GetDockerStatusAsync();

                ctx.Status("Done"); // Update status

                DockerDiagnostics.DisplayDockerStatus(status);

                Environment.ExitCode = status.IsAvailable ? 0 : 1;
            }
            catch (Exception ex)
            {
                ctx.Status("Error occurred");
                AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
                Environment.ExitCode = 1;
            }
        });
});

// Interactive command (REQ-06-020)
var interactiveCommand = new Command("interactive", "Interactive pipeline exploration and execution");
var interactiveFileOption = new Option<FileInfo?>(
    aliases: ["--file", "-f"],
    description: "Path to pipeline file (auto-detects if not specified)");

interactiveCommand.AddOption(interactiveFileOption);
interactiveCommand.SetHandler(async file =>
{
    try
    {
        var cmd = serviceProvider.GetRequiredService<InteractiveCommand>();
        cmd.File = file;
        Environment.ExitCode = await cmd.ExecuteAsync();
    }
    catch (Exception ex)
    {
        var errorFormatter = serviceProvider.GetRequiredService<ErrorFormatter>();
        errorFormatter.DisplayError(ex, verbose: false);
        Environment.Exit(1);
    }
}, interactiveFileOption);

rootCommand.AddCommand(runCommand);
rootCommand.AddCommand(listCommand);
rootCommand.AddCommand(validateCommand);
rootCommand.AddCommand(versionCommand);
rootCommand.AddCommand(doctorCommand);
rootCommand.AddCommand(interactiveCommand);

return await rootCommand.InvokeAsync(args);

static void ConfigureServices(ServiceCollection services)
{
    // Configure logging with Serilog (file + structured logging)
    services.AddLogging(builder =>
    {
        builder.ConfigurePdkLogging();
    });

    // Register UI services
    services.AddSingleton<IAnsiConsole>(AnsiConsole.Console);
    services.AddSingleton<IConsoleOutput>(sp =>
        new ConsoleOutput(sp.GetRequiredService<IAnsiConsole>()));
    services.AddSingleton<IProgressReporter>(sp =>
        new ConsoleProgressReporter(sp.GetRequiredService<IAnsiConsole>()));

    // Register secret masker
    services.AddSingleton<ISecretMasker, SecretMasker>();

    // Register error handling services
    services.AddSingleton<ErrorSuggestionEngine>();
    services.AddSingleton<ErrorFormatter>(sp =>
        new ErrorFormatter(
            sp.GetRequiredService<IAnsiConsole>(),
            sp.GetRequiredService<ISecretMasker>()));

    // Register parsers
    services.AddSingleton<IPipelineParser, GitHubActionsParser>();
    services.AddSingleton<IPipelineParser, AzureDevOpsParser>();

    // Register services
    services.AddSingleton<PipelineParserFactory>();
    services.AddSingleton<IPipelineParserFactory>(sp => sp.GetRequiredService<PipelineParserFactory>());
    services.AddSingleton<PipelineExecutor>();
    services.AddTransient<ListCommand>();
    services.AddTransient<InteractiveCommand>();

    // Register container manager
    services.AddSingleton<PDK.Runners.IContainerManager, DockerContainerManager>();

    // Register step executors
    services.AddSingleton<IStepExecutor, CheckoutStepExecutor>();
    services.AddSingleton<IStepExecutor, ScriptStepExecutor>();
    services.AddSingleton<IStepExecutor, PowerShellStepExecutor>();
    services.AddSingleton<IStepExecutor, DotnetStepExecutor>();
    services.AddSingleton<IStepExecutor, NpmStepExecutor>();
    services.AddSingleton<IStepExecutor, DockerStepExecutor>();

    // Register step executor factory
    services.AddSingleton<StepExecutorFactory>();

    // Register job runner
    services.AddSingleton<PDK.Runners.IJobRunner, DockerJobRunner>();
    services.AddSingleton<IImageMapper, ImageMapper>();

    // Register version command services (REQ-06-040 through REQ-06-043)
    // Registered after parsers, executors, and container manager as SystemInfo depends on them
    services.AddSingleton<ISystemInfo, SystemInfo>();
    services.AddSingleton<IUpdateChecker, UpdateChecker>();
    services.AddTransient<VersionCommand>();
}