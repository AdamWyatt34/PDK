using System.Text.RegularExpressions;
using PDK.Core.Models;
using PDK.Providers.AzureDevOps.Models;

namespace PDK.Providers.AzureDevOps;

/// <summary>
/// Maps Azure Pipeline steps to PDK common Step model.
/// Handles task parsing, script shortcuts, input conversion, and variable syntax transformation.
/// </summary>
public static class AzureStepMapper
{
    /// <summary>
    /// Maps an Azure Pipeline step to a PDK Step model.
    /// Handles both task-based steps (task: TaskName@version) and script shortcuts (bash:, pwsh:, script:).
    /// </summary>
    /// <param name="azureStep">The Azure step to map.</param>
    /// <param name="stepIndex">The zero-based index of the step within the job.</param>
    /// <returns>A PDK Step model representing the Azure step.</returns>
    public static Step MapStep(AzureStep azureStep, int stepIndex)
    {
        var step = new Step
        {
            Name = GenerateStepName(azureStep, stepIndex),
            ContinueOnError = azureStep.ContinueOnError ?? false,
            WorkingDirectory = ConvertVariableSyntax(azureStep.WorkingDirectory)
        };

        // Map environment variables
        if (azureStep.Env != null && azureStep.Env.Count > 0)
        {
            step.Environment = azureStep.Env.ToDictionary(
                kvp => kvp.Key,
                kvp => ConvertVariableSyntax(kvp.Value)
            );
        }

        // Map condition
        if (!string.IsNullOrWhiteSpace(azureStep.Condition))
        {
            step.Condition = new Condition
            {
                Expression = ConvertVariableSyntax(azureStep.Condition),
                Type = ConditionType.Expression
            };
        }

        // Determine step type and map accordingly
        var stepType = azureStep.GetStepType();

        switch (stepType)
        {
            case "checkout":
                step.Type = StepType.Checkout;
                if (!string.IsNullOrEmpty(azureStep.Checkout))
                {
                    step.With["repository"] = azureStep.Checkout;
                }
                break;

            case "task":
                MapTaskStep(azureStep, step);
                break;

            case "bash":
            case "pwsh":
            case "powershell":
            case "script":
                MapScriptStep(azureStep, step);
                break;

            default:
                step.Type = StepType.Unknown;
                break;
        }

        return step;
    }

