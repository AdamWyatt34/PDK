// File: src/PDK.CLI/Program.cs
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PDK.CLI;
using PDK.CLI.Commands;
using PDK.CLI.Diagnostics;
using PDK.CLI.ErrorHandling;
using PDK.CLI.UI;
using PDK.CLI.WatchMode;
using PDK.CLI.Logging;
using PDK.Cli.Filtering;
using PDK.Core.Diagnostics;
using PDK.Core.Logging;
using PDK.Core.Performance;
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

// Spectre.Console renders nothing when the detected terminal width is not positive
// (e.g. TERM=linux with redirected output makes .NET report BufferWidth = -1).
if (AnsiConsole.Profile.Width <= 0)
{
    AnsiConsole.Profile.Width = 80;
}

var rootCommand = new RootCommand("PDK - Pipeline Development Kit");

// Run command
var runCommand = new Command("run", "Run a pipeline locally");
var fileOption = new Option<FileInfo?>(
    aliases: ["--file", "-f"],
    description: "Path to the pipeline file (auto-detects .github/workflows/*.yml or azure-pipelines.yml if not specified)");

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
    var value = result.GetValueForOption(runnerOption)?.ToLowerInvariant();
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

// Structured logging options (Sprint 11 - REQ-11-005)
var traceOption = new Option<bool>(
    aliases: ["--trace"],
    description: "Enable trace-level logging (most verbose)",
    getDefaultValue: () => false);

var silentOption = new Option<bool>(
    aliases: ["--silent"],
    description: "Show only errors (suppress all other output)",
    getDefaultValue: () => false);

var logFileOption = new Option<string?>(
    aliases: ["--log-file"],
    description: "Path to write text log file");

var logJsonOption = new Option<string?>(
    aliases: ["--log-json"],
    description: "Path to write JSON-formatted log file");

