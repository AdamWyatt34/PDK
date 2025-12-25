using PDK.CLI.UI;
using PDK.Core.Validation;
using Spectre.Console;

namespace PDK.CLI.DryRun;

/// <summary>
/// Formats and displays dry-run results in a human-readable tree format.
/// </summary>
public class DryRunUI
{
    private readonly IAnsiConsole _console;
    private readonly IConsoleOutput _output;

    public DryRunUI(IAnsiConsole console, IConsoleOutput output)
    {
        _console = console;
        _output = output;
    }

    /// <summary>
    /// Displays the dry-run result to the console.
    /// </summary>
    public void Display(DryRunResult result)
    {
        _console.WriteLine();
        _console.MarkupLine("[cyan bold]Dry-Run Mode: Validating execution plan[/]");
        _console.WriteLine();

        if (result.IsValid && result.ExecutionPlan != null)
        {
            DisplayExecutionPlan(result.ExecutionPlan);
        }

        if (result.Warnings.Count > 0)
        {
            DisplayWarnings(result.Warnings);
        }

        if (!result.IsValid)
        {
            DisplayErrors(result.Errors);
        }

        DisplaySummary(result);
    }

    private void DisplayExecutionPlan(ExecutionPlan plan)
    {
        // Pipeline header
        _console.MarkupLine($"[bold]Pipeline:[/] {Markup.Escape(plan.PipelineName)}");
        _console.MarkupLine($"[bold]File:[/] {Markup.Escape(plan.FilePath)}");
        _console.MarkupLine($"[bold]Provider:[/] {plan.Provider}");
        _console.WriteLine();

        // Display resolved variables if any (limited to first 5)
        if (plan.ResolvedVariables.Count > 0)
        {
            _console.MarkupLine("[dim]Variables:[/]");
            var displayVars = plan.ResolvedVariables.Take(5);
            foreach (var (key, value) in displayVars)
            {
                _console.MarkupLine($"  [dim]{Markup.Escape(key)}:[/] {Markup.Escape(TruncateValue(value, 50))}");
            }
            if (plan.ResolvedVariables.Count > 5)
            {
                _console.MarkupLine($"  [dim]... and {plan.ResolvedVariables.Count - 5} more[/]");
            }
            _console.WriteLine();
        }

        // Display jobs
        foreach (var job in plan.Jobs)
        {
            DisplayJob(job);
        }
    }

    private void DisplayJob(JobPlanNode job)
    {
        // Job header with execution order
        var orderPrefix = job.ExecutionOrder > 0 ? $"[{job.ExecutionOrder}] " : "";
        _console.MarkupLine($"[cyan bold]{orderPrefix}Job: {Markup.Escape(job.JobName)}[/] [dim]({Markup.Escape(job.RunsOn)})[/]");

        // Dependencies
        if (job.DependsOn.Count > 0)
        {
            _console.MarkupLine($"  [dim]Dependencies:[/] {string.Join(", ", job.DependsOn.Select(d => Markup.Escape(d)))}");
        }
        else
        {
            _console.MarkupLine("  [dim]Dependencies:[/] none");
        }

        // Container image if applicable
        if (!string.IsNullOrEmpty(job.ContainerImage))
        {
            _console.MarkupLine($"  [dim]Container:[/] {Markup.Escape(job.ContainerImage)}");
        }

        // Condition if applicable
        if (!string.IsNullOrEmpty(job.Condition))
        {
            _console.MarkupLine($"  [dim]Condition:[/] {Markup.Escape(TruncateValue(job.Condition, 60))}");
        }

        // Steps
        _console.MarkupLine("  [bold]Steps:[/]");
        foreach (var step in job.Steps)
        {
            DisplayStep(step);
        }

        _console.WriteLine();
    }