    /// <summary>
    /// Maps a task-based Azure step to a PDK Step.
    /// Extracts task name and version, maps to appropriate StepType, and converts inputs.
    /// </summary>
    /// <param name="azureStep">The Azure step with a task definition.</param>
    /// <param name="step">The PDK step to populate.</param>
    private static void MapTaskStep(AzureStep azureStep, Step step)
    {
        if (string.IsNullOrEmpty(azureStep.Task))
        {
            step.Type = StepType.Unknown;
            return;
        }

        // Extract task name from "TaskName@version" format
        var atIndex = azureStep.Task.IndexOf('@');
        var taskName = atIndex > 0 ? azureStep.Task[..atIndex] : azureStep.Task;
        var taskVersion = atIndex > 0 && atIndex < azureStep.Task.Length - 1
            ? azureStep.Task[(atIndex + 1)..]
            : null;

        // Map to StepType
        step.Type = MapTaskToStepType(taskName);

        // Convert and store inputs
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
        }
    }

    /// <summary>
    /// Maps a script-based Azure step (bash:, pwsh:, script:) to a PDK Step.
    /// Determines the shell type and extracts the script content.
    /// </summary>
    /// <param name="azureStep">The Azure step with a script definition.</param>
    /// <param name="step">The PDK step to populate.</param>
    private static void MapScriptStep(AzureStep azureStep, Step step)
    {
        var scriptContent = azureStep.GetScriptContent();

        if (string.IsNullOrEmpty(scriptContent))
        {
            step.Type = StepType.Unknown;
            return;
        }

        // Determine step type and shell based on script format
        if (!string.IsNullOrEmpty(azureStep.Bash))
        {
            step.Type = StepType.Bash;
            step.Shell = "bash";
        }
        else if (!string.IsNullOrEmpty(azureStep.Pwsh))
        {
            step.Type = StepType.PowerShell;
            step.Shell = "pwsh";
        }
        else if (!string.IsNullOrEmpty(azureStep.PowerShell))
        {
            step.Type = StepType.PowerShell;
            step.Shell = "powershell";
        }
        else if (!string.IsNullOrEmpty(azureStep.Script))
        {
            step.Type = StepType.Script;
            step.Shell = "bash"; // Azure uses platform default, we'll use bash as common default
        }
        else
        {
            step.Type = StepType.Script;
            step.Shell = "bash";
        }

        // Convert variable syntax in script content
        step.Script = ConvertVariableSyntax(scriptContent);
    }

    /// <summary>
    /// Maps an Azure task name to a PDK StepType enum value.
    /// </summary>
    /// <param name="taskName">The Azure task name (without version).</param>
    /// <returns>The corresponding StepType enum value.</returns>
    private static StepType MapTaskToStepType(string taskName)
    {
        return taskName.ToLowerInvariant() switch
        {
            "dotnetcorecli" => StepType.Dotnet,
            "usedotnet" => StepType.Dotnet,
            "powershell" => StepType.PowerShell,
            "bash" => StepType.Bash,
            "docker" => StepType.Docker,
            "cmdline" => StepType.Script,
            "publishbuildartifacts" => StepType.FileOperation,
            "downloadbuildartifacts" => StepType.FileOperation,
            "copyfiles" => StepType.FileOperation,
            "npm" => StepType.Npm,
            "maven" => StepType.Maven,
            "gradle" => StepType.Gradle,
            _ => StepType.Unknown
        };
    }

    /// <summary>
    /// Generates a step name based on the Azure step's displayName or task identifier.
    /// Falls back to a numbered step name if neither is available.
    /// </summary>
    /// <param name="azureStep">The Azure step.</param>
    /// <param name="stepIndex">The zero-based index of the step.</param>
    /// <returns>A name for the step.</returns>
    private static string GenerateStepName(AzureStep azureStep, int stepIndex)
    {
        // Use displayName if provided
        if (!string.IsNullOrWhiteSpace(azureStep.DisplayName))
        {
            return ConvertVariableSyntax(azureStep.DisplayName);
        }

        // Use task name if available
        if (!string.IsNullOrEmpty(azureStep.Task))
        {
            var atIndex = azureStep.Task.IndexOf('@');
            var taskName = atIndex > 0 ? azureStep.Task[..atIndex] : azureStep.Task;
            return taskName;
        }

        // Use script type as name
        var stepType = azureStep.GetStepType();
        if (stepType != "task" && stepType != "unknown")
        {
            return $"{char.ToUpper(stepType[0])}{stepType[1..]} script";
        }

        // Fallback to numbered step
        return $"Step {stepIndex + 1}";
    }

    /// <summary>
    /// Converts Azure Pipeline variable syntax $(variableName) to PDK common syntax ${variableName}.
    /// Applies regex-based transformation to all variable references in the input string.
    /// </summary>
    /// <param name="input">The input string that may contain Azure variable references.</param>
    /// <returns>The string with converted variable syntax, or empty string if input is null.</returns>
    private static string ConvertVariableSyntax(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return input ?? string.Empty;

        // Replace $(variable) with ${variable}
        // This regex matches $( followed by one or more non-) characters, followed by )
        return Regex.Replace(input, @"\$\(([^)]+)\)", @"${$1}");
    }

    /// <summary>
    /// Converts Azure task inputs (Dictionary&lt;string, object&gt;) to PDK step inputs (Dictionary&lt;string, string&gt;).
    /// Applies variable syntax conversion to all input values.
    /// </summary>
    /// <param name="inputs">The Azure task inputs.</param>
    /// <returns>A dictionary of string key-value pairs suitable for PDK Step.With property.</returns>
    private static Dictionary<string, string> ConvertInputs(Dictionary<string, object>? inputs)
    {
        if (inputs == null || inputs.Count == 0)
            return new Dictionary<string, string>();

        return inputs.ToDictionary(
            kvp => kvp.Key,
            kvp => ConvertVariableSyntax(kvp.Value?.ToString() ?? string.Empty)
        );
    }

    /// <summary>
    /// Merges environment variables from multiple levels (pipeline, job, step).
    /// Later levels override earlier levels (pipeline &lt; job &lt; step).
    /// </summary>
    /// <param name="pipelineEnv">Pipeline-level environment variables.</param>
    /// <param name="jobEnv">Job-level environment variables.</param>
    /// <param name="stepEnv">Step-level environment variables.</param>
    /// <returns>A merged dictionary of environment variables with variable syntax converted.</returns>
    public static Dictionary<string, string> MergeEnvironmentVariables(
        Dictionary<string, string>? pipelineEnv,
        Dictionary<string, string>? jobEnv,
        Dictionary<string, string>? stepEnv)
    {
        var result = new Dictionary<string, string>();

        // Apply in order: pipeline -> job -> step (later overrides earlier)
        if (pipelineEnv != null)
        {
            foreach (var kvp in pipelineEnv)
            {
                result[kvp.Key] = ConvertVariableSyntax(kvp.Value);
            }
        }

        if (jobEnv != null)
        {
            foreach (var kvp in jobEnv)
            {
                result[kvp.Key] = ConvertVariableSyntax(kvp.Value);
            }
        }

        if (stepEnv != null)
        {
            foreach (var kvp in stepEnv)
            {
                result[kvp.Key] = ConvertVariableSyntax(kvp.Value);
            }
        }

        return result;
    }

    /// <summary>
    /// Parses job dependencies which can be a string (single dependency) or list (multiple dependencies).
    /// </summary>
    /// <param name="dependsOn">The dependsOn property from an Azure job or stage.</param>
    /// <returns>A list of dependency identifiers.</returns>
    public static List<string> ParseJobDependencies(object? dependsOn)
    {
        if (dependsOn == null)
            return new List<string>();

        if (dependsOn is string singleDep)
            return new List<string> { singleDep };

        if (dependsOn is List<object> listDeps)
            return listDeps.Select(d => d.ToString() ?? string.Empty)
                          .Where(d => !string.IsNullOrEmpty(d))
                          .ToList();

        // Try to handle IEnumerable<object>
        if (dependsOn is System.Collections.IEnumerable enumerable)
        {
            return enumerable.Cast<object>()
                           .Select(d => d.ToString() ?? string.Empty)
                           .Where(d => !string.IsNullOrEmpty(d))
                           .ToList();
        }

        return new List<string>();
    }

    // Task-specific handlers for extracting special properties

    /// <summary>
    /// Handles DotNetCoreCLI@2 task-specific processing.
    /// Extracts command, projects, and arguments inputs.
    /// </summary>
    private static void HandleDotNetCoreTask(AzureStep azureStep, Step step)
    {
        if (azureStep.Inputs == null)
            return;

        // Common inputs: command, projects, arguments
        // These are already in step.With from ConvertInputs, but we can add additional processing if needed

        // If command input exists, we might want to include it in the step name for clarity
        if (azureStep.Inputs.TryGetValue("command", out var command) &&
            string.IsNullOrWhiteSpace(azureStep.DisplayName))
        {
            step.Name = $"dotnet {command}";
        }
    }

    /// <summary>
    /// Handles PowerShell@2 task-specific processing.
    /// Extracts script content based on targetType (inline or filePath).
    /// </summary>
    private static void HandlePowerShellTask(AzureStep azureStep, Step step)
    {
        if (azureStep.Inputs == null)
            return;

        step.Shell = "pwsh";

        // Check targetType to determine if script is inline or file-based
        if (azureStep.Inputs.TryGetValue("targetType", out var targetType))
        {
            var targetTypeStr = targetType?.ToString()?.ToLowerInvariant();

            if (targetTypeStr == "inline" && azureStep.Inputs.TryGetValue("script", out var scriptContent))
            {
                step.Script = ConvertVariableSyntax(scriptContent?.ToString());
            }
            else if (targetTypeStr == "filepath" && azureStep.Inputs.TryGetValue("filePath", out var filePath))
            {
                // For file-based scripts, we store the path and create a script that executes it
                var filePathStr = ConvertVariableSyntax(filePath?.ToString());
                step.Script = $"pwsh -File \"{filePathStr}\"";
                step.With["scriptFile"] = filePathStr;
            }
        }
    }

    /// <summary>
    /// Handles Bash@3 task-specific processing.
    /// Extracts script content based on targetType (inline or filePath).
    /// </summary>
    private static void HandleBashTask(AzureStep azureStep, Step step)
    {
        if (azureStep.Inputs == null)
            return;

        step.Shell = "bash";

        // Check targetType to determine if script is inline or file-based
        if (azureStep.Inputs.TryGetValue("targetType", out var targetType))
        {
            var targetTypeStr = targetType?.ToString()?.ToLowerInvariant();

            if (targetTypeStr == "inline" && azureStep.Inputs.TryGetValue("script", out var scriptContent))
            {
                step.Script = ConvertVariableSyntax(scriptContent?.ToString());
            }
            else if (targetTypeStr == "filepath" && azureStep.Inputs.TryGetValue("filePath", out var filePath))
            {
                // For file-based scripts, we store the path and create a script that executes it
                var filePathStr = ConvertVariableSyntax(filePath?.ToString());
                step.Script = $"bash \"{filePathStr}\"";
                step.With["scriptFile"] = filePathStr;
            }
        }
    }

    /// <summary>
    /// Handles Docker@2 task-specific processing.
    /// Extracts command, Dockerfile, and tags inputs.
    /// </summary>
    private static void HandleDockerTask(AzureStep azureStep, Step step)
    {
        if (azureStep.Inputs == null)
            return;

        // Docker task inputs are already in step.With from ConvertInputs
        // We might want to construct a docker command for the script property

        if (azureStep.Inputs.TryGetValue("command", out var command))
        {
            var commandStr = command?.ToString();

            // Optionally construct a docker command string for Script property
            // This is useful for execution engines that prefer script-based execution
            if (!string.IsNullOrEmpty(commandStr))
            {
                var dockerCmd = $"docker {commandStr}";

                if (commandStr.ToLowerInvariant() == "build" &&
                    azureStep.Inputs.TryGetValue("Dockerfile", out var dockerfile))
                {
                    dockerCmd += $" -f {ConvertVariableSyntax(dockerfile?.ToString())}";
                }

                if (azureStep.Inputs.TryGetValue("tags", out var tags))
                {
                    var tagsStr = ConvertVariableSyntax(tags?.ToString());
                    if (!string.IsNullOrEmpty(tagsStr))
                    {
                        dockerCmd += $" -t {tagsStr}";
                    }
                }

                step.Script = dockerCmd;
            }
        }
    }

    /// <summary>
    /// Handles CmdLine@2 task-specific processing.
    /// Extracts script input and sets it as the step script.
    /// </summary>
    private static void HandleCmdLineTask(AzureStep azureStep, Step step)
    {
        if (azureStep.Inputs == null)
            return;

        // CmdLine task has a 'script' input that contains the command to execute
        if (azureStep.Inputs.TryGetValue("script", out var scriptContent))
        {
            step.Script = ConvertVariableSyntax(scriptContent?.ToString());
        }
    }
}