var noRedactOption = new Option<bool>(
    aliases: ["--no-redact"],
    description: "Disable secret redaction in logs (WARNING: may expose secrets)",
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
// Accepted for compatibility only: every job already gets a fresh container.
var noReuseOption = new Option<bool>(
    aliases: ["--no-reuse"],
    description: "No effect: every job runs in a fresh container",
    getDefaultValue: () => false)
{
    IsHidden = true
};

var noCacheOption = new Option<bool>(
    aliases: ["--no-cache"],
    description: "Disable Docker image caching (always pull images)",
    getDefaultValue: () => false);

var parallelOption = new Option<bool>(
    aliases: ["--parallel"],
    description: "Run independent jobs concurrently (dependencies still run first); output lines are prefixed with the job name",
    getDefaultValue: () => false);

var maxParallelOption = new Option<int>(
    aliases: ["--max-parallel"],
    description: "Maximum number of jobs to run at the same time with --parallel (default: 4)",
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

// Watch mode options (Sprint 11 - REQ-11-001)
var watchOption = new Option<bool>(
    aliases: ["--watch", "-w"],
    description: "Watch for file changes and re-execute pipeline automatically",
    getDefaultValue: () => false);

var watchDebounceOption = new Option<int>(
    aliases: ["--watch-debounce"],
    description: "Debounce period in milliseconds (default: 500)",
    getDefaultValue: () => 500);
watchDebounceOption.AddValidator(result =>
{
    var value = result.GetValueForOption(watchDebounceOption);
    if (value < 100 || value > 10000)
    {
        result.ErrorMessage = "Watch debounce must be between 100 and 10000 milliseconds";
    }
});

var watchClearOption = new Option<bool>(
    aliases: ["--watch-clear"],
    description: "Clear terminal between watch mode runs",
    getDefaultValue: () => false);

// Dry-run options (Sprint 11 - REQ-11-003)
var dryRunOption = new Option<bool>(
    aliases: ["--dry-run"],
    description: "Validate pipeline and show execution plan without executing",
    getDefaultValue: () => false);

var dryRunJsonOption = new Option<string?>(
    aliases: ["--dry-run-json"],
    description: "Output dry-run results to JSON file (implies --dry-run)");

// Step filtering options (Sprint 11 - REQ-11-007)
var filterStepOption = new Option<string[]>(
    aliases: ["--step-filter"],
    description: "Run specific step(s) by name (can be repeated, case-insensitive)")
{
    AllowMultipleArgumentsPerToken = true
};

var filterStepIndexOption = new Option<string[]>(
    aliases: ["--step-index"],
    description: "Run specific step(s) by index (e.g., '3', '1,3,5', '2-5', '1,3-5,7')")
{
    AllowMultipleArgumentsPerToken = true
};

var filterStepRangeOption = new Option<string[]>(
    aliases: ["--step-range"],
    description: "Run range of steps (e.g., '1-5' or 'Build-Test')")
{
    AllowMultipleArgumentsPerToken = true
};

var skipStepOption = new Option<string[]>(
    aliases: ["--skip-step"],
    description: "Skip specific step(s) by name (takes precedence over include filters)")
{
    AllowMultipleArgumentsPerToken = true
};

var includeDepsOption = new Option<bool>(
    aliases: ["--include-dependencies"],
    description: "Automatically include dependencies of selected steps",
    getDefaultValue: () => false);

// Filter preview options (Sprint 11 - REQ-11-008)
var previewFilterOption = new Option<bool>(
    aliases: ["--preview-filter"],
    description: "Preview which steps will run and exit without execution",
    getDefaultValue: () => false);

var confirmFilterOption = new Option<bool>(
    aliases: ["--confirm"],
    description: "Show filter preview and confirm before execution",
    getDefaultValue: () => false);

var filterPresetOption = new Option<string?>(
    aliases: ["--preset"],
    description: "Load filter preset from configuration file");

var noDepsOption = new Option<bool>(
    aliases: ["--no-deps"],
    description: "With --job: run only the selected job, not the jobs it depends on",
    getDefaultValue: () => false);

var strictOption = new Option<bool>(
    aliases: ["--strict"],
    description: "Fail the job when it contains an action or task PDK cannot run (default: skip with a warning)",
    getDefaultValue: () => false);

var eventOption = new Option<string>(
    aliases: ["--event"],
    description: "Event name presented to the pipeline (github.event_name / Build.Reason), e.g. push, pull_request",
    getDefaultValue: () => "push");

var paramOption = new Option<string[]>(
    aliases: ["--param", "--input"],
    description: "Parameter or input value as NAME=VALUE (Azure 'parameters', GitHub 'inputs'); repeatable")
{
    AllowMultipleArgumentsPerToken = false
};

var keepContainersOption = new Option<bool>(
    aliases: ["--keep-containers"],
    description: "Keep job containers after the run for inspection (Docker mode)",
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
runCommand.AddOption(traceOption);
runCommand.AddOption(silentOption);
runCommand.AddOption(logFileOption);
runCommand.AddOption(logJsonOption);
runCommand.AddOption(noRedactOption);
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
runCommand.AddOption(watchOption);
runCommand.AddOption(watchDebounceOption);
runCommand.AddOption(watchClearOption);
runCommand.AddOption(dryRunOption);
runCommand.AddOption(dryRunJsonOption);
runCommand.AddOption(filterStepOption);
runCommand.AddOption(filterStepIndexOption);
runCommand.AddOption(filterStepRangeOption);
runCommand.AddOption(skipStepOption);
runCommand.AddOption(includeDepsOption);
runCommand.AddOption(previewFilterOption);
runCommand.AddOption(confirmFilterOption);
runCommand.AddOption(filterPresetOption);
runCommand.AddOption(noDepsOption);
runCommand.AddOption(strictOption);
runCommand.AddOption(eventOption);
runCommand.AddOption(keepContainersOption);
runCommand.AddOption(paramOption);

runCommand.SetHandler(async context =>
{
    var cancellationToken = context.GetCancellationToken();
    var fileArg = context.ParseResult.GetValueForOption(fileOption);
    var job = context.ParseResult.GetValueForOption(jobOption);
    var step = context.ParseResult.GetValueForOption(stepOption);
    var host = context.ParseResult.GetValueForOption(hostOption);
    var docker = context.ParseResult.GetValueForOption(dockerOption);
    var runner = context.ParseResult.GetValueForOption(runnerOption);
    var validate = context.ParseResult.GetValueForOption(validateOption);
    var verbose = context.ParseResult.GetValueForOption(verboseOption);
    var quiet = context.ParseResult.GetValueForOption(quietOption);
    var trace = context.ParseResult.GetValueForOption(traceOption);
    var silent = context.ParseResult.GetValueForOption(silentOption);
    var logFile = context.ParseResult.GetValueForOption(logFileOption);
    var logJson = context.ParseResult.GetValueForOption(logJsonOption);
    var noRedact = context.ParseResult.GetValueForOption(noRedactOption);
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
    var watch = context.ParseResult.GetValueForOption(watchOption);
    var watchDebounce = context.ParseResult.GetValueForOption(watchDebounceOption);
    var watchClear = context.ParseResult.GetValueForOption(watchClearOption);
    var dryRun = context.ParseResult.GetValueForOption(dryRunOption);
    var dryRunJson = context.ParseResult.GetValueForOption(dryRunJsonOption);
    var filterSteps = context.ParseResult.GetValueForOption(filterStepOption) ?? [];
    var filterStepIndices = context.ParseResult.GetValueForOption(filterStepIndexOption) ?? [];
    var filterStepRanges = context.ParseResult.GetValueForOption(filterStepRangeOption) ?? [];
    var skipSteps = context.ParseResult.GetValueForOption(skipStepOption) ?? [];
    var includeDeps = context.ParseResult.GetValueForOption(includeDepsOption);
    var previewFilter = context.ParseResult.GetValueForOption(previewFilterOption);
    var confirmFilter = context.ParseResult.GetValueForOption(confirmFilterOption);
    var filterPreset = context.ParseResult.GetValueForOption(filterPresetOption);
    var noDeps = context.ParseResult.GetValueForOption(noDepsOption);
    var strict = context.ParseResult.GetValueForOption(strictOption);
    var eventName = context.ParseResult.GetValueForOption(eventOption) ?? "push";
    var keepContainers = context.ParseResult.GetValueForOption(keepContainersOption);
    var paramValues = context.ParseResult.GetValueForOption(paramOption) ?? [];

    // --dry-run-json implies --dry-run
    if (!string.IsNullOrEmpty(dryRunJson))
    {
        dryRun = true;
    }

    // --step is shorthand for a single --step-filter entry
    if (!string.IsNullOrWhiteSpace(step))
    {
        filterSteps = [.. filterSteps, step];
    }

    var located = PipelineFileLocator.Resolve(fileArg, AnsiConsole.Console, "run");
    if (located.File == null)
    {
        context.ExitCode = located.ExitCode;
        return;
    }
    var file = located.File;

    try
    {
        // Validate conflicting runner options
        if (host && docker)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Cannot specify both --host and --docker flags. Choose one.");
            context.ExitCode = ExitCodes.InvalidArguments;
            return;
        }

        // Validate conflicting verbosity options (REQ-11-005)
        var verbosityCount = (trace ? 1 : 0) + (verbose ? 1 : 0) + (quiet ? 1 : 0) + (silent ? 1 : 0);
        if (verbosityCount > 1)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Cannot specify multiple verbosity flags. Choose one of: --trace, --verbose, --quiet, --silent.");
            context.ExitCode = ExitCodes.InvalidArguments;
            return;
        }

        // Warn about --no-redact security implications
        if (noRedact)
        {
            AnsiConsole.MarkupLine("[yellow]WARNING:[/] Secret redaction is disabled (--no-redact).");
            AnsiConsole.MarkupLine("[yellow]         [/] Sensitive data may appear in logs and console output.");
        }

        // Build logging options from the configuration's logging section and the CLI flags
        // (flags win) and apply them to the logging pipeline
        var loggingConfig = (await serviceProvider.GetRequiredService<IConfigurationLoader>().LoadAsync(configPath))?.Logging;
        var loggingOptions = LoggingOptionsBuilder.FromCliFlagsAndConfiguration(
            loggingConfig,
            verbose: verbose,
            trace: trace,
            quiet: quiet,
            silent: silent,
            logFile: logFile,
            logJson: logJson,
            noRedact: noRedact);
        serviceProvider.GetRequiredService<PdkLoggingController>().Apply(loggingOptions);

        // Determine runner type from CLI options
        var runnerType = DetermineRunnerType(host, docker, runner);

        // Validate mode conflicts
        if (dryRun && (watch || interactive))
        {
            AnsiConsole.MarkupLine("[red]Error:[/] --dry-run cannot be used with --watch or --interactive.");
            context.ExitCode = ExitCodes.InvalidArguments;
            return;
        }

        if (watch && interactive)
        {
            AnsiConsole.MarkupLine("[red]Error:[/] Watch mode cannot be used with interactive mode.");
            context.ExitCode = ExitCodes.InvalidArguments;
            return;
        }

        // Interactive mode (REQ-06-020)
        if (interactive)
        {
            var cmd = serviceProvider.GetRequiredService<InteractiveCommand>();
            cmd.File = file;
            context.ExitCode = await cmd.ExecuteAsync(cancellationToken);
            return;
        }

        // Parse NAME=VALUE arrays into dictionaries (reject malformed entries)
        var malformed = vars.Concat(secrets).Concat(paramValues).Where(p => p.IndexOf('=') <= 0).ToList();
        if (malformed.Count > 0)
        {
            AnsiConsole.MarkupLine($"[red]Error:[/] Expected NAME=VALUE for --var/--secret/--param, got: {Markup.Escape(string.Join(", ", malformed))}");
            context.ExitCode = ExitCodes.InvalidArguments;
            return;
        }
        var cliVariables = ParseKeyValuePairs(vars);
        var cliSecrets = ParseKeyValuePairs(secrets);
        var cliParameters = new Dictionary<string, string>(ParseKeyValuePairs(paramValues), StringComparer.OrdinalIgnoreCase);

        // Options kept for compatibility that no longer change behaviour
        if (noReuse)
        {
            AnsiConsole.MarkupLine("[yellow]Warning:[/] --no-reuse has no effect: every job already gets a fresh container.");
        }

        // Warn if secrets passed via CLI
        if (cliSecrets.Count > 0)
        {
            AnsiConsole.MarkupLine("[yellow]Warning:[/] Secrets passed via --secret are visible in process lists.");
            AnsiConsole.MarkupLine("[yellow]Recommendation:[/] Use 'pdk secret set NAME' or PDK_SECRET_* environment variables.");
        }

        // Dry-run mode (Sprint 11 - REQ-11-003)
        if (dryRun)
        {
            var dryRunService = serviceProvider.GetRequiredService<PDK.CLI.DryRun.DryRunService>();
            var parserFactory = serviceProvider.GetRequiredService<IPipelineParserFactory>();
            var variableResolver = serviceProvider.GetRequiredService<IVariableResolver>();
            var configLoader = serviceProvider.GetRequiredService<IConfigurationLoader>();

            // Load configuration and variables
            var config = await configLoader.LoadAsync(configPath);
            if (config != null)
            {
                variableResolver.LoadFromConfiguration(config);
            }
            variableResolver.LoadFromEnvironment();
            foreach (var (k, v) in cliVariables)
            {
                variableResolver.SetVariable(k, v, PDK.Core.Variables.VariableSource.CliArgument);
            }

            // Parse pipeline
            var parser = parserFactory.GetParser(file.FullName);
            var pipeline = await parser.ParseFile(file.FullName, new PDK.Core.Models.PipelineParseOptions
            {
                Parameters = cliParameters,
                Variables = cliVariables,
                WorkspacePath = Directory.GetCurrentDirectory(),
                EventName = eventName
            });

            // Run dry-run validation
            var runnerTypeStr = runnerType switch
            {
                RunnerType.Docker => "docker",
                RunnerType.Host => "host",
                _ => "auto"
            };

            // Apply the same step filters as a real run so the plan shows what would execute
            PDK.Core.Filtering.IStepFilter? dryRunFilter = null;
            var dryRunFilterOptions = serviceProvider.GetRequiredService<FilterOptionsBuilder>().Build(new ExecutionOptions
            {
                FilePath = file.FullName,
                JobName = job,
                StepName = step,
                FilterStepNames = [.. filterSteps],
                FilterStepIndices = [.. filterStepIndices],
                FilterStepRanges = [.. filterStepRanges],
                SkipStepNames = [.. skipSteps],
                IncludeDependencies = includeDeps,
                FilterPreset = filterPreset
            }, config);

            if (dryRunFilterOptions.HasFilters)
            {
                var filterBuilder = serviceProvider.GetRequiredService<PDK.Core.Filtering.IStepFilterBuilder>();
                var filterValidation = filterBuilder.Validate(dryRunFilterOptions, pipeline);
                if (!filterValidation.IsValid)
                {
                    foreach (var error in filterValidation.Errors)
                    {
                        AnsiConsole.MarkupLine($"[red]Error:[/] [[{Markup.Escape(error.Code)}]] {Markup.Escape(error.Message)}");
                    }

                    context.ExitCode = ExitCodes.InvalidArguments;
                    return;
                }

                dryRunFilter = filterBuilder.Build(dryRunFilterOptions, pipeline);
            }

            var result = await dryRunService.ExecuteAsync(
                pipeline,
                file.FullName,
                runnerTypeStr,
                dryRunJson,
                new PDK.CLI.DryRun.DryRunRequest { JobName = job, Filter = dryRunFilter },
                cancellationToken);

            context.ExitCode = result.IsValid ? ExitCodes.Success : ExitCodes.Failure;
            return;
        }

        // Watch mode (Sprint 11 - REQ-11-001)
        if (watch)
        {
            // Watch mode is incompatible with validate-only mode
            if (validate)
            {
                AnsiConsole.MarkupLine("[red]Error:[/] Watch mode cannot be used with validate-only mode.");
                context.ExitCode = ExitCodes.InvalidArguments;
                return;
            }

            var watchService = serviceProvider.GetRequiredService<IWatchModeService>();

            var executionOptions = new ExecutionOptions
            {
                FilePath = file.FullName,
                JobName = job,
                StepName = step,
                RunnerType = runnerType,
                ValidateOnly = validate,
                Verbose = verbose,
                Quiet = quiet,
                Trace = trace,
                Silent = silent,
                ConfigPath = configPath,
                CliVariables = cliVariables,
                VarFilePath = varFile?.FullName,
                CliSecrets = cliSecrets,
                NoReuseContainers = noReuse,
                NoCacheImages = noCache,
                ParallelSteps = parallel,
                MaxParallelism = maxParallel,
                ShowMetrics = showMetrics || verbose,
                WatchMode = true,
                WatchDebounceMs = watchDebounce,
                WatchClear = watchClear,
                // Step filtering (REQ-11-007)
                FilterStepNames = [.. filterSteps],
                FilterStepIndices = [.. filterStepIndices],
                FilterStepRanges = [.. filterStepRanges],
                SkipStepNames = [.. skipSteps],
                IncludeDependencies = includeDeps,
                PreviewFilter = previewFilter,
                ConfirmFilter = confirmFilter,
                FilterPreset = filterPreset,
                NoDependencies = noDeps,
                StrictUnsupportedSteps = strict,
                EventName = eventName,
                KeepContainers = keepContainers,
                Parameters = cliParameters
            };

            // Configuration supplies the defaults for watch mode; explicit CLI flags win
            var watchModeOptions = new WatchModeOptions();
            var watchConfig = await serviceProvider.GetRequiredService<IConfigurationLoader>().LoadAsync(configPath);
            watchModeOptions.ApplyConfiguration(watchConfig?.Watch);
            if (context.ParseResult.FindResultFor(watchDebounceOption) is { IsImplicit: false })
            {
                watchModeOptions.DebounceMs = watchDebounce;
            }

            if (context.ParseResult.FindResultFor(watchClearOption) is { IsImplicit: false })
            {
                watchModeOptions.ClearOnRerun = watchClear;
            }

            // Ctrl+C / SIGTERM cancel the token (CancelOnProcessTermination) and the watch
            // service shuts down gracefully.
            await using (watchService)
            {
                await watchService.RunAsync(executionOptions, watchModeOptions, cancellationToken);
            }
            context.ExitCode = ExitCodes.Success;
            return;
        }

        var executor = serviceProvider.GetRequiredService<PipelineExecutor>();
        var runResult = await executor.Execute(new ExecutionOptions
        {
            FilePath = file.FullName,
            JobName = job,
            StepName = step,
            RunnerType = runnerType,
            ValidateOnly = validate,
            Verbose = verbose,
            Quiet = quiet,
            Trace = trace,
            Silent = silent,
            ConfigPath = configPath,
            CliVariables = cliVariables,
            VarFilePath = varFile?.FullName,
            CliSecrets = cliSecrets,
            NoReuseContainers = noReuse,
            NoCacheImages = noCache,
            ParallelSteps = parallel,
            MaxParallelism = maxParallel,
            ShowMetrics = showMetrics || verbose,  // Show metrics when verbose is enabled
            // Step filtering (REQ-11-007)
            FilterStepNames = [.. filterSteps],
            FilterStepIndices = [.. filterStepIndices],
            FilterStepRanges = [.. filterStepRanges],
            SkipStepNames = [.. skipSteps],
            IncludeDependencies = includeDeps,
            PreviewFilter = previewFilter,
            ConfirmFilter = confirmFilter,
            FilterPreset = filterPreset,
            NoDependencies = noDeps,
            StrictUnsupportedSteps = strict,
            EventName = eventName,
            KeepContainers = keepContainers,
            Parameters = cliParameters
        }, cancellationToken);
        context.ExitCode = runResult.ExitCode;
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[yellow]Cancelled.[/]");
        context.ExitCode = ExitCodes.Cancelled;
    }
    catch (Exception ex)
    {
        var errorFormatter = serviceProvider.GetRequiredService<ErrorFormatter>();
        errorFormatter.DisplayError(ex, verbose);
        context.ExitCode = ExitCodeFor(ex);
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
listCommand.AddOption(paramOption);

listCommand.SetHandler(async (InvocationContext context) =>
{
    try
    {
        if (!TryParseParameters(context.ParseResult.GetValueForOption(paramOption), out var listParameters))
        {
            context.ExitCode = ExitCodes.InvalidArguments;
            return;
        }

        var cmd = serviceProvider.GetRequiredService<ListCommand>();
        cmd.File = context.ParseResult.GetValueForOption(listFileOption);
        cmd.Details = context.ParseResult.GetValueForOption(detailsOption);
        cmd.Format = context.ParseResult.GetValueForOption(formatOption);
        cmd.Parameters = listParameters;
        context.ExitCode = await cmd.ExecuteAsync();
    }
    catch (Exception ex)
    {
        var errorFormatter = serviceProvider.GetRequiredService<ErrorFormatter>();
        errorFormatter.DisplayError(ex, verbose: false);
        context.ExitCode = ExitCodeFor(ex);
    }
});

// Validate command
var validateCommand = new Command("validate", "Validate a pipeline file");
validateCommand.AddOption(fileOption);
validateCommand.AddOption(paramOption);

validateCommand.SetHandler(async (InvocationContext context) =>
{
    var located = PipelineFileLocator.Resolve(
        context.ParseResult.GetValueForOption(fileOption), AnsiConsole.Console, "validate");
    if (located.File == null)
    {
        context.ExitCode = located.ExitCode;
        return;
    }

    if (!TryParseParameters(context.ParseResult.GetValueForOption(paramOption), out var validateParameters))
    {
        context.ExitCode = ExitCodes.InvalidArguments;
        return;
    }

    try
    {
        var parserFactory = serviceProvider.GetRequiredService<PipelineParserFactory>();
        var parser = parserFactory.GetParser(located.File.FullName);
        var pipeline = await parser.ParseFile(located.File.FullName, new PDK.Core.Models.PipelineParseOptions
        {
            Parameters = validateParameters,
            WorkspacePath = Directory.GetCurrentDirectory()
        });

        AnsiConsole.MarkupLine($"[green]\u2713[/] Pipeline is valid");
        AnsiConsole.MarkupLine($"  Provider: {pipeline.Provider}");
        AnsiConsole.MarkupLine($"  Jobs: {pipeline.Jobs.Count}");
        AnsiConsole.MarkupLine($"  Total Steps: {pipeline.Jobs.Values.Sum(j => j.Steps.Count)}");
        context.ExitCode = ExitCodes.Success;
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[red]\u2717 Pipeline validation failed[/]");
        var errorFormatter = serviceProvider.GetRequiredService<ErrorFormatter>();
        errorFormatter.DisplayError(ex, verbose: false);
        context.ExitCode = ex is FileNotFoundException or DirectoryNotFoundException
            ? ExitCodes.FileNotFound
            : ExitCodes.Failure;
    }
});

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

versionCommand.SetHandler(async (InvocationContext context) =>
{
    try
    {
        var cmd = serviceProvider.GetRequiredService<VersionCommand>();
        cmd.Full = context.ParseResult.GetValueForOption(versionFullOption);
        cmd.Format = context.ParseResult.GetValueForOption(versionFormatOption);
        cmd.NoUpdateCheck = context.ParseResult.GetValueForOption(noUpdateCheckOption);
        context.ExitCode = await cmd.ExecuteAsync(context.GetCancellationToken());
    }
    catch (Exception ex)
    {
        var errorFormatter = serviceProvider.GetRequiredService<ErrorFormatter>();
        errorFormatter.DisplayError(ex, verbose: false);
        context.ExitCode = ExitCodeFor(ex);
    }
});

// Doctor command (REQ-DK-007: Docker Availability Detection)
var doctorCommand = new Command("doctor", "Check system requirements and Docker availability");
doctorCommand.SetHandler(async (InvocationContext context) =>
{
    AnsiConsole.MarkupLine("[bold]PDK Doctor - System Diagnostics[/]");
    AnsiConsole.WriteLine();

    try
    {
        var statusProvider = serviceProvider.GetRequiredService<PDK.Core.Docker.IDockerStatusProvider>();
        var status = await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .StartAsync("Checking Docker availability...",
                _ => statusProvider.GetDockerStatusAsync(context.GetCancellationToken()));

        DockerDiagnostics.DisplayDockerStatus(status);
        context.ExitCode = status.IsAvailable ? ExitCodes.Success : ExitCodes.DockerUnavailable;
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
        context.ExitCode = ExitCodes.Failure;
    }
});

// Interactive command (REQ-06-020)
var interactiveCommand = new Command("interactive", "Interactive pipeline exploration and execution");
var interactiveFileOption = new Option<FileInfo?>(
    aliases: ["--file", "-f"],
    description: "Path to pipeline file (auto-detects if not specified)");

interactiveCommand.AddOption(interactiveFileOption);
interactiveCommand.SetHandler(async (InvocationContext context) =>
{
    try
    {
        var cmd = serviceProvider.GetRequiredService<InteractiveCommand>();
        cmd.File = context.ParseResult.GetValueForOption(interactiveFileOption);
        context.ExitCode = await cmd.ExecuteAsync(context.GetCancellationToken());
    }
    catch (Exception ex)
    {
        var errorFormatter = serviceProvider.GetRequiredService<ErrorFormatter>();
        errorFormatter.DisplayError(ex, verbose: false);
        context.ExitCode = ExitCodeFor(ex);
    }
});

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

secretSetCommand.SetHandler(async (InvocationContext context) =>
{
    var name = context.ParseResult.GetValueForArgument(secretNameArg);
    var valueOpt = context.ParseResult.GetValueForOption(secretValueOption);
    var useStdin = context.ParseResult.GetValueForOption(secretStdinOption);

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
        else if (valueOpt != null)
        {
            // Use --value option (with warning)
            AnsiConsole.MarkupLine("[yellow]Warning:[/] Value provided via CLI is visible in process list.");
            value = valueOpt;
        }
        else
        {
            // Interactive mode (recommended). Falls back to a plain line read when stdin is redirected.
            AnsiConsole.MarkupLine($"Enter value for [blue]{Markup.Escape(name)}[/]:");
            value = ReadSecretFromConsole();
        }

        await manager.SetSecretAsync(name, value);
        AnsiConsole.MarkupLine($"[green]\u2713[/] Secret '{Markup.Escape(name)}' saved");
        context.ExitCode = ExitCodes.Success;
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
        context.ExitCode = ExitCodes.Failure;
    }
});

// pdk secret list
var secretListCommand = new Command("list", "List secret names");
secretListCommand.SetHandler(async (InvocationContext context) =>
{
    try
    {
        var manager = serviceProvider.GetRequiredService<ISecretManager>();
        var names = await manager.ListSecretNamesAsync();
        if (!names.Any())
        {
            AnsiConsole.MarkupLine("[dim]No secrets stored[/]");
            context.ExitCode = ExitCodes.Success;
            return;
        }
        var unreadable = new HashSet<string>(await manager.GetUnreadableSecretNamesAsync(), StringComparer.Ordinal);
        foreach (var name in names)
        {
            if (unreadable.Contains(name))
            {
                AnsiConsole.MarkupLine($"{Markup.Escape(name)} [yellow](unreadable: cannot be decrypted with the current key; set it again)[/]");
            }
            else
            {
                AnsiConsole.WriteLine(name);
            }
        }
        context.ExitCode = ExitCodes.Success;
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
        context.ExitCode = ExitCodes.Failure;
    }
});

// pdk secret delete NAME
var secretDeleteCommand = new Command("delete", "Delete a secret");
var deleteNameArg = new Argument<string>("name", "Secret name to delete");
secretDeleteCommand.AddArgument(deleteNameArg);
secretDeleteCommand.SetHandler(async (InvocationContext context) =>
{
    var name = context.ParseResult.GetValueForArgument(deleteNameArg);
    try
    {
        var manager = serviceProvider.GetRequiredService<ISecretManager>();
        if (!await manager.SecretExistsAsync(name))
        {
            AnsiConsole.MarkupLine($"[yellow]Secret '{Markup.Escape(name)}' not found[/]");
            context.ExitCode = ExitCodes.Failure;
            return;
        }
        await manager.DeleteSecretAsync(name);
        AnsiConsole.MarkupLine($"[green]\u2713[/] Secret '{Markup.Escape(name)}' deleted");
        context.ExitCode = ExitCodes.Success;
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
        context.ExitCode = ExitCodes.Failure;
    }
});

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

var parser = new CommandLineBuilder(rootCommand)
    .UseVersionOption()
    .UseHelp()
    .UseEnvironmentVariableDirective()
    .UseParseDirective()
    .UseSuggestDirective()
    .RegisterWithDotnetSuggest()
    .UseTypoCorrections()
    .UseParseErrorReporting(ExitCodes.InvalidArguments)
    .UseExceptionHandler()
    .CancelOnProcessTermination()
    .Build();

try
{
    return await parser.InvokeAsync(args);
}
finally
{
    // Disposes singletons such as the container manager (removes tracked containers)
    // and flushes any buffered log events.
    await serviceProvider.DisposeAsync();
    Serilog.Log.CloseAndFlush();
}

static void ConfigureServices(ServiceCollection services)
{
    // Logging: one Serilog pipeline whose level, sinks and redaction the run command adjusts
    // after parsing its flags (--verbose/--trace/--quiet/--silent, --log-file, --log-json, --no-redact)
    var secretMasker = new SecretMasker();
    var loggingController = new PdkLoggingController(secretMasker);
    services.AddSingleton(loggingController);
    services.AddLogging(builder => loggingController.Configure(builder));

    // Register UI services
    services.AddSingleton<IAnsiConsole>(AnsiConsole.Console);
    services.AddSingleton<IConsoleOutput>(sp =>
        new ConsoleOutput(sp.GetRequiredService<IAnsiConsole>()));
    services.AddSingleton<IProgressReporter>(sp =>
        new ConsoleProgressReporter(sp.GetRequiredService<IAnsiConsole>()));

    // Register secret masker (shared with the logging pipeline)
    services.AddSingleton<ISecretMasker>(secretMasker);

    // Register configuration services (Sprint 7)
    services.AddSingleton<ConfigurationValidator>();
    services.AddSingleton<IConfigurationLoader, ConfigurationLoader>();
    services.AddSingleton<IConfigurationMerger, ConfigurationMerger>();
    // Register default configuration (can be replaced by command-specific configuration)
    services.AddSingleton<IConfiguration>(sp =>
    {
        var loader = sp.GetRequiredService<IConfigurationLoader>();
        // Pass null to use config discovery (returns null if no config file found)
        var config = loader.LoadAsync(null).GetAwaiter().GetResult() ?? new PdkConfig();
        return new PdkConfiguration(config);
    });

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
        sp.GetRequiredService<ISecretMasker>(),
        sp.GetService<ILogger<SecretManager>>()));
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
    services.AddSingleton<IPipelineParser, PDK.Providers.GitLab.GitLabCiParser>();

    // Register services
    services.AddSingleton<PipelineParserFactory>();
    services.AddSingleton<IPipelineParserFactory>(sp => sp.GetRequiredService<PipelineParserFactory>());
    services.AddSingleton<PipelineExecutor>();
    services.AddTransient<ListCommand>();
    services.AddTransient<InteractiveCommand>();

    // Register container manager
    services.AddSingleton<PDK.Runners.IContainerManager, DockerContainerManager>();
    // IContainerManager extends IDockerStatusProvider, so forward the registration
    services.AddSingleton<PDK.Core.Docker.IDockerStatusProvider>(sp =>
        sp.GetRequiredService<PDK.Runners.IContainerManager>());

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
    services.AddSingleton<IHostStepExecutor, HostUploadArtifactExecutor>();
    services.AddSingleton<IHostStepExecutor, HostDownloadArtifactExecutor>();
    services.AddSingleton<HostStepExecutorFactory>();

    // Register process executor for host mode
    services.AddSingleton<IProcessExecutor, ProcessExecutor>();

    // Performance metrics (--metrics): one tracker per process, read by the pipeline executor
    services.AddSingleton<IPerformanceTracker, PerformanceTracker>();

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

    // Register watch mode services (Sprint 11 - REQ-11-001)
    services.AddWatchMode();

    // Register dry-run services (Sprint 11 - REQ-11-003)
    services.AddSingleton<PDK.Core.Validation.IExecutorValidator, PDK.Runners.Validation.ExecutorValidator>();
    services.AddSingleton<PDK.Core.Validation.IValidationPhase, PDK.Core.Validation.Phases.SchemaValidationPhase>();
    services.AddSingleton<PDK.Core.Validation.IValidationPhase, PDK.Core.Validation.Phases.ExecutorValidationPhase>();
    services.AddSingleton<PDK.Core.Validation.IValidationPhase, PDK.Core.Validation.Phases.VariableValidationPhase>();
    services.AddSingleton<PDK.Core.Validation.IValidationPhase, PDK.Core.Validation.Phases.DependencyValidationPhase>();
    services.AddTransient<PDK.CLI.DryRun.DryRunUI>();
    services.AddTransient<PDK.CLI.DryRun.JsonOutputFormatter>();
    services.AddSingleton<PDK.Core.Validation.IImageMappingProvider, PDK.CLI.DryRun.ImageMappingProvider>();
    services.AddTransient<PDK.CLI.DryRun.DryRunService>();

    // Register step filtering services (Sprint 11 - REQ-11-007, REQ-11-008)
    services.AddStepFiltering();
}

/// <summary>
/// Parses <c>--param NAME=VALUE</c> values, reporting malformed entries.
/// </summary>
static bool TryParseParameters(string[]? values, out Dictionary<string, string> parameters)
{
    var malformed = (values ?? []).Where(p => p.IndexOf('=') <= 0).ToList();
    if (malformed.Count > 0)
    {
        AnsiConsole.MarkupLine($"[red]Error:[/] Expected NAME=VALUE for --param, got: {Markup.Escape(string.Join(", ", malformed))}");
        parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return false;
    }

    parameters = new Dictionary<string, string>(ParseKeyValuePairs(values), StringComparer.OrdinalIgnoreCase);
    return true;
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
/// Maps an exception escaping a command handler to the documented exit code.
/// </summary>
static int ExitCodeFor(Exception ex)
{
    return ex switch
    {
        FileNotFoundException or DirectoryNotFoundException => ExitCodes.FileNotFound,
        DockerUnavailableException => ExitCodes.DockerUnavailable,
        OperationCanceledException => ExitCodes.Cancelled,
        ArgumentException => ExitCodes.InvalidArguments,
        _ => ExitCodes.Failure
    };
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
    if (Console.IsInputRedirected)
    {
        // No interactive console (piped input, CI): read a single line.
        return (Console.In.ReadLine() ?? string.Empty).TrimEnd('\r', '\n');
    }

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