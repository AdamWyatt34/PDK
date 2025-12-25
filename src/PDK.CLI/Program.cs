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
using PDK.CLI.Runners;
using PDK.Core.Configuration;
using PDK.Core.Docker;
using PDK.Core.Runners;
using PDK.Core.Variables;
using PDK.Core.Secrets;
using Spectre.Console;
using System.Text;

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

var dockerOption = new Option<bool>(
    aliases: ["--docker"],
    description: "Force Docker execution mode (fail if Docker unavailable)",
    getDefaultValue: () => false);

var runnerOption = new Option<string?>(
    aliases: ["--runner"],
    description: "Runner type: 'docker', 'host', or 'auto' (default)");
runnerOption.AddValidator(result =>
{
    var value = result.GetValueForOption(runnerOption);
    if (value != null && value != "docker" && value != "host" && value != "auto")
    {
        result.ErrorMessage = "Runner must be 'docker', 'host', or 'auto'";
    }
});

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

var configOption = new Option<string?>(
    aliases: ["--config", "-c"],
    description: "Path to configuration file (auto-discovers if not specified)");

var varOption = new Option<string[]>(
    aliases: ["--var"],
    description: "Set variable (NAME=VALUE, can be repeated)")
{
    AllowMultipleArgumentsPerToken = true
};

var varFileOption = new Option<FileInfo?>(
    aliases: ["--var-file"],
    description: "Load variables from JSON file");
varFileOption.AddValidator(result =>
{
    var file = result.GetValueForOption(varFileOption);
    if (file?.Exists == false)
    {
        result.ErrorMessage = $"Variable file not found: {file.FullName}";
    }
});

var secretOption = new Option<string[]>(
    aliases: ["--secret"],
    description: "Set secret (NAME=VALUE, WARNING: visible in process list)")
{
    AllowMultipleArgumentsPerToken = true
};

// Performance optimization options (Sprint 10 Phase 3)
var noReuseOption = new Option<bool>(
    aliases: ["--no-reuse"],
    description: "Disable container reuse (create new container per step)",
    getDefaultValue: () => false);

var noCacheOption = new Option<bool>(
    aliases: ["--no-cache"],
    description: "Disable Docker image caching (always pull images)",
    getDefaultValue: () => false);

var parallelOption = new Option<bool>(
    aliases: ["--parallel"],
    description: "Enable parallel step execution for independent steps",
    getDefaultValue: () => false);

var maxParallelOption = new Option<int>(
    aliases: ["--max-parallel"],
    description: "Maximum number of steps to run in parallel (default: 4)",
    getDefaultValue: () => 4);
maxParallelOption.AddValidator(result =>
{
    var value = result.GetValueForOption(maxParallelOption);
    if (value < 1 || value > 16)
    {
        result.ErrorMessage = "Max parallel must be between 1 and 16";
    }
});

var metricsOption = new Option<bool>(
    aliases: ["--metrics"],
    description: "Show performance metrics after execution",
    getDefaultValue: () => false);

runCommand.AddOption(fileOption);
runCommand.AddOption(jobOption);
runCommand.AddOption(stepOption);
runCommand.AddOption(hostOption);
runCommand.AddOption(dockerOption);
runCommand.AddOption(runnerOption);
runCommand.AddOption(validateOption);
runCommand.AddOption(verboseOption);
runCommand.AddOption(quietOption);
runCommand.AddOption(interactiveOption);
runCommand.AddOption(configOption);
runCommand.AddOption(varOption);
runCommand.AddOption(varFileOption);
runCommand.AddOption(secretOption);
runCommand.AddOption(noReuseOption);
runCommand.AddOption(noCacheOption);
runCommand.AddOption(parallelOption);
runCommand.AddOption(maxParallelOption);
runCommand.AddOption(metricsOption);

