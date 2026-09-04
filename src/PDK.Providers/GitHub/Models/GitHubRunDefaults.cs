using YamlDotNet.Serialization;

namespace PDK.Providers.GitHub.Models;

/// <summary>
/// The <c>defaults.run</c> block: shell and working directory applied to <c>run:</c> steps that do not set their own.
/// </summary>
public sealed class GitHubRunDefaults
{
    /// <summary>
    /// Default shell for run steps (e.g. <c>bash</c>, <c>pwsh</c>, or a template such as <c>bash -eo pipefail {0}</c>).
    /// </summary>
    [YamlMember(Alias = "shell")]
    public string? Shell { get; set; }

    /// <summary>
    /// Default working directory for run steps.
    /// </summary>
    [YamlMember(Alias = "working-directory")]
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Merges two levels of defaults; values from <paramref name="overrides"/> win when set.
    /// </summary>
    public static GitHubRunDefaults? Merge(GitHubRunDefaults? baseDefaults, GitHubRunDefaults? overrides)
    {
        if (baseDefaults is null && overrides is null)
        {
            return null;
        }

        return new GitHubRunDefaults
        {
            Shell = FirstNonEmpty(overrides?.Shell, baseDefaults?.Shell),
            WorkingDirectory = FirstNonEmpty(overrides?.WorkingDirectory, baseDefaults?.WorkingDirectory)
        };
    }

    private static string? FirstNonEmpty(string? first, string? second) =>
        !string.IsNullOrWhiteSpace(first) ? first : (!string.IsNullOrWhiteSpace(second) ? second : null);
}
