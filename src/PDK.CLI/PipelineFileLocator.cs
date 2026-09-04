using Spectre.Console;

namespace PDK.CLI;

/// <summary>
/// Result of locating a pipeline file for a command.
/// </summary>
/// <param name="File">The resolved pipeline file, or null when none could be selected.</param>
/// <param name="ExitCode">The exit code to use when <paramref name="File"/> is null.</param>
public sealed record PipelineFileLocation(FileInfo? File, int ExitCode);

/// <summary>
/// Finds the pipeline file a command should operate on: the explicit <c>--file</c> value,
/// or the single pipeline file discovered in the current directory.
/// </summary>
public static class PipelineFileLocator
{
    /// <summary>
    /// Locations searched, in order, when no file is given.
    /// </summary>
    public static readonly string[] SearchDescriptions =
    [
        ".github/workflows/*.yml",
        ".github/workflows/*.yaml",
        "azure-pipelines.yml",
        "azure-pipelines.yaml",
        ".azure-pipelines/*.yml",
        ".azure-pipelines/*.yaml",
        "*.pipeline.yml",
        "*.pipeline.yaml"
    ];

    /// <summary>
    /// Discovers candidate pipeline files below <paramref name="directory"/>.
    /// Returned paths are relative to the directory and sorted.
    /// </summary>
    public static List<string> Discover(string? directory = null)
    {
        var currentDir = directory ?? Directory.GetCurrentDirectory();
        var files = new List<string>();

        var githubDir = Path.Combine(currentDir, ".github", "workflows");
        if (Directory.Exists(githubDir))
        {
            files.AddRange(Directory.GetFiles(githubDir, "*.yml"));
            files.AddRange(Directory.GetFiles(githubDir, "*.yaml"));
        }

        foreach (var name in new[] { "azure-pipelines.yml", "azure-pipelines.yaml" })
        {
            var candidate = Path.Combine(currentDir, name);
            if (System.IO.File.Exists(candidate))
            {
                files.Add(candidate);
            }
        }

        var azureDir = Path.Combine(currentDir, ".azure-pipelines");
        if (Directory.Exists(azureDir))
        {
            files.AddRange(Directory.GetFiles(azureDir, "*.yml"));
            files.AddRange(Directory.GetFiles(azureDir, "*.yaml"));
        }

        files.AddRange(Directory.GetFiles(currentDir, "*.pipeline.yml"));
        files.AddRange(Directory.GetFiles(currentDir, "*.pipeline.yaml"));

        return files
            .Distinct()
            .Select(f => Path.GetRelativePath(currentDir, f))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Resolves the file for a command. Reports problems on <paramref name="console"/> and returns
    /// the exit code the command should use when no file could be selected.
    /// </summary>
    /// <param name="explicitFile">The <c>--file</c> value, or null to auto-detect.</param>
    /// <param name="console">Console used for messages.</param>
    /// <param name="verb">Verb used in the guidance message (e.g. "run", "validate").</param>
    public static PipelineFileLocation Resolve(FileInfo? explicitFile, IAnsiConsole console, string verb = "run")
    {
        if (explicitFile != null)
        {
            if (!explicitFile.Exists)
            {
                console.MarkupLine($"[red]Error:[/] File not found: {Markup.Escape(explicitFile.FullName)}");
                return new PipelineFileLocation(null, ExitCodes.FileNotFound);
            }

            return new PipelineFileLocation(explicitFile, ExitCodes.Success);
        }

        var currentDir = Directory.GetCurrentDirectory();
        var detected = Discover(currentDir);

        if (detected.Count == 0)
        {
            console.MarkupLine("[red]Error:[/] No pipeline file found in the current directory.");
            console.MarkupLine("[dim]Looked for:[/]");
            foreach (var pattern in SearchDescriptions)
            {
                console.MarkupLine($"  [dim]{Markup.Escape(pattern)}[/]");
            }
            console.MarkupLine($"Use [cyan]--file[/] to specify the pipeline to {Markup.Escape(verb)}.");
            return new PipelineFileLocation(null, ExitCodes.FileNotFound);
        }

        if (detected.Count > 1)
        {
            console.MarkupLine("[yellow]Multiple pipeline files found:[/]");
            foreach (var file in detected)
            {
                console.MarkupLine($"  {Markup.Escape(file)}");
            }
            console.MarkupLine($"Use [cyan]--file[/] to specify which pipeline to {Markup.Escape(verb)}.");
            return new PipelineFileLocation(null, ExitCodes.InvalidArguments);
        }

        var single = new FileInfo(Path.Combine(currentDir, detected[0]));
        console.MarkupLine($"[cyan]Auto-detected:[/] {Markup.Escape(detected[0])}");
        return new PipelineFileLocation(single, ExitCodes.Success);
    }
}
