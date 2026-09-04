using PDK.Core.Artifacts;
using PDK.Core.Models;
using PDK.Providers.AzureDevOps.Models;
using PDK.Providers.Common;

namespace PDK.Providers.AzureDevOps;

/// <summary>
/// Maps Azure Pipeline steps to the PDK common Step model.
/// Handles task parsing, script shortcuts, publish/download shortcuts and input conversion.
/// All text (scripts, inputs, environment, conditions, display names) is kept raw: <c>$( )</c> macros are
/// resolved at run time for known variables only.
/// </summary>
public static class AzureStepMapper
{
    private const string DefaultPublishBuildArtifactsPath = "$(Build.ArtifactStagingDirectory)";
    private const string DefaultPublishPipelineArtifactPath = "$(Pipeline.Workspace)";
    private const string DefaultArtifactName = "drop";
    private const string DefaultDownloadPath = "./";

    private static readonly HashSet<string> SetupTasks = new(StringComparer.OrdinalIgnoreCase)
    {
        "usedotnet",
        "nodetool",
        "usenode",
        "usepythonversion",
        "javatoolinstaller",
        "gotool",
        "nugettoolinstaller",
        "cache"
    };

    /// <summary>
    /// Maps an Azure Pipeline step to a PDK Step model.
    /// </summary>
    /// <param name="azureStep">The Azure step to map.</param>
    /// <param name="stepIndex">The zero-based index of the step within the job.</param>
    /// <returns>A PDK Step model representing the Azure step.</returns>
    public static Step MapStep(AzureStep azureStep, int stepIndex) => MapStep(azureStep, stepIndex, null);

    /// <summary>
    /// Maps an Azure Pipeline step to a PDK Step model, recording non-fatal findings in <paramref name="warnings"/>.
    /// </summary>
    /// <param name="azureStep">The Azure step to map.</param>
    /// <param name="stepIndex">The zero-based index of the step within the job.</param>
    /// <param name="warnings">Optional sink for warnings.</param>
    /// <returns>A PDK Step model representing the Azure step.</returns>
    public static Step MapStep(AzureStep azureStep, int stepIndex, ICollection<string>? warnings)
    {
        ArgumentNullException.ThrowIfNull(azureStep);

        var step = new Step
        {
            Id = string.IsNullOrWhiteSpace(azureStep.Name) ? null : azureStep.Name.Trim(),
            Name = GenerateStepName(azureStep, stepIndex),
            ContinueOnError = azureStep.ContinueOnError ?? false,
            Enabled = azureStep.Enabled ?? true,
            TimeoutMinutes = azureStep.TimeoutInMinutes,
            WorkingDirectory = string.IsNullOrWhiteSpace(azureStep.WorkingDirectory) ? null : azureStep.WorkingDirectory
        };

        // Map environment variables (raw)
        if (azureStep.Env is { Count: > 0 })
        {
            step.Environment = azureStep.Env.ToDictionary(kvp => kvp.Key, kvp => kvp.Value ?? string.Empty);
        }

        // Map condition (raw expression, evaluated at run time)
        if (!string.IsNullOrWhiteSpace(azureStep.Condition))
        {
            step.Condition = new Condition
            {
                Expression = azureStep.Condition,
                Type = ConditionType.Expression
            };
        }

        switch (azureStep.GetStepType())
        {
            case "checkout":
                MapCheckoutStep(azureStep, step);
                break;

            case "task":
                MapTaskStep(azureStep, step, warnings);
                break;

            case "bash":
            case "pwsh":
            case "powershell":
            case "script":
                MapScriptStep(azureStep, step);
                break;

            case "publish":
                MapPublishShortcut(azureStep, step);
                break;

            case "download":
                MapDownloadShortcut(azureStep, step);
                break;

            default:
                step.Type = StepType.Unknown;
                break;
        }

        return step;
    }

    /// <summary>
    /// Maps <c>checkout:</c> steps. <c>checkout: none</c> keeps a disabled checkout step so the job shape is preserved.
    /// </summary>
    private static void MapCheckoutStep(AzureStep azureStep, Step step)
    {
        step.Type = StepType.Checkout;

        var target = azureStep.Checkout!.Trim();
        if (target.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            step.Enabled = false;
        }
        else if (target.Length > 0)
        {
            step.With["repository"] = target;
        }

        AddWithIfPresent(step, "fetchDepth", azureStep.FetchDepth);
        AddWithIfPresent(step, "clean", azureStep.Clean);
        AddWithIfPresent(step, "submodules", azureStep.Submodules);
        AddWithIfPresent(step, "lfs", azureStep.Lfs);
        AddWithIfPresent(step, "persistCredentials", azureStep.PersistCredentials);
        AddWithIfPresent(step, "path", azureStep.Path);
    }

