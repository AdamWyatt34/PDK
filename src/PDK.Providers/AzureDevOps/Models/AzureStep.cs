using YamlDotNet.Serialization;

namespace PDK.Providers.AzureDevOps.Models;

/// <summary>
/// Represents a step definition in an Azure Pipeline job.
/// Steps are the individual actions executed as part of a job, such as running scripts or invoking tasks.
/// </summary>
/// <remarks>
/// Azure Pipelines supports multiple step formats:
/// <list type="bullet">
/// <item><description>Task format: task: TaskName@version with inputs</description></item>
/// <item><description>Bash shortcut: bash: script content</description></item>
/// <item><description>PowerShell shortcut: pwsh: script content</description></item>
/// <item><description>Generic script: script: command</description></item>
/// </list>
/// </remarks>
public sealed class AzureStep
{
    /// <summary>
    /// Gets or sets the task identifier in the format "TaskName@version".
    /// Used for task-based steps (e.g., "DotNetCoreCLI@2").
    /// Mutually exclusive with script shortcuts (Bash, Pwsh, Script).
    /// </summary>
    [YamlMember(Alias = "task")]
    public string? Task { get; set; }

    /// <summary>
    /// Gets or sets the human-readable name displayed for this step in the pipeline run.
    /// If not specified, Azure Pipelines generates a default display name based on the step type.
    /// </summary>
    [YamlMember(Alias = "displayName")]
    public string? DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the input parameters for a task step.
    /// Each task defines its own set of inputs. The value can be a string or complex object.
    /// Only applicable when Task property is set.
    /// </summary>
    [YamlMember(Alias = "inputs")]
    public Dictionary<string, object>? Inputs { get; set; }

    /// <summary>
    /// Gets or sets the Bash script to execute.
    /// Script shortcut for running Bash commands. Can be single-line or multi-line script.
    /// Mutually exclusive with Task, Pwsh, and Script properties.
    /// </summary>
    [YamlMember(Alias = "bash")]
    public string? Bash { get; set; }

    /// <summary>
    /// Gets or sets the PowerShell script to execute.
    /// Script shortcut for running PowerShell commands on any platform (Windows, Linux, macOS).
    /// Can be single-line or multi-line script.
    /// Mutually exclusive with Task, Bash, and Script properties.
    /// </summary>
    [YamlMember(Alias = "pwsh")]
    public string? Pwsh { get; set; }

    /// <summary>
    /// Gets or sets the generic script to execute.
    /// Script shortcut that uses the platform's default shell (cmd.exe on Windows, bash on Linux/macOS).
    /// Can be single-line or multi-line script.
    /// Mutually exclusive with Task, Bash, and Pwsh properties.
    /// </summary>
    [YamlMember(Alias = "script")]
    public string? Script { get; set; }

    /// <summary>
    /// Gets or sets the PowerShell script to execute (Windows PowerShell).
    /// Legacy script shortcut for running Windows PowerShell. Prefer 'pwsh' for cross-platform PowerShell.
    /// Can be single-line or multi-line script.
    /// </summary>
    [YamlMember(Alias = "powershell")]
    public string? PowerShell { get; set; }

    /// <summary>
    /// Gets or sets the condition expression that determines whether the step runs.
    /// Examples: "succeeded()", "failed()", "always()", "eq(variables['Build.Reason'], 'PullRequest')".
    /// If not specified, the default is "succeeded()" (run only if previous steps succeeded).
    /// </summary>
    [YamlMember(Alias = "condition")]
    public string? Condition { get; set; }

    /// <summary>
    /// Gets or sets whether the step is enabled.
    /// If false, the step is skipped during pipeline execution.
    /// Useful for temporarily disabling steps without removing them.
    /// </summary>
    [YamlMember(Alias = "enabled")]
    public bool? Enabled { get; set; }

    /// <summary>
    /// Gets or sets whether the pipeline should continue if this step fails.
    /// When true, subsequent steps will run even if this step fails.
    /// The job will still be marked as failed, but execution continues.
    /// </summary>
    [YamlMember(Alias = "continueOnError")]
    public bool? ContinueOnError { get; set; }

    /// <summary>
    /// Gets or sets the timeout for the step in minutes.
    /// If the step runs longer than the specified timeout, it is automatically cancelled.
    /// </summary>
    [YamlMember(Alias = "timeoutInMinutes")]
    public int? TimeoutInMinutes { get; set; }

    /// <summary>
    /// Gets or sets environment variables for the step.
    /// These variables are available to the step during execution and override job-level variables.
    /// </summary>
    [YamlMember(Alias = "env")]
    public Dictionary<string, string>? Env { get; set; }

    /// <summary>
    /// Gets or sets the working directory for the step.
    /// Specifies the directory where the step's script or task should execute.
    /// Relative paths are resolved from the pipeline workspace root.
    /// </summary>
    [YamlMember(Alias = "workingDirectory")]
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Gets or sets the target for checkout steps.
    /// Specifies which repository to check out (e.g., 'self' for the current repository).
    /// </summary>
    [YamlMember(Alias = "checkout")]
    public string? Checkout { get; set; }

    /// <summary>
    /// Gets or sets the retry count for the step.
    /// If the step fails, it will be retried up to the specified number of times.
    /// </summary>
    [YamlMember(Alias = "retryCountOnTaskFailure")]
    public int? RetryCountOnTaskFailure { get; set; }

    /// <summary>
    /// Determines the type of step based on its properties.
    /// </summary>
    /// <returns>A string indicating the step type: "task", "bash", "pwsh", "powershell", "script", or "checkout".</returns>
    public string GetStepType()
    {
        if (!string.IsNullOrEmpty(Checkout)) return "checkout";
        if (!string.IsNullOrEmpty(Task)) return "task";
        if (!string.IsNullOrEmpty(Bash)) return "bash";
        if (!string.IsNullOrEmpty(Pwsh)) return "pwsh";
        if (!string.IsNullOrEmpty(PowerShell)) return "powershell";
        if (!string.IsNullOrEmpty(Script)) return "script";
        return "unknown";
    }

    /// <summary>
    /// Gets the script content based on the step type.
    /// </summary>
    /// <returns>The script content if this is a script step, otherwise null.</returns>
    public string? GetScriptContent()
    {
        return Bash ?? Pwsh ?? PowerShell ?? Script;
    }
}
