using YamlDotNet.Serialization;

namespace PDK.Providers.AzureDevOps.Models;

/// <summary>
/// Represents a single step in an Azure Pipeline job. A step is one of:
/// <c>task:</c>, <c>bash:</c>, <c>pwsh:</c>, <c>powershell:</c>, <c>script:</c>, <c>checkout:</c>,
/// <c>publish:</c>, <c>download:</c>, or a <c>template:</c> reference (not supported locally).
/// </summary>
public sealed class AzureStep
{
    /// <summary>
    /// Gets or sets the task reference in <c>TaskName@version</c> format.
    /// </summary>
    [YamlMember(Alias = "task")]
    public string? Task { get; set; }

    /// <summary>
    /// Gets or sets the display name (kept raw, including <c>$( )</c> macros).
    /// </summary>
    [YamlMember(Alias = "displayName")]
    public string? DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the step name used as identifier for output variables (<c>name:</c>).
    /// </summary>
    [YamlMember(Alias = "name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the task inputs.
    /// </summary>
    [YamlMember(Alias = "inputs")]
    public Dictionary<string, object>? Inputs { get; set; }

    /// <summary>
    /// Gets or sets the inline bash script (<c>bash:</c> shortcut).
    /// </summary>
    [YamlMember(Alias = "bash")]
    public string? Bash { get; set; }

    /// <summary>
    /// Gets or sets the inline PowerShell Core script (<c>pwsh:</c> shortcut).
    /// </summary>
    [YamlMember(Alias = "pwsh")]
    public string? Pwsh { get; set; }

    /// <summary>
    /// Gets or sets the inline platform-default script (<c>script:</c> shortcut).
    /// </summary>
    [YamlMember(Alias = "script")]
    public string? Script { get; set; }

    /// <summary>
    /// Gets or sets the inline Windows PowerShell script (<c>powershell:</c> shortcut).
    /// </summary>
    [YamlMember(Alias = "powershell")]
    public string? PowerShell { get; set; }

    /// <summary>
    /// Gets or sets the step condition (kept raw).
    /// </summary>
    [YamlMember(Alias = "condition")]
    public string? Condition { get; set; }

    /// <summary>
    /// Gets or sets whether the step is enabled (<c>enabled: false</c> keeps the step but skips it).
    /// </summary>
    [YamlMember(Alias = "enabled")]
    public bool? Enabled { get; set; }

    /// <summary>
    /// Gets or sets whether the job continues when the step fails.
    /// </summary>
    [YamlMember(Alias = "continueOnError")]
    public bool? ContinueOnError { get; set; }

    /// <summary>
    /// Gets or sets the step timeout in minutes.
    /// </summary>
    [YamlMember(Alias = "timeoutInMinutes")]
    public int? TimeoutInMinutes { get; set; }

    /// <summary>
    /// Gets or sets the step environment variables (kept raw).
    /// </summary>
    [YamlMember(Alias = "env")]
    public Dictionary<string, string>? Env { get; set; }

    /// <summary>
    /// Gets or sets the working directory of script shortcuts.
    /// </summary>
    [YamlMember(Alias = "workingDirectory")]
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Gets or sets the checkout target: <c>self</c>, <c>none</c>, or a repository resource alias.
    /// </summary>
    [YamlMember(Alias = "checkout")]
    public string? Checkout { get; set; }

    /// <summary>
    /// Gets or sets the checkout fetch depth.
    /// </summary>
    [YamlMember(Alias = "fetchDepth")]
    public object? FetchDepth { get; set; }

    /// <summary>
    /// Gets or sets the checkout clean option.
    /// </summary>
    [YamlMember(Alias = "clean")]
    public object? Clean { get; set; }

    /// <summary>
    /// Gets or sets the checkout submodules option.
    /// </summary>
    [YamlMember(Alias = "submodules")]
    public object? Submodules { get; set; }

    /// <summary>
    /// Gets or sets the checkout LFS option.
    /// </summary>
    [YamlMember(Alias = "lfs")]
    public object? Lfs { get; set; }

    /// <summary>
    /// Gets or sets the checkout persistCredentials option.
    /// </summary>
    [YamlMember(Alias = "persistCredentials")]
    public object? PersistCredentials { get; set; }

    /// <summary>
    /// Gets or sets the <c>path</c> of a checkout (relative checkout directory) or of a download (target directory).
    /// </summary>
    [YamlMember(Alias = "path")]
    public string? Path { get; set; }

    /// <summary>
    /// Gets or sets the publish shortcut (<c>- publish: &lt;path&gt;</c>).
    /// </summary>
    [YamlMember(Alias = "publish")]
    public string? Publish { get; set; }

    /// <summary>
    /// Gets or sets the download shortcut (<c>- download: current|none|&lt;alias&gt;</c>).
    /// </summary>
    [YamlMember(Alias = "download")]
    public string? Download { get; set; }

    /// <summary>
    /// Gets or sets the artifact name of a publish/download shortcut.
    /// </summary>
    [YamlMember(Alias = "artifact")]
    public string? Artifact { get; set; }

    /// <summary>
    /// Gets or sets the file patterns of a download shortcut (string or list).
    /// </summary>
    [YamlMember(Alias = "patterns")]
    public object? Patterns { get; set; }

    /// <summary>
    /// Gets or sets the steps template reference (<c>- template: steps.yml</c>); not supported locally.
    /// </summary>
    [YamlMember(Alias = "template")]
    public string? Template { get; set; }

    /// <summary>
    /// Gets or sets the parameters passed to a steps template.
    /// </summary>
    [YamlMember(Alias = "parameters")]
    public object? Parameters { get; set; }

    /// <summary>
    /// Gets or sets the retry count on task failure.
    /// </summary>
    [YamlMember(Alias = "retryCountOnTaskFailure")]
    public int? RetryCountOnTaskFailure { get; set; }

    /// <summary>
    /// Gets or sets the failOnStderr option of script shortcuts.
    /// </summary>
    [YamlMember(Alias = "failOnStderr")]
    public object? FailOnStderr { get; set; }

    /// <summary>
    /// Gets or sets the step target (container/host).
    /// </summary>
    [YamlMember(Alias = "target")]
    public object? Target { get; set; }

    /// <summary>
    /// Determines the step kind.
    /// </summary>
    /// <returns>"template", "checkout", "task", "bash", "pwsh", "powershell", "script", "publish", "download", or "unknown".</returns>
    public string GetStepType()
    {
        if (Template is not null)
        {
            return "template";
        }

        if (Checkout is not null)
        {
            return "checkout";
        }

        if (Task is not null)
        {
            return "task";
        }

        if (Bash is not null)
        {
            return "bash";
        }

        if (Pwsh is not null)
        {
            return "pwsh";
        }

        if (PowerShell is not null)
        {
            return "powershell";
        }

        if (Script is not null)
        {
            return "script";
        }

        if (Publish is not null)
        {
            return "publish";
        }

        if (Download is not null)
        {
            return "download";
        }

        return "unknown";
    }

    /// <summary>
    /// Gets the inline script content of a script shortcut, or null for other step kinds.
    /// </summary>
    public string? GetScriptContent()
    {
        return Bash ?? Pwsh ?? PowerShell ?? Script;
    }
}