runCommand.SetHandler(async context =>
{
    var file = context.ParseResult.GetValueForOption(fileOption)!;
    var job = context.ParseResult.GetValueForOption(jobOption);
    var step = context.ParseResult.GetValueForOption(stepOption);
    var host = context.ParseResult.GetValueForOption(hostOption);
    var docker = context.ParseResult.GetValueForOption(dockerOption);
    var runner = context.ParseResult.GetValueForOption(runnerOption);
    var validate = context.ParseResult.GetValueForOption(validateOption);
    var verbose = context.ParseResult.GetValueForOption(verboseOption);
    var quiet = context.ParseResult.GetValueForOption(quietOption);
    var interactive = context.ParseResult.GetValueForOption(interactiveOption);
    var configPath = context.ParseResult.GetValueForOption(configOption);
    var vars = context.ParseResult.GetValueForOption(varOption) ?? [];
    var varFile = context.ParseResult.GetValueForOption(varFileOption);
    var secrets = context.ParseResult.GetValueForOption(secretOption) ?? [];
    var noReuse = context.ParseResult.GetValueForOption(noReuseOption);
    var noCache = context.ParseResult.GetValueForOption(noCacheOption);
    var parallel = context.ParseResult.GetValueForOption(parallelOption);
    var maxParallel = context.ParseResult.GetValueForOption(maxParallelOption);
    var showMetrics = context.ParseResult.GetValueForOption(metricsOption);

    try
    {
        // Validate conflicting runner options
        if (host && docker)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Cannot specify both --host and --docker flags. Choose one.");
            Environment.Exit(1);
            return;
        }

        // Determine runner type from CLI options
        var runnerType = DetermineRunnerType(host, docker, runner);

        // Interactive mode takes precedence (REQ-06-020)
        if (interactive)
        {
            var cmd = serviceProvider.GetRequiredService<InteractiveCommand>();
            cmd.File = file;
            Environment.ExitCode = await cmd.ExecuteAsync();
            return;
        }

        // Parse NAME=VALUE arrays into dictionaries
        var cliVariables = ParseKeyValuePairs(vars);
        var cliSecrets = ParseKeyValuePairs(secrets);

        // Warn if secrets passed via CLI
        if (cliSecrets.Count > 0)
        {
            AnsiConsole.MarkupLine("[yellow]Warning:[/] Secrets passed via --secret are visible in process lists.");
            AnsiConsole.MarkupLine("[yellow]Recommendation:[/] Use 'pdk secret set NAME' or PDK_SECRET_* environment variables.");
        }

        var executor = serviceProvider.GetRequiredService<PipelineExecutor>();
        await executor.Execute(new ExecutionOptions
        {
            FilePath = file.FullName,
            JobName = job,
            StepName = step,
            RunnerType = runnerType,
            ValidateOnly = validate,
            Verbose = verbose,
            Quiet = quiet,
            ConfigPath = configPath,
            CliVariables = cliVariables,
            VarFilePath = varFile?.FullName,
            CliSecrets = cliSecrets,
            NoReuseContainers = noReuse,
            NoCacheImages = noCache,
            ParallelSteps = parallel,
            MaxParallelism = maxParallel,
            ShowMetrics = showMetrics || verbose  // Show metrics when verbose is enabled
        });
    }
    catch (Exception ex)
    {
        var errorFormatter = serviceProvider.GetRequiredService<ErrorFormatter>();
        errorFormatter.DisplayError(ex, verbose);
        Environment.Exit(1);
    }
});

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

// Secret command (Sprint 7)
var secretCommand = new Command("secret", "Manage secrets");

// pdk secret set NAME [--value VALUE] [--stdin]
var secretSetCommand = new Command("set", "Set a secret value");
var secretNameArg = new Argument<string>("name", "Secret name");
var secretValueOption = new Option<string?>("--value", "Secret value (WARNING: visible in process list)");
var secretStdinOption = new Option<bool>("--stdin", "Read value from stdin");
secretSetCommand.AddArgument(secretNameArg);
secretSetCommand.AddOption(secretValueOption);
secretSetCommand.AddOption(secretStdinOption);

