namespace PDK.CLI.Commands;

using System.Text.Json;
using System.Text.Json.Serialization;
using PDK.CLI.UI;
using PDK.Core.Models;
using Spectre.Console;

/// <summary>
/// Output format options for the list command.
/// </summary>
public enum OutputFormat
{
    /// <summary>
    /// Rich table format with colors (default).
    /// </summary>
    Table,

    /// <summary>
    /// JSON output for scripting and automation.
    /// </summary>
    Json,

    /// <summary>
    /// Minimal output showing only job IDs.
    /// </summary>
    Minimal
}

/// <summary>
/// Command handler for listing jobs in a pipeline.
/// Supports multiple output formats and detailed step information.
/// </summary>
public sealed class ListCommand
{
    private readonly IPipelineParserFactory _parserFactory;
    private readonly IConsoleOutput _output;
    private readonly IAnsiConsole _console;

    /// <summary>
    /// Gets or sets the pipeline file to parse. If null, auto-detection is attempted.
    /// </summary>
    public FileInfo? File { get; set; }

    /// <summary>
    /// Gets or sets whether to show detailed step information.
    /// </summary>
    public bool Details { get; set; }

    /// <summary>
    /// Gets or sets the output format.
    /// </summary>
    public OutputFormat Format { get; set; } = OutputFormat.Table;

    /// <summary>
    /// Initializes a new instance of <see cref="ListCommand"/>.
    /// </summary>
    /// <param name="parserFactory">Factory for getting pipeline parsers.</param>
    /// <param name="output">Console output service.</param>
    /// <param name="console">Spectre.Console instance for rendering tables.</param>
    public ListCommand(
        IPipelineParserFactory parserFactory,
        IConsoleOutput output,
        IAnsiConsole console)
    {
        _parserFactory = parserFactory ?? throw new ArgumentNullException(nameof(parserFactory));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _console = console ?? throw new ArgumentNullException(nameof(console));
    }