    /// <summary>
    /// Maps a task-based Azure step to a PDK Step.
    /// Extracts task name and version, maps to the appropriate StepType, and converts inputs.
    /// </summary>
    private static void MapTaskStep(AzureStep azureStep, Step step, ICollection<string>? warnings)
    {
        var taskReference = azureStep.Task!.Trim();
        if (taskReference.Length == 0)
        {
            step.Type = StepType.Unknown;
            return;
        }

        // Extract task name from "TaskName@version" format
        var atIndex = taskReference.IndexOf('@');
        var taskName = atIndex > 0 ? taskReference[..atIndex] : taskReference;
        var taskVersion = atIndex > 0 && atIndex < taskReference.Length - 1
            ? taskReference[(atIndex + 1)..]
            : null;

        step.ActionReference = taskReference;
        step.Type = MapTaskToStepType(taskName);

        // Convert and store inputs (raw)
        step.With = ConvertInputs(azureStep.Inputs);

        // Store task metadata for debugging and reference
        step.With["_task"] = taskName;
        if (taskVersion != null)
        {
            step.With["_version"] = taskVersion;
        }

        // Handle specific task types with special processing
        switch (taskName.ToLowerInvariant())
        {
            case "dotnetcorecli":
                HandleDotNetCoreTask(azureStep, step);
                break;

            case "powershell":
                HandlePowerShellTask(azureStep, step);
                break;

            case "bash":
                HandleBashTask(azureStep, step);
                break;

            case "docker":
                HandleDockerTask(azureStep, step);
                break;

            case "cmdline":
                HandleCmdLineTask(azureStep, step);
                break;

            case "npm":
                HandleNpmTask(azureStep, step);
                break;

            case "publishbuildartifacts":
                HandlePublishBuildArtifactsTask(azureStep, step);
                break;

            case "publishpipelineartifact":
                HandlePublishPipelineArtifactTask(azureStep, step);
                break;

            case "downloadbuildartifacts":
                HandleDownloadBuildArtifactsTask(azureStep, step);
                break;

            case "downloadpipelineartifact":
                HandleDownloadPipelineArtifactTask(azureStep, step);
                break;

            default:
                if (step.Type == StepType.Unknown)
                {
                    warnings?.Add($"Task '{taskReference}' (step '{step.Name}') is not supported locally and will be skipped.");
                }

                break;
        }
    }

    /// <summary>
    /// Maps a script shortcut (bash:, pwsh:, powershell:, script:) to a PDK Step.
    /// </summary>
    private static void MapScriptStep(AzureStep azureStep, Step step)
    {
        // Determine step type and shell based on script format
        if (azureStep.Bash is not null)
        {
            step.Type = StepType.Script;
            step.Shell = "bash";
        }
        else if (azureStep.Pwsh is not null)
        {
            step.Type = StepType.PowerShell;
            step.Shell = "pwsh";
        }
        else if (azureStep.PowerShell is not null)
        {
            step.Type = StepType.PowerShell;
            step.Shell = "powershell";
        }
        else
        {
            // 'script:' uses the platform default shell; bash is the common default for Linux runners
            step.Type = StepType.Script;
            step.Shell = "bash";
        }

        step.Script = azureStep.GetScriptContent();
    }

    /// <summary>
    /// Maps the <c>- publish: &lt;path&gt;</c> shortcut (equivalent to PublishPipelineArtifact@1).
    /// </summary>
    private static void MapPublishShortcut(AzureStep azureStep, Step step)
    {
        step.Type = StepType.UploadArtifact;

        var name = string.IsNullOrWhiteSpace(azureStep.Artifact) ? DefaultArtifactName : azureStep.Artifact;
        var path = string.IsNullOrWhiteSpace(azureStep.Publish) ? DefaultPublishPipelineArtifactPath : azureStep.Publish;

        step.With["artifact"] = name;
        step.With["targetPath"] = path;
        step.Artifact = CreateUploadDefinition(name, path);
    }

