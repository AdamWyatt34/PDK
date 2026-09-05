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
                    return ExitCodes.FileNotFound;
                }
                filePath = File.FullName;
            }
            else
            {
                var detectedFile = AutoDetectPipeline();
                if (detectedFile == null)
                {
                    return ExitCodes.FileNotFound; // Error already displayed in AutoDetectPipeline
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
    /// Auto-detects the pipeline file in the current directory using the shared
    /// <see cref="PipelineFileLocator"/> search rules.
    /// </summary>
    /// <returns>The path to the detected file, or null if none (or several) were found.</returns>
    private string? AutoDetectPipeline()
    {
        var currentDir = Directory.GetCurrentDirectory();
        var detectedFiles = PipelineFileLocator.Discover(currentDir);

        if (detectedFiles.Count == 0)
        {
            _output.WriteError("No pipeline files found in current directory.");
            _output.WriteLine();
            _output.WriteInfo("Expected locations:");
            foreach (var pattern in PipelineFileLocator.SearchDescriptions)
            {
                _output.WriteLine($"  {pattern}");
            }
            _output.WriteLine();
            _output.WriteInfo("Use --file to specify a pipeline file explicitly.");
            return null;
        }

        if (detectedFiles.Count == 1)
        {
            _output.WriteInfo($"Auto-detected: {detectedFiles[0]}");
            return Path.Combine(currentDir, detectedFiles[0]);
        }

        // Multiple files found - list them
        _output.WriteWarning("Multiple pipeline files found:");
        foreach (var file in detectedFiles)
        {
            _output.WriteLine($"  {file}");
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
            // Table cells are markup: escape every user-controlled value
            table.AddRow(
                Markup.Escape(job.Id),
                Markup.Escape(job.Name),
                Markup.Escape(job.RunsOn),
                job.Steps.Count.ToString(),
                Markup.Escape(FormatDependencies(job.DependsOn)),
                Markup.Escape(FormatCondition(job.Condition))
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
            if (!string.IsNullOrEmpty(job.Stage))
            {
                _console.MarkupLine($"[dim]Stage:[/] {Markup.Escape(job.Stage)}");
            }
            if (!string.IsNullOrEmpty(job.Container))
            {
                _console.MarkupLine($"[dim]Container:[/] {Markup.Escape(job.Container)}");
            }
            _console.MarkupLine($"[dim]Dependencies:[/] {Markup.Escape(FormatDependencies(job.DependsOn))}");
            _console.MarkupLine($"[dim]Condition:[/] {Markup.Escape(FormatCondition(job.Condition))}");

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
                var typeText = step.Enabled ? step.Type.ToString() : $"{step.Type} (disabled)";
                table.AddRow(
                    (i + 1).ToString(),
                    Markup.Escape(step.Name),
                    Markup.Escape(typeText),
                    Markup.Escape(GetStepDetails(step))
                );
            }

            _console.Write(table);
        }
    }

    /// <summary>
    /// Renders the pipeline in JSON format. Steps are always included (id, name, type, enabled,
    /// actionReference); <c>--details</c> adds the script and inputs.
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
                Stage = job.Stage,
                Container = job.Container,
                Matrix = job.Matrix is { Count: > 0 } ? job.Matrix : null,
                StepCount = job.Steps.Count,
                DependsOn = job.DependsOn,
                Condition = job.Condition?.Expression,
                Steps = job.Steps.Select(s => new StepJsonOutput
                {
                    Id = s.Id,
                    Name = s.Name,
                    Type = s.Type.ToString(),
                    Enabled = s.Enabled,
                    ActionReference = s.ActionReference,
                    Script = Details ? s.Script : null,
                    With = Details && s.With.Count > 0 ? s.With : null
                }).ToList()
            }).ToList()
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        var json = JsonSerializer.Serialize(output, options);

        // Raw write: the renderer would wrap long lines and break the JSON
        _console.Profile.Out.Writer.WriteLine(json);
        _console.Profile.Out.Writer.Flush();
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
        _console.MarkupLine($"[bold]Provider:[/] {Markup.Escape(pipeline.Provider.ToString())}");
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

        // Parse-time decisions (GitLab rules/only/except, manual jobs) explain themselves better than "false"
        if (!string.IsNullOrWhiteSpace(condition.Description))
        {
            return TruncateString(condition.Description, 30);
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

        // For action/task steps, show the reference
        if (!string.IsNullOrEmpty(step.ActionReference))
        {
            return TruncateString(step.ActionReference, 40);
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
        public string? Stage { get; set; }
        public string? Container { get; set; }
        public Dictionary<string, string>? Matrix { get; set; }
        public int StepCount { get; set; }
        public List<string> DependsOn { get; set; } = [];
        public string? Condition { get; set; }
        public List<StepJsonOutput> Steps { get; set; } = [];
    }

    private sealed class StepJsonOutput
    {
        public string? Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public string? ActionReference { get; set; }
        public string? Script { get; set; }
        public Dictionary<string, string>? With { get; set; }
    }
}