    /// <summary>
    /// Executes the list command.
    /// </summary>
    /// <returns>Exit code: 0 for success, non-zero for failure.</returns>
    public async Task<int> ExecuteAsync()
    {
        try
        {
            // Determine file path
            string filePath;
            if (File != null)
            {
                if (!File.Exists)
                {
                    _output.WriteError($"File not found: {File.FullName}");
                    ShowFileNotFoundSuggestions();
                    return 1;
                }
                filePath = File.FullName;
            }
            else
            {
                var detectedFile = AutoDetectPipeline();
                if (detectedFile == null)
                {
                    return 1; // Error already displayed in AutoDetectPipeline
                }
                filePath = detectedFile;
            }

            // Parse pipeline
            var parser = _parserFactory.GetParser(filePath);
            var pipeline = await parser.ParseFile(filePath);

            if (pipeline.Jobs.Count == 0)
            {
                _output.WriteWarning("No jobs found in pipeline.");
                return 0;
            }

            // Render based on format
            switch (Format)
            {
                case OutputFormat.Json:
                    RenderJson(pipeline);
                    break;
                case OutputFormat.Minimal:
                    RenderMinimal(pipeline);
                    break;
                case OutputFormat.Table:
                default:
                    if (Details)
                    {
                        RenderTableWithDetails(pipeline);
                    }
                    else
                    {
                        RenderTable(pipeline);
                    }
                    break;
            }

            return 0;
        }
        catch (NotSupportedException ex)
        {
            _output.WriteError(ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            _output.WriteError($"Failed to parse pipeline: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Auto-detects pipeline files in the current directory.
    /// </summary>
    /// <returns>The path to the detected file, or null if none found.</returns>
    private string? AutoDetectPipeline()
    {
        var currentDir = Directory.GetCurrentDirectory();
        var detectedFiles = new List<string>();

        // Check for GitHub Actions workflows
        var githubWorkflowsDir = Path.Combine(currentDir, ".github", "workflows");
        if (Directory.Exists(githubWorkflowsDir))
        {
            var ymlFiles = Directory.GetFiles(githubWorkflowsDir, "*.yml");
            var yamlFiles = Directory.GetFiles(githubWorkflowsDir, "*.yaml");
            detectedFiles.AddRange(ymlFiles);
            detectedFiles.AddRange(yamlFiles);
        }

        // Check for Azure DevOps pipelines
        var azurePipelineYml = Path.Combine(currentDir, "azure-pipelines.yml");
        var azurePipelineYaml = Path.Combine(currentDir, "azure-pipelines.yaml");
        if (System.IO.File.Exists(azurePipelineYml))
        {
            detectedFiles.Add(azurePipelineYml);
        }
        if (System.IO.File.Exists(azurePipelineYaml))
        {
            detectedFiles.Add(azurePipelineYaml);
        }

        if (detectedFiles.Count == 0)
        {
            _output.WriteError("No pipeline files found in current directory.");
            _output.WriteLine();
            _output.WriteInfo("Expected locations:");
            _output.WriteLine("  .github/workflows/*.yml");
            _output.WriteLine("  .github/workflows/*.yaml");
            _output.WriteLine("  azure-pipelines.yml");
            _output.WriteLine("  azure-pipelines.yaml");
            _output.WriteLine();
            _output.WriteInfo("Use --file to specify a pipeline file explicitly.");
            return null;
        }

        if (detectedFiles.Count == 1)
        {
            _output.WriteInfo($"Auto-detected: {Path.GetRelativePath(currentDir, detectedFiles[0])}");
            return detectedFiles[0];
        }

        // Multiple files found - list them
        _output.WriteWarning("Multiple pipeline files found:");
        foreach (var file in detectedFiles)
        {
            _output.WriteLine($"  {Path.GetRelativePath(currentDir, file)}");
        }
        _output.WriteLine();
        _output.WriteInfo("Use --file to specify which pipeline to list.");
        return null;
    }

    /// <summary>
    /// Renders the pipeline in table format without step details.
    /// </summary>
    private void RenderTable(Pipeline pipeline)
    {
        WritePipelineHeader(pipeline);

        var table = new Table();
        table.AddColumn("Job ID");
        table.AddColumn("Name");
        table.AddColumn("Runs On");
        table.AddColumn("Steps");
        table.AddColumn("Dependencies");
        table.AddColumn("Condition");

        var sortedJobs = SortByDependencyOrder(pipeline.Jobs);

        foreach (var job in sortedJobs)
        {
            table.AddRow(
                Markup.Escape(job.Id),
                Markup.Escape(job.Name),
                Markup.Escape(job.RunsOn),
                job.Steps.Count.ToString(),
                FormatDependencies(job.DependsOn),
                FormatCondition(job.Condition)
            );
        }

        _console.Write(table);
    }

    /// <summary>
    /// Renders the pipeline in table format with detailed step information.
    /// </summary>
    private void RenderTableWithDetails(Pipeline pipeline)
    {
        WritePipelineHeader(pipeline);

        var sortedJobs = SortByDependencyOrder(pipeline.Jobs);

        foreach (var job in sortedJobs)
        {
            _output.WriteLine();
            _console.MarkupLine($"[bold]Job:[/] {Markup.Escape(job.Id)} ({Markup.Escape(job.RunsOn)})");
            _console.MarkupLine($"[dim]Dependencies:[/] {FormatDependencies(job.DependsOn)}");
            _console.MarkupLine($"[dim]Condition:[/] {FormatCondition(job.Condition)}");

            if (job.Steps.Count == 0)
            {
                _output.WriteInfo("No steps defined.");
                continue;
            }

            var table = new Table();
            table.AddColumn("#");
            table.AddColumn("Step Name");
            table.AddColumn("Type");
            table.AddColumn("Details");

            for (int i = 0; i < job.Steps.Count; i++)
            {
                var step = job.Steps[i];
                table.AddRow(
                    (i + 1).ToString(),
                    Markup.Escape(step.Name),
                    step.Type.ToString(),
                    Markup.Escape(GetStepDetails(step))
                );
            }

            _console.Write(table);
        }
    }

    /// <summary>
    /// Renders the pipeline in JSON format.
    /// </summary>
    private void RenderJson(Pipeline pipeline)
    {
        var sortedJobs = SortByDependencyOrder(pipeline.Jobs);

        var output = new PipelineJsonOutput
        {
            Name = pipeline.Name,
            Provider = pipeline.Provider.ToString(),
            Jobs = sortedJobs.Select(job => new JobJsonOutput
            {
                Id = job.Id,
                Name = job.Name,
                RunsOn = job.RunsOn,
                StepCount = job.Steps.Count,
                DependsOn = job.DependsOn,
                Condition = job.Condition?.Expression,
                Steps = Details
                    ? job.Steps.Select(s => new StepJsonOutput
                    {
                        Name = s.Name,
                        Type = s.Type.ToString(),
                        Script = s.Script,
                        With = s.With.Count > 0 ? s.With : null
                    }).ToList()
                    : null
            }).ToList()
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        var json = JsonSerializer.Serialize(output, options);
        _output.WriteLine(json);
    }

    /// <summary>
    /// Renders the pipeline in minimal format (job IDs only).
    /// </summary>
    private void RenderMinimal(Pipeline pipeline)
    {
        var sortedJobs = SortByDependencyOrder(pipeline.Jobs);

        foreach (var job in sortedJobs)
        {
            _output.WriteLine(job.Id);
        }
    }

    /// <summary>
    /// Writes the pipeline header information.
    /// </summary>
    private void WritePipelineHeader(Pipeline pipeline)
    {
        _console.MarkupLine($"[bold]Pipeline:[/] {Markup.Escape(pipeline.Name)}");
        _console.MarkupLine($"[bold]Provider:[/] {pipeline.Provider}");
        _output.WriteLine();
    }

    /// <summary>
    /// Sorts jobs by dependency order using topological sort.
    /// Jobs with dependencies appear after their dependencies.
    /// </summary>
    /// <param name="jobs">The jobs dictionary to sort.</param>
    /// <returns>Jobs sorted by dependency order.</returns>
    /// <exception cref="InvalidOperationException">Thrown when circular dependency detected.</exception>
    internal IEnumerable<Job> SortByDependencyOrder(Dictionary<string, Job> jobs)
    {
        var sorted = new List<Job>();
        var visited = new HashSet<string>();
        var visiting = new HashSet<string>(); // For cycle detection

        void Visit(string jobId)
        {
            if (visited.Contains(jobId)) return;
            if (visiting.Contains(jobId))
            {
                throw new InvalidOperationException($"Circular dependency detected involving job: {jobId}");
            }

            visiting.Add(jobId);

            if (jobs.TryGetValue(jobId, out var job))
            {
                foreach (var dep in job.DependsOn)
                {
                    Visit(dep);
                }

                visiting.Remove(jobId);
                visited.Add(jobId);
                sorted.Add(job);
            }
        }

        foreach (var jobId in jobs.Keys)
        {
            Visit(jobId);
        }

        return sorted;
    }

    /// <summary>
    /// Formats the dependencies list for display.
    /// </summary>
    internal string FormatDependencies(List<string> dependencies)
    {
        if (dependencies == null || dependencies.Count == 0)
        {
            return "-";
        }
        return string.Join(", ", dependencies);
    }

    /// <summary>
    /// Formats a condition for display.
    /// </summary>
    internal string FormatCondition(Condition? condition)
    {
        if (condition == null || string.IsNullOrEmpty(condition.Expression))
        {
            return "-";
        }
        return TruncateString(condition.Expression, 30);
    }

    /// <summary>
    /// Gets a brief description of the step for display.
    /// </summary>
    private string GetStepDetails(Step step)
    {
        // For script steps, show truncated script
        if (!string.IsNullOrEmpty(step.Script))
        {
            // Get first line and truncate
            var firstLine = step.Script.Split('\n')[0].Trim();
            return TruncateString(firstLine, 40);
        }

        // For steps with parameters, show key parameter
        if (step.With.Count > 0)
        {
            var firstParam = step.With.First();
            return TruncateString($"{firstParam.Key}: {firstParam.Value}", 40);
        }

        return "-";
    }

    /// <summary>
    /// Truncates a string to the specified length with ellipsis.
    /// </summary>
    internal string TruncateString(string? value, int maxLength = 30)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "-";
        }
        if (value.Length <= maxLength)
        {
            return value;
        }
        return value[..(maxLength - 3)] + "...";
    }

    /// <summary>
    /// Shows suggestions when a file is not found.
    /// </summary>
    private void ShowFileNotFoundSuggestions()
    {
        _output.WriteLine();
        _output.WriteInfo("Expected pipeline file locations:");
        _output.WriteLine("  .github/workflows/*.yml (GitHub Actions)");
        _output.WriteLine("  azure-pipelines.yml (Azure DevOps)");
        _output.WriteLine();
        _output.WriteInfo("Try running without --file to auto-detect pipeline files.");
    }

    // JSON output models
    private sealed class PipelineJsonOutput
    {
        public string Name { get; set; } = string.Empty;
        public string Provider { get; set; } = string.Empty;
        public List<JobJsonOutput> Jobs { get; set; } = [];
    }

    private sealed class JobJsonOutput
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string RunsOn { get; set; } = string.Empty;
        public int StepCount { get; set; }
        public List<string> DependsOn { get; set; } = [];
        public string? Condition { get; set; }
        public List<StepJsonOutput>? Steps { get; set; }
    }

    private sealed class StepJsonOutput
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string? Script { get; set; }
        public Dictionary<string, string>? With { get; set; }
    }
}