    /// <summary>
    /// Maps the <c>- download: current|none|&lt;alias&gt;</c> shortcut (equivalent to DownloadPipelineArtifact@2).
    /// </summary>
    private static void MapDownloadShortcut(AzureStep azureStep, Step step)
    {
        step.Type = StepType.DownloadArtifact;

        var source = azureStep.Download!.Trim();
        if (source.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            step.Enabled = false;
            return;
        }

        var name = azureStep.Artifact ?? string.Empty;
        var path = string.IsNullOrWhiteSpace(azureStep.Path) ? DefaultDownloadPath : azureStep.Path;

        step.With["source"] = source;
        step.With["artifact"] = name;
        step.With["path"] = path;

        var patterns = YamlValues.ToStringList(azureStep.Patterns);
        if (patterns.Count > 0)
        {
            step.With["patterns"] = string.Join("\n", patterns);
        }

        step.Artifact = CreateDownloadDefinition(name, path);
    }

    /// <summary>
    /// Maps an Azure task name to a PDK StepType enum value.
    /// </summary>
    /// <param name="taskName">The Azure task name (without version).</param>
    /// <returns>The corresponding StepType enum value.</returns>
    private static StepType MapTaskToStepType(string taskName)
    {
        var key = taskName.ToLowerInvariant();

        return key switch
        {
            "dotnetcorecli" => StepType.Dotnet,
            "powershell" => StepType.PowerShell,
            "bash" => StepType.Script,
            "docker" => StepType.Docker,
            "cmdline" => StepType.Script,
            "publishbuildartifacts" => StepType.UploadArtifact,
            "publishpipelineartifact" => StepType.UploadArtifact,
            "downloadbuildartifacts" => StepType.DownloadArtifact,
            "downloadpipelineartifact" => StepType.DownloadArtifact,
            "copyfiles" => StepType.FileOperation,
            "npm" => StepType.Npm,
            "maven" => StepType.Maven,
            "gradle" => StepType.Gradle,
            _ when SetupTasks.Contains(key) => StepType.Setup,
            _ => StepType.Unknown
        };
    }

    /// <summary>
    /// Generates a step name based on the Azure step's displayName or its kind.
    /// Falls back to a numbered step name if nothing better is available.
    /// </summary>
    /// <param name="azureStep">The Azure step.</param>
    /// <param name="stepIndex">The zero-based index of the step.</param>
    /// <returns>A name for the step.</returns>
    private static string GenerateStepName(AzureStep azureStep, int stepIndex)
    {
        // Use displayName if provided (raw; $( ) macros are resolved at run time)
        if (!string.IsNullOrWhiteSpace(azureStep.DisplayName))
        {
            return azureStep.DisplayName;
        }

        // Use task name if available
        if (!string.IsNullOrWhiteSpace(azureStep.Task))
        {
            var task = azureStep.Task.Trim();
            var atIndex = task.IndexOf('@');
            return atIndex > 0 ? task[..atIndex] : task;
        }

        switch (azureStep.GetStepType())
        {
            case "checkout":
                return azureStep.Checkout!.Trim().Equals("none", StringComparison.OrdinalIgnoreCase)
                    ? "Checkout (none)"
                    : "Checkout";

            case "publish":
                return $"Publish {(string.IsNullOrWhiteSpace(azureStep.Artifact) ? DefaultArtifactName : azureStep.Artifact)}";

            case "download":
                return azureStep.Download!.Trim().Equals("none", StringComparison.OrdinalIgnoreCase)
                    ? "Download (none)"
                    : $"Download {(string.IsNullOrWhiteSpace(azureStep.Artifact) ? "artifacts" : azureStep.Artifact)}";

            case "bash":
            case "pwsh":
            case "powershell":
            case "script":
                var kind = azureStep.GetStepType();
                return $"{char.ToUpperInvariant(kind[0])}{kind[1..]} script";

            default:
                return $"Step {stepIndex + 1}";
        }
    }

