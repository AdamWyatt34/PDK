using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PDK.Core.ErrorHandling;
using PDK.Core.Models;

namespace PDK.Core.Validation.Phases;

/// <summary>
/// Validates variable references and interpolation syntax.
/// </summary>
public partial class VariableValidationPhase : IValidationPhase
{
    private readonly ILogger<VariableValidationPhase> _logger;

    public string Name => "Variable Validation";
    public int Order => 3;

    // Matches ${NAME}, ${NAME:-default}, ${NAME:?error} using the runtime expander's grammar
    // (NAME = [A-Za-z_][A-Za-z0-9_]*). Escaped references (\${) and ${{ }} expressions are not
    // matched, and neither are bash parameter expansions such as ${VAR#x}, ${#VAR} or ${VAR:0:2}.
    [GeneratedRegex(@"(?<!\\)\$\{(?!\{)([A-Za-z_][A-Za-z0-9_]*)(?::([?-])([^}]*))?\}", RegexOptions.Compiled)]
    private static partial Regex VariableReferencePattern();

    // Matches ${{ expression }} (GitHub/Azure syntax)
    [GeneratedRegex(@"\$\{\{\s*([^}]+)\s*\}\}", RegexOptions.Compiled)]
    private static partial Regex ExpressionPattern();

    public VariableValidationPhase(ILogger<VariableValidationPhase> logger)
    {
        _logger = logger;
    }

    public Task<IReadOnlyList<DryRunValidationError>> ValidateAsync(
        Pipeline pipeline,
        ValidationContext context,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<DryRunValidationError>();

        _logger.LogDebug("Starting variable validation for pipeline: {PipelineName}", pipeline.Name);

        // Validate pipeline-level variables
        if (pipeline.Variables != null)
        {
            foreach (var (key, value) in pipeline.Variables)
            {
                ValidateVariableValue(value, null, null, null, context, errors);
            }
        }

        // Validate job and step variables
        foreach (var (jobId, job) in pipeline.Jobs)
        {
            // Job environment variables
            if (job.Environment != null)
            {
                foreach (var (key, value) in job.Environment)
                {
                    ValidateVariableValue(value, jobId, null, null, context, errors);
                }
            }

            // Job condition
            if (job.Condition?.Expression != null)
            {
                ValidateVariableValue(job.Condition.Expression, jobId, null, null, context, errors);
            }

            // Steps
            for (int i = 0; i < job.Steps.Count; i++)
            {
                var step = job.Steps[i];
                var stepIndex = i + 1;
                var stepName = step.Name ?? $"Step {stepIndex}";

                // Step environment variables
                if (step.Environment != null)
                {
                    foreach (var (key, value) in step.Environment)
                    {
                        ValidateVariableValue(value, jobId, stepName, stepIndex, context, errors);
                    }
                }

                // Step 'with' inputs
                if (step.With != null)
                {
                    foreach (var (key, value) in step.With)
                    {
                        ValidateVariableValue(value, jobId, stepName, stepIndex, context, errors);
                    }
                }

                // Step script content
                if (!string.IsNullOrEmpty(step.Script))
                {
                    ValidateVariableValue(step.Script, jobId, stepName, stepIndex, context, errors);
                }

                // Step condition
                if (step.Condition?.Expression != null)
                {
                    ValidateVariableValue(step.Condition.Expression, jobId, stepName, stepIndex, context, errors);
                }

                // Step working directory
                if (!string.IsNullOrEmpty(step.WorkingDirectory))
                {
                    ValidateVariableValue(step.WorkingDirectory, jobId, stepName, stepIndex, context, errors);
                }
            }
        }

        _logger.LogDebug("Variable validation completed with {ErrorCount} errors", errors.Count);

        return Task.FromResult<IReadOnlyList<DryRunValidationError>>(errors);
    }