secretSetCommand.SetHandler(async (name, valueOpt, useStdin) =>
{
    try
    {
        var manager = serviceProvider.GetRequiredService<ISecretManager>();
        string value;

        if (useStdin)
        {
            // Read from stdin (for piping: echo 'secret' | pdk secret set NAME --stdin)
            value = await Console.In.ReadToEndAsync();
            value = value.TrimEnd('\r', '\n');
        }
        else if (!string.IsNullOrEmpty(valueOpt))
        {
            // Use --value option (with warning)
            AnsiConsole.MarkupLine("[yellow]Warning:[/] Value provided via CLI is visible in process list.");
            value = valueOpt;
        }
        else
        {
            // Interactive mode (recommended)
            AnsiConsole.MarkupLine($"Enter value for [blue]{name}[/]:");
            value = ReadSecretFromConsole();
        }

        await manager.SetSecretAsync(name, value);
        AnsiConsole.MarkupLine($"[green]\u2713[/] Secret '{name}' saved");
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
        Environment.ExitCode = 1;
    }
}, secretNameArg, secretValueOption, secretStdinOption);

// pdk secret list
var secretListCommand = new Command("list", "List secret names");
secretListCommand.SetHandler(async () =>
{
    try
    {
        var manager = serviceProvider.GetRequiredService<ISecretManager>();
        var names = await manager.ListSecretNamesAsync();
        if (!names.Any())
        {
            AnsiConsole.MarkupLine("[dim]No secrets stored[/]");
            return;
        }
        foreach (var name in names)
        {
            AnsiConsole.WriteLine(name);
        }
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
        Environment.ExitCode = 1;
    }
});

// pdk secret delete NAME
var secretDeleteCommand = new Command("delete", "Delete a secret");
var deleteNameArg = new Argument<string>("name", "Secret name to delete");
secretDeleteCommand.AddArgument(deleteNameArg);
secretDeleteCommand.SetHandler(async (name) =>
{
    try
    {
        var manager = serviceProvider.GetRequiredService<ISecretManager>();
        if (!await manager.SecretExistsAsync(name))
        {
            AnsiConsole.MarkupLine($"[yellow]Secret '{name}' not found[/]");
            Environment.ExitCode = 1;
            return;
        }
        await manager.DeleteSecretAsync(name);
        AnsiConsole.MarkupLine($"[green]\u2713[/] Secret '{name}' deleted");
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[red]Error:[/] {ex.Message}");
        Environment.ExitCode = 1;
    }
}, deleteNameArg);

secretCommand.AddCommand(secretSetCommand);
secretCommand.AddCommand(secretListCommand);
secretCommand.AddCommand(secretDeleteCommand);