    /// <summary>
    /// Converts Azure task inputs (Dictionary&lt;string, object&gt;) to PDK step inputs (Dictionary&lt;string, string&gt;).
    /// Values are kept raw.
    /// </summary>
    /// <param name="inputs">The Azure task inputs.</param>
    /// <returns>A dictionary of string key-value pairs suitable for PDK Step.With property.</returns>
    private static Dictionary<string, string> ConvertInputs(Dictionary<string, object>? inputs)
    {
        if (inputs == null || inputs.Count == 0)
        {
            return new Dictionary<string, string>();
        }

        return inputs.ToDictionary(
            kvp => kvp.Key,
            kvp => YamlValues.AsString(kvp.Value) ?? string.Empty);
    }

    /// <summary>
    /// Merges environment variables from multiple levels (pipeline, job, step).
    /// Later levels override earlier levels (pipeline &lt; job &lt; step). Values are kept raw.
    /// </summary>
    /// <param name="pipelineEnv">Pipeline-level environment variables.</param>
    /// <param name="jobEnv">Job-level environment variables.</param>
    /// <param name="stepEnv">Step-level environment variables.</param>
    /// <returns>A merged dictionary of environment variables.</returns>
    public static Dictionary<string, string> MergeEnvironmentVariables(
        Dictionary<string, string>? pipelineEnv,
        Dictionary<string, string>? jobEnv,
        Dictionary<string, string>? stepEnv)
    {
        var result = new Dictionary<string, string>();

        // Apply in order: pipeline -> job -> step (later overrides earlier)
        foreach (var level in new[] { pipelineEnv, jobEnv, stepEnv })
        {
            if (level is null)
            {
                continue;
            }

            foreach (var kvp in level)
            {
                result[kvp.Key] = kvp.Value ?? string.Empty;
            }
        }

        return result;
    }

    /// <summary>
    /// Parses job/stage dependencies which can be a string (single dependency) or a list (multiple dependencies).
    /// </summary>
    /// <param name="dependsOn">The dependsOn property from an Azure job or stage.</param>
    /// <returns>A list of dependency identifiers.</returns>
    public static List<string> ParseJobDependencies(object? dependsOn) => YamlValues.ToStringList(dependsOn);

    // Task-specific handlers for extracting special properties

    /// <summary>
    /// Handles DotNetCoreCLI@2: maps <c>workingDirectory</c>, generates a name from <c>command</c>, and turns
    /// <c>command: custom</c> into a script step running <c>dotnet &lt;custom&gt; &lt;arguments&gt;</c>.
    /// </summary>
    private static void HandleDotNetCoreTask(AzureStep azureStep, Step step)
    {
        var command = GetInput(azureStep, "command");
        ApplyWorkingDirectory(azureStep, step, "workingDirectory");

        if (command is not null && command.Equals("custom", StringComparison.OrdinalIgnoreCase))
        {
            var custom = GetInput(azureStep, "custom") ?? string.Empty;
            var arguments = GetInput(azureStep, "arguments");
            var commandLine = $"dotnet {custom}".TrimEnd();

            step.Type = StepType.Script;
            step.Shell = "bash";
            step.Script = string.IsNullOrWhiteSpace(arguments) ? commandLine : $"{commandLine} {arguments}";

            if (string.IsNullOrWhiteSpace(azureStep.DisplayName))
            {
                step.Name = commandLine;
            }

            return;
        }

        if (command is not null && string.IsNullOrWhiteSpace(azureStep.DisplayName))
        {
            step.Name = $"dotnet {command}";
        }
    }

    /// <summary>
    /// Handles PowerShell@2: inline scripts run as-is, file scripts become <c>pwsh -File &lt;filePath&gt; &lt;arguments&gt;</c>.
    /// </summary>
    private static void HandlePowerShellTask(AzureStep azureStep, Step step)
    {
        step.Shell = "pwsh";
        ApplyWorkingDirectory(azureStep, step, "workingDirectory");
        MapScriptTaskContent(azureStep, step, filePath => $"pwsh -File \"{filePath}\"");
    }

    /// <summary>
    /// Handles Bash@3: inline scripts run as-is, file scripts become <c>bash &lt;filePath&gt; &lt;arguments&gt;</c>.
    /// </summary>
    private static void HandleBashTask(AzureStep azureStep, Step step)
    {
        step.Shell = "bash";
        ApplyWorkingDirectory(azureStep, step, "workingDirectory");
        MapScriptTaskContent(azureStep, step, filePath => $"bash \"{filePath}\"");
    }