    private void ValidateVariableValue(
        string value,
        string? jobId,
        string? stepName,
        int? stepIndex,
        ValidationContext context,
        List<DryRunValidationError> errors)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        // Check for ${VAR} style references
        var varMatches = VariableReferencePattern().Matches(value);
        foreach (Match match in varMatches)
        {
            var varName = match.Groups[1].Value;
            var modifier = match.Groups[2].Value;
            var modifierValue = match.Groups[3].Value;

            // If variable has default value (:-), don't require it to be defined
            if (modifier == "-")
            {
                continue;
            }

            // If variable has required marker (:?), it must be defined
            if (modifier == "?")
            {
                if (context.VariableResolver != null && !context.VariableResolver.ContainsVariable(varName))
                {
                    errors.Add(DryRunValidationError.VariableError(
                        ErrorCodes.VariableRequired,
                        $"Required variable '{varName}' is not defined" +
                        (string.IsNullOrEmpty(modifierValue) ? "" : $": {modifierValue}"),
                        jobId: jobId,
                        stepName: stepName,
                        suggestions: new[]
                        {
                            $"Define the variable using --var {varName}=value",
                            $"Add the variable to your configuration file",
                            $"Set the environment variable PDK_VAR_{varName}"
                        }));
                }
                continue;
            }

            // Plain ${VAR} - check if defined (warning, not error)
            if (context.VariableResolver != null && !context.VariableResolver.ContainsVariable(varName))
            {
                // Only warn for plain variable references
                errors.Add(DryRunValidationError.Warning(
                    ErrorCodes.VariableRequired,
                    $"Variable '{varName}' is not defined and has no default value",
                    ValidationCategory.Variable,
                    jobId: jobId,
                    stepName: stepName,
                    suggestions: new[]
                    {
                        $"Define the variable using --var {varName}=value",
                        $"Use default value syntax: ${{{varName}:-default}}"
                    }));
            }
        }

        // Check for ${{ expression }} style references (GitHub/Azure syntax)
        // These are typically resolved at runtime, so we just validate syntax
        var exprMatches = ExpressionPattern().Matches(value);
        foreach (Match match in exprMatches)
        {
            var expression = match.Groups[1].Value.Trim();

            // Validate expression syntax
            if (!ValidateExpressionSyntax(expression, out var syntaxError))
            {
                errors.Add(DryRunValidationError.VariableError(
                    ErrorCodes.VariableInvalidSyntax,
                    $"Invalid expression syntax: {syntaxError}",
                    jobId: jobId,
                    stepName: stepName,
                    suggestions: new[]
                    {
                        "Check the expression for syntax errors",
                        "See documentation for supported expression syntax"
                    }));
            }
        }

        // Check for unclosed references (escaped \${ is ignored)
        var (unclosedVariable, unclosedExpression) = FindUnclosedReferences(value);

        if (unclosedVariable)
        {
            errors.Add(DryRunValidationError.VariableError(
                ErrorCodes.VariableInvalidSyntax,
                "Unclosed variable reference (missing '}')",
                jobId: jobId,
                stepName: stepName,
                suggestions: "Ensure all ${ references are closed with }"));
        }

        if (unclosedExpression)
        {
            errors.Add(DryRunValidationError.VariableError(
                ErrorCodes.VariableInvalidSyntax,
                "Unclosed expression reference (missing '}}')",
                jobId: jobId,
                stepName: stepName,
                suggestions: "Ensure all ${{ expressions are closed with }}"));
        }
    }

    /// <summary>
    /// Scans for <c>${</c> / <c>${{</c> openings without a matching closing brace.
    /// </summary>
    private static (bool UnclosedVariable, bool UnclosedExpression) FindUnclosedReferences(string value)
    {
        var unclosedVariable = false;
        var unclosedExpression = false;

        var position = 0;
        while ((position = value.IndexOf("${", position, StringComparison.Ordinal)) >= 0)
        {
            var escaped = position > 0 && value[position - 1] == '\\';
            var isExpression = position + 2 < value.Length && value[position + 2] == '{';

            if (!escaped)
            {
                if (isExpression)
                {
                    if (value.IndexOf("}}", position + 3, StringComparison.Ordinal) < 0)
                    {
                        unclosedExpression = true;
                    }
                }
                else if (value.IndexOf('}', position + 2) < 0)
                {
                    unclosedVariable = true;
                }
            }

            position += isExpression ? 3 : 2;
        }

        return (unclosedVariable, unclosedExpression);
    }

    private static bool ValidateExpressionSyntax(string expression, out string? error)
    {
        return ExpressionSyntaxChecker.Validate(expression, out error);
    }
}