rootCommand.AddCommand(runCommand);
rootCommand.AddCommand(listCommand);
rootCommand.AddCommand(validateCommand);
rootCommand.AddCommand(versionCommand);
rootCommand.AddCommand(doctorCommand);
rootCommand.AddCommand(interactiveCommand);
rootCommand.AddCommand(secretCommand);

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

    // Register configuration services (Sprint 7)
    services.AddSingleton<ConfigurationValidator>();
    services.AddSingleton<IConfigurationLoader, ConfigurationLoader>();
    services.AddSingleton<IConfigurationMerger, ConfigurationMerger>();

    // Register variable services (Sprint 7)
    services.AddSingleton<IBuiltInVariables, BuiltInVariables>();
    services.AddSingleton<IVariableResolver, VariableResolver>();
    services.AddSingleton<IVariableExpander, VariableExpander>();

    // Register secret services (Sprint 7)
    services.AddSingleton<ISecretEncryption, SecretEncryption>();
    services.AddSingleton<SecretStorage>();
    services.AddSingleton<ISecretManager>(sp => new SecretManager(
        sp.GetRequiredService<ISecretEncryption>(),
        sp.GetRequiredService<SecretStorage>(),
        sp.GetRequiredService<ISecretMasker>()));
    services.AddSingleton<ISecretDetector, SecretDetector>();

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

    // Register artifact services (Sprint 8)
    services.AddSingleton<PDK.Core.Artifacts.IFileSelector, PDK.Core.Artifacts.FileSelector>();
    services.AddSingleton<PDK.Core.Artifacts.IArtifactCompressor, PDK.Core.Artifacts.ArtifactCompressor>();
    services.AddSingleton<PDK.Core.Artifacts.IArtifactManager, PDK.Core.Artifacts.ArtifactManager>();

    // Register step executors
    services.AddSingleton<IStepExecutor, CheckoutStepExecutor>();
    services.AddSingleton<IStepExecutor, ScriptStepExecutor>();
    services.AddSingleton<IStepExecutor, PowerShellStepExecutor>();
    services.AddSingleton<IStepExecutor, DotnetStepExecutor>();
    services.AddSingleton<IStepExecutor, NpmStepExecutor>();
    services.AddSingleton<IStepExecutor, DockerStepExecutor>();
    services.AddSingleton<IStepExecutor, UploadArtifactExecutor>();
    services.AddSingleton<IStepExecutor, DownloadArtifactExecutor>();

    // Register step executor factory (Docker)
    services.AddSingleton<StepExecutorFactory>();

    // Register host step executors (Sprint 10 - Host Mode)
    services.AddSingleton<IHostStepExecutor, HostScriptExecutor>();
    services.AddSingleton<IHostStepExecutor, HostCheckoutExecutor>();
    services.AddSingleton<IHostStepExecutor, HostDotnetExecutor>();
    services.AddSingleton<IHostStepExecutor, HostNpmExecutor>();
    services.AddSingleton<HostStepExecutorFactory>();

    // Register process executor for host mode
    services.AddSingleton<IProcessExecutor, ProcessExecutor>();

    // Register both job runners as concrete types (Sprint 10)
    services.AddSingleton<DockerJobRunner>();
    services.AddSingleton<HostJobRunner>();
    services.AddSingleton<IImageMapper, ImageMapper>();

    // Register Docker detection with caching (Sprint 10)
    services.AddSingleton<IDockerDetector, DockerDetector>();

    // Register runner selection services (Sprint 10)
    services.AddSingleton<IRunnerSelector, RunnerSelector>();
    services.AddSingleton<IRunnerFactory, RunnerFactory>();

    // Register version command services (REQ-06-040 through REQ-06-043)
    // Registered after parsers, executors, and container manager as SystemInfo depends on them
    services.AddSingleton<ISystemInfo, SystemInfo>();
    services.AddSingleton<IUpdateChecker, UpdateChecker>();
    services.AddTransient<VersionCommand>();
}

/// <summary>
/// Parses an array of NAME=VALUE strings into a dictionary.
/// </summary>
static Dictionary<string, string> ParseKeyValuePairs(string[]? pairs)
{
    var result = new Dictionary<string, string>();
    foreach (var pair in pairs ?? [])
    {
        var eqIndex = pair.IndexOf('=');
        if (eqIndex > 0)
        {
            var key = pair[..eqIndex];
            var value = pair[(eqIndex + 1)..];
            result[key] = value;
        }
    }
    return result;
}

/// <summary>
/// Determines the runner type from CLI options.
/// </summary>
static RunnerType DetermineRunnerType(bool host, bool docker, string? runner)
{
    // Explicit flags take precedence
    if (host) return RunnerType.Host;
    if (docker) return RunnerType.Docker;

    // --runner option
    if (!string.IsNullOrEmpty(runner))
    {
        return runner.ToLowerInvariant() switch
        {
            "host" => RunnerType.Host,
            "docker" => RunnerType.Docker,
            "auto" => RunnerType.Auto,
            _ => RunnerType.Auto
        };
    }

    // Default to auto
    return RunnerType.Auto;
}

/// <summary>
/// Reads a secret value from the console with masked input.
/// </summary>
static string ReadSecretFromConsole()
{
    var value = new StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter) break;
        if (key.Key == ConsoleKey.Backspace && value.Length > 0)
            value.Length--;
        else if (!char.IsControl(key.KeyChar))
            value.Append(key.KeyChar);
    }
    Console.WriteLine();
    return value.ToString();
}