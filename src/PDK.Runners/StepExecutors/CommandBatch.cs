namespace PDK.Runners.StepExecutors;

using System.Text;
using PDK.Runners.Models;

/// <summary>
/// Runs several command lines for one step and aggregates their output into a single result.
/// The first non-zero exit code wins.
/// </summary>
internal static class CommandBatch
{
    public static async Task<StepExecutionResult> RunAsync(
        string stepName,
        IReadOnlyList<string> commandLines,
        Func<string, Task<ExecutionResult>> execute,
        DateTimeOffset startTime,
        bool stopOnFailure,
        IEnumerable<string>? notes = null)
    {
        var output = new StringBuilder();
        var errors = new StringBuilder();
        var exitCode = 0;

        if (notes != null)
        {
            foreach (var note in notes.Where(n => !string.IsNullOrWhiteSpace(n)))
            {
                output.AppendLine(note);
            }
        }

        foreach (var commandLine in commandLines)
        {
            if (commandLines.Count > 1)
            {
                output.AppendLine($"$ {commandLine}");
            }

            var result = await execute(commandLine).ConfigureAwait(false);

            AppendBlock(output, result.StandardOutput);
            AppendBlock(errors, result.StandardError);

            if (exitCode == 0 && result.ExitCode != 0)
            {
                exitCode = result.ExitCode;
            }

            if (!result.Success && stopOnFailure)
            {
                break;
            }
        }

        var endTime = DateTimeOffset.Now;
        return new StepExecutionResult
        {
            StepName = stepName,
            Success = exitCode == 0,
            ExitCode = exitCode,
            Output = output.ToString(),
            ErrorOutput = errors.ToString(),
            Duration = endTime - startTime,
            StartTime = startTime,
            EndTime = endTime
        };
    }

    private static void AppendBlock(StringBuilder builder, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        builder.Append(text);
        if (!text.EndsWith('\n'))
        {
            builder.AppendLine();
        }
    }
}
