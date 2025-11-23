using YamlDotNet.Serialization;

namespace PDK.Providers.AzureDevOps.Models;

/// <summary>
/// Represents task-specific properties for Azure Pipeline steps.
/// Tasks are reusable building blocks that perform specific actions like building code,
/// running tests, or deploying applications.
/// </summary>
/// <remarks>
/// Tasks are specified using the format "TaskName@version" (e.g., "DotNetCoreCLI@2").
/// Common tasks include DotNetCoreCLI@2, PowerShell@2, Bash@3, Docker@2, and CmdLine@2.
/// </remarks>
public sealed class AzureTask
{
    /// <summary>
    /// Gets or sets the task identifier in the format "TaskName@version".
    /// Example: "DotNetCoreCLI@2", "PowerShell@2", "Bash@3".
    /// The version number ensures compatibility with specific task implementations.
    /// </summary>
    [YamlMember(Alias = "task")]
    public string Task { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the human-readable name displayed for this task in the pipeline run.
    /// If not specified, Azure Pipelines uses the task name as the display name.
    /// </summary>
    [YamlMember(Alias = "displayName")]
    public string? DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the input parameters for the task.
    /// Each task defines its own set of inputs. Common examples:
    /// - DotNetCoreCLI@2: command, projects, arguments
    /// - PowerShell@2: targetType, script, filePath
    /// - Docker@2: command, Dockerfile, tags
    /// </summary>
    [YamlMember(Alias = "inputs")]
    public Dictionary<string, object> Inputs { get; set; } = new();

    /// <summary>
    /// Gets or sets the condition expression that determines whether the task runs.
    /// Examples: "succeeded()", "failed()", "always()", "and(succeeded(), eq(variables['Build.SourceBranch'], 'refs/heads/main'))".
    /// If not specified, the default is "succeeded()" (run only if previous steps succeeded).
    /// </summary>
    [YamlMember(Alias = "condition")]
    public string? Condition { get; set; }

    /// <summary>
    /// Gets or sets whether the task is enabled.
    /// If false, the task is skipped during pipeline execution.
    /// Useful for temporarily disabling tasks without removing them from the pipeline.
    /// </summary>
    [YamlMember(Alias = "enabled")]
    public bool? Enabled { get; set; }

    /// <summary>
    /// Gets or sets whether the pipeline should continue if this task fails.
    /// When true, subsequent tasks will run even if this task fails.
    /// The job will still be marked as failed, but execution continues.
    /// </summary>
    [YamlMember(Alias = "continueOnError")]
    public bool? ContinueOnError { get; set; }

    /// <summary>
    /// Gets or sets the timeout for the task in minutes.
    /// If the task runs longer than the specified timeout, it is automatically cancelled.
    /// </summary>
    [YamlMember(Alias = "timeoutInMinutes")]
    public int? TimeoutInMinutes { get; set; }

    /// <summary>
    /// Gets or sets environment variables for the task.
    /// These variables are available to the task during execution.
    /// </summary>
    [YamlMember(Alias = "env")]
    public Dictionary<string, string>? Env { get; set; }

    /// <summary>
    /// Extracts the task name from the full task identifier.
    /// </summary>
    /// <returns>The task name without the version (e.g., "DotNetCoreCLI" from "DotNetCoreCLI@2").</returns>
    public string GetTaskName()
    {
        var atIndex = Task.IndexOf('@');
        return atIndex > 0 ? Task[..atIndex] : Task;
    }

    /// <summary>
    /// Extracts the task version from the full task identifier.
    /// </summary>
    /// <returns>The task version (e.g., "2" from "DotNetCoreCLI@2"), or null if no version is specified.</returns>
    public string? GetTaskVersion()
    {
        var atIndex = Task.IndexOf('@');
        return atIndex > 0 && atIndex < Task.Length - 1 ? Task[(atIndex + 1)..] : null;
    }
}