    private void DisplayStep(StepPlanNode step)
    {
        // Step with type and executor
        var stepColor = GetStepTypeColor(step.TypeName);
        _console.MarkupLine($"    [{stepColor}][{step.Index}][/] {Markup.Escape(step.StepName)}");
        _console.MarkupLine($"        [dim]Type:[/] {step.TypeName} [dim]->[/] {Markup.Escape(step.ExecutorName)}");

        // Shell if applicable
        if (!string.IsNullOrEmpty(step.Shell))
        {
            _console.MarkupLine($"        [dim]Shell:[/] {Markup.Escape(step.Shell)}");
        }

        // Working directory if specified
        if (!string.IsNullOrEmpty(step.WorkingDirectory))
        {
            _console.MarkupLine($"        [dim]Working Dir:[/] {Markup.Escape(step.WorkingDirectory)}");
        }

        // Script preview if applicable
        if (!string.IsNullOrEmpty(step.ScriptPreview))
        {
            _console.MarkupLine($"        [dim]Command:[/] {Markup.Escape(step.ScriptPreview)}");
        }

        // Inputs (limited to first 3)
        if (step.Inputs.Count > 0)
        {
            _console.MarkupLine("        [dim]Inputs:[/]");
            var displayInputs = step.Inputs.Take(3);
            foreach (var (key, value) in displayInputs)
            {
                _console.MarkupLine($"          {Markup.Escape(key)}: {Markup.Escape(TruncateValue(value, 40))}");
            }
            if (step.Inputs.Count > 3)
            {
                _console.MarkupLine($"          [dim]... and {step.Inputs.Count - 3} more[/]");
            }
        }

        // Condition if applicable
        if (!string.IsNullOrEmpty(step.Condition))
        {
            _console.MarkupLine($"        [dim]Condition:[/] {Markup.Escape(TruncateValue(step.Condition, 50))}");
        }

        // Continue on error
        if (step.ContinueOnError)
        {
            _console.MarkupLine("        [yellow]Continue on error: yes[/]");
        }

        // Dependencies
        if (step.Needs.Count > 0)
        {
            _console.MarkupLine($"        [dim]Needs:[/] {string.Join(", ", step.Needs.Select(n => Markup.Escape(n)))}");
        }
    }

    private void DisplayWarnings(IReadOnlyList<DryRunValidationError> warnings)
    {
        _console.WriteLine();
        _console.MarkupLine($"[yellow bold]Warnings ({warnings.Count}):[/]");

        var groupedWarnings = warnings.GroupBy(w => w.Category);
        foreach (var group in groupedWarnings)
        {
            _console.MarkupLine($"  [yellow]{group.Key}:[/]");
            foreach (var warning in group)
            {
                DisplayValidationError(warning, "yellow");
            }
        }
    }

    private void DisplayErrors(IReadOnlyList<DryRunValidationError> errors)
    {
        _console.WriteLine();
        _console.MarkupLine($"[red bold]Errors ({errors.Count}):[/]");

        var groupedErrors = errors.GroupBy(e => e.Category);
        foreach (var group in groupedErrors)
        {
            _console.MarkupLine($"  [red]{group.Key} Errors ({group.Count()}):[/]");
            foreach (var error in group)
            {
                DisplayValidationError(error, "red");
            }
        }
    }

    private void DisplayValidationError(DryRunValidationError error, string color)
    {
        var location = BuildLocationString(error);
        _console.MarkupLine($"    [{color}]x[/] {Markup.Escape(error.Message)}");

        if (!string.IsNullOrEmpty(location))
        {
            _console.MarkupLine($"      [dim]Location: {Markup.Escape(location)}[/]");
        }

        _console.MarkupLine($"      [dim]Code: {error.ErrorCode}[/]");

        if (error.Suggestions.Count > 0)
        {
            _console.MarkupLine("      [dim]Suggestions:[/]");
            foreach (var suggestion in error.Suggestions)
            {
                _console.MarkupLine($"        [dim]- {Markup.Escape(suggestion)}[/]");
            }
        }
    }

    private void DisplaySummary(DryRunResult result)
    {
        _console.WriteLine();

        if (result.IsValid && result.ExecutionPlan != null)
        {
            _console.MarkupLine($"[green bold]Validation: PASSED[/]");
            _console.MarkupLine($"  {result.ExecutionPlan.TotalJobs} job(s), {result.ExecutionPlan.TotalSteps} step(s)");
        }
        else
        {
            _console.MarkupLine($"[red bold]Validation: FAILED[/]");
            _console.MarkupLine($"  {result.Errors.Count} error(s), {result.Warnings.Count} warning(s)");
        }

        _console.MarkupLine($"  [dim]Duration: {result.ValidationDuration.TotalMilliseconds:F0}ms[/]");
    }

    private static string BuildLocationString(DryRunValidationError error)
    {
        var parts = new List<string>();

        if (!string.IsNullOrEmpty(error.JobId))
        {
            parts.Add($"Job '{error.JobId}'");
        }

        if (!string.IsNullOrEmpty(error.StepName))
        {
            var stepPart = error.StepIndex.HasValue
                ? $"Step {error.StepIndex}: '{error.StepName}'"
                : $"Step '{error.StepName}'";
            parts.Add(stepPart);
        }

        if (error.LineNumber.HasValue)
        {
            parts.Add($"Line {error.LineNumber}");
        }

        return string.Join(", ", parts);
    }

    private static string GetStepTypeColor(string typeName)
    {
        return typeName.ToLowerInvariant() switch
        {
            "checkout" => "blue",
            "script" or "bash" or "pwsh" => "green",
            "docker" => "magenta",
            "npm" or "dotnet" or "python" or "maven" or "gradle" => "cyan",
            "uploadartifact" or "downloadartifact" => "yellow",
            _ => "white"
        };
    }

    private static string TruncateValue(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (value.Length <= maxLength) return value;
        return value[..(maxLength - 3)] + "...";
    }
}