    /// <summary>
    /// Resolves the script content of Bash@3 / PowerShell@2 from <c>targetType</c> (default: filePath).
    /// </summary>
    private static void MapScriptTaskContent(AzureStep azureStep, Step step, Func<string, string> fileCommand)
    {
        var script = GetInput(azureStep, "script");
        var filePath = GetInput(azureStep, "filePath");
        var arguments = GetInput(azureStep, "arguments");

        var targetType = GetInput(azureStep, "targetType")?.ToLowerInvariant();
        targetType ??= !string.IsNullOrEmpty(script) && string.IsNullOrEmpty(filePath) ? "inline" : "filepath";

        if (targetType == "inline")
        {
            step.Script = script;
            return;
        }

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            var commandLine = fileCommand(filePath);
            step.Script = string.IsNullOrWhiteSpace(arguments) ? commandLine : $"{commandLine} {arguments}";
            step.With["scriptFile"] = filePath;
        }
    }

    /// <summary>
    /// Handles Docker@2: default command is <c>buildAndPush</c>; maps <c>buildContext</c> to <c>context</c> and keeps
    /// <c>repository</c>, <c>containerRegistry</c>, <c>tags</c> (newline list) and <c>Dockerfile</c> raw.
    /// </summary>
    private static void HandleDockerTask(AzureStep azureStep, Step step)
    {
        var command = GetInput(azureStep, "command") ?? "buildAndPush";
        step.With["command"] = command;

        var context = GetInput(azureStep, "buildContext");
        if (context is not null)
        {
            step.With["context"] = context;
        }

        var dockerfile = GetInput(azureStep, "Dockerfile");
        var repository = GetInput(azureStep, "repository");
        var tags = GetInput(azureStep, "tags");

        // Construct an informational docker command line for engines that prefer script-based execution
        var lowerCommand = command.ToLowerInvariant();
        if (lowerCommand is "build" or "buildandpush")
        {
            var parts = new List<string> { "docker", "build" };

            if (!string.IsNullOrWhiteSpace(dockerfile))
            {
                parts.Add($"-f {dockerfile}");
            }

            foreach (var tag in SplitLines(tags))
            {
                parts.Add($"-t {FormatImageReference(repository, tag)}");
            }

            parts.Add(string.IsNullOrWhiteSpace(context) ? "." : context);
            step.Script = string.Join(" ", parts);
        }
        else
        {
            step.Script = $"docker {command}";
        }
    }

    /// <summary>
    /// Handles CmdLine@2: the <c>script</c> input is the step script.
    /// </summary>
    private static void HandleCmdLineTask(AzureStep azureStep, Step step)
    {
        step.Shell = "bash";
        ApplyWorkingDirectory(azureStep, step, "workingDirectory");
        step.Script = GetInput(azureStep, "script");
    }

    /// <summary>
    /// Handles Npm@1: maps <c>workingDir</c> and translates <c>customCommand</c> into the executor's command/script
    /// inputs (or a plain script step for commands the npm executor does not model).
    /// </summary>
    private static void HandleNpmTask(AzureStep azureStep, Step step)
    {
        ApplyWorkingDirectory(azureStep, step, "workingDir");

        var command = GetInput(azureStep, "command") ?? "install";
        if (!command.Equals("custom", StringComparison.OrdinalIgnoreCase))
        {
            step.With["command"] = command;
            return;
        }

        var custom = (GetInput(azureStep, "customCommand") ?? string.Empty).Trim();
        var tokens = custom.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length == 0)
        {
            step.With["command"] = "install";
            return;
        }

        var verb = tokens[0].ToLowerInvariant();
        switch (verb)
        {
            case "install":
            case "ci":
            case "test":
            case "build":
                step.With["command"] = verb;
                SetArguments(step, tokens.Skip(1));
                return;

            case "run" when tokens.Length > 1:
                step.With["command"] = "run";
                step.With["script"] = tokens[1];
                SetArguments(step, tokens.Skip(2));
                return;

            default:
                step.Type = StepType.Script;
                step.Shell = "bash";
                step.Script = $"npm {custom}";
                step.With["command"] = "custom";
                return;
        }
    }

    /// <summary>
    /// Handles PublishBuildArtifacts@1 (<c>PathtoPublish</c>, <c>ArtifactName</c>).
    /// </summary>
    private static void HandlePublishBuildArtifactsTask(AzureStep azureStep, Step step)
    {
        var path = GetInput(azureStep, "PathtoPublish") ?? DefaultPublishBuildArtifactsPath;
        var name = GetInput(azureStep, "ArtifactName") ?? DefaultArtifactName;

        step.Artifact = CreateUploadDefinition(name, path);
    }

    /// <summary>
    /// Handles PublishPipelineArtifact@1 (<c>artifact</c> then <c>artifactName</c>; <c>targetPath</c>).
    /// </summary>
    private static void HandlePublishPipelineArtifactTask(AzureStep azureStep, Step step)
    {
        var name = GetInput(azureStep, "artifact") ?? GetInput(azureStep, "artifactName") ?? DefaultArtifactName;
        var path = GetInput(azureStep, "targetPath") ?? GetInput(azureStep, "path") ?? DefaultPublishPipelineArtifactPath;

        step.Artifact = CreateUploadDefinition(name, path);
    }

    /// <summary>
    /// Handles DownloadBuildArtifacts@0 (<c>artifactName</c>, <c>downloadPath</c>).
    /// </summary>
    private static void HandleDownloadBuildArtifactsTask(AzureStep azureStep, Step step)
    {
        var name = GetInput(azureStep, "artifactName") ?? string.Empty;
        var path = GetInput(azureStep, "downloadPath") ?? GetInput(azureStep, "targetPath") ?? DefaultDownloadPath;

        step.Artifact = CreateDownloadDefinition(name, path);
    }

    /// <summary>
    /// Handles DownloadPipelineArtifact@2 (<c>artifact</c>/<c>artifactName</c>; <c>path</c>/<c>downloadPath</c>/<c>targetPath</c>).
    /// </summary>
    private static void HandleDownloadPipelineArtifactTask(AzureStep azureStep, Step step)
    {
        var name = GetInput(azureStep, "artifact") ?? GetInput(azureStep, "artifactName") ?? string.Empty;
        var path = GetInput(azureStep, "path")
                   ?? GetInput(azureStep, "downloadPath")
                   ?? GetInput(azureStep, "targetPath")
                   ?? DefaultDownloadPath;

        step.Artifact = CreateDownloadDefinition(name, path);
    }

    private static ArtifactDefinition CreateUploadDefinition(string name, string path) => new()
    {
        Name = name,
        Operation = ArtifactOperation.Upload,
        Patterns = new[] { path },
        Options = new ArtifactOptions
        {
            Compression = CompressionType.Zip // Azure default
        }
    };

    private static ArtifactDefinition CreateDownloadDefinition(string name, string path) => new()
    {
        Name = name,
        Operation = ArtifactOperation.Download,
        Patterns = Array.Empty<string>(),
        TargetPath = path,
        Options = ArtifactOptions.Default
    };

    /// <summary>
    /// Reads a task input by name (exact, then case-insensitive). Empty values are treated as absent.
    /// </summary>
    private static string? GetInput(AzureStep azureStep, string name)
    {
        var inputs = azureStep.Inputs;
        if (inputs is null || inputs.Count == 0)
        {
            return null;
        }

        if (!inputs.TryGetValue(name, out var value))
        {
            var match = inputs.FirstOrDefault(kvp => kvp.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (match.Key is null)
            {
                return null;
            }

            value = match.Value;
        }

        var text = YamlValues.AsString(value);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static void ApplyWorkingDirectory(AzureStep azureStep, Step step, string inputName)
    {
        var workingDirectory = GetInput(azureStep, inputName);
        if (workingDirectory is not null)
        {
            step.WorkingDirectory = workingDirectory;
        }
    }

    private static void SetArguments(Step step, IEnumerable<string> tokens)
    {
        var arguments = string.Join(' ', tokens);
        if (arguments.Length > 0)
        {
            step.With["arguments"] = arguments;
        }
    }

    private static void AddWithIfPresent(Step step, string key, object? value)
    {
        var text = YamlValues.AsString(value);
        if (!string.IsNullOrWhiteSpace(text))
        {
            step.With[key] = text;
        }
    }

    private static IEnumerable<string> SplitLines(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        return value
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0);
    }

    private static string FormatImageReference(string? repository, string tag) =>
        string.IsNullOrWhiteSpace(repository) ? tag : $"{repository}:{tag}";
}
