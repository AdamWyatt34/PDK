using PDK.Core.Logging;
using PDK.Core.Variables;

namespace PDK.Runners;

/// <summary>
/// Helpers shared by the Docker and host job runners.
/// </summary>
public static class JobRunnerSupport
{
    /// <summary>
    /// Merges the variables and secrets known to the resolver (configuration, CLI, <c>PDK_VAR_*</c>,
    /// stored secrets) into the run context so that every step sees them, both as expression
    /// contexts and as exported environment variables. Values already present on the context win.
    /// </summary>
    public static JobRunContext WithResolverVariables(JobRunContext context, IVariableResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(resolver);

        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        var secrets = new Dictionary<string, string>(StringComparer.Ordinal);

        var known = resolver.GetAllVariables();
        foreach (var (name, value) in known ?? new Dictionary<string, string>())
        {
            var source = resolver.GetSource(name);
            switch (source)
            {
                case VariableSource.Secret:
                    secrets[name] = value;
                    break;
                case VariableSource.BuiltIn:
                    // PDK_* built-ins are added by the environment builder with step-accurate values
                    break;
                default:
                    variables[name] = value;
                    break;
            }
        }

        foreach (var (k, v) in context.Variables)
        {
            variables[k] = v;
        }

        foreach (var (k, v) in context.Secrets)
        {
            secrets[k] = v;
        }

        return context with { Variables = variables, Secrets = secrets };
    }

    /// <summary>
    /// Wraps a live-output callback so every line is masked before it reaches the console.
    /// </summary>
    public static Action<string>? MaskingOutputHandler(Action<string>? handler, ISecretMasker masker, JobExecutionSession session)
    {
        if (handler == null)
        {
            return null;
        }

        ArgumentNullException.ThrowIfNull(masker);
        ArgumentNullException.ThrowIfNull(session);

        return line =>
        {
            var masked = masker.MaskSecrets(line);
            foreach (var value in session.AdditionalMaskValues)
            {
                if (value.Length > 0)
                {
                    masked = masked.Replace(value, "***", StringComparison.Ordinal);
                }
            }

            handler(masked);
        };
    }

    /// <summary>
    /// Masks registered secrets and dynamically added mask values in a step result.
    /// </summary>
    public static StepExecutionResult MaskResult(StepExecutionResult result, ISecretMasker masker, JobExecutionSession? session)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(masker);

        string Mask(string text)
        {
            var masked = masker.MaskSecrets(text);
            if (session != null)
            {
                foreach (var value in session.AdditionalMaskValues)
                {
                    if (value.Length > 0)
                    {
                        masked = masked.Replace(value, "***", StringComparison.Ordinal);
                    }
                }
            }

            return masked;
        }

        return result with
        {
            Output = Mask(result.Output),
            ErrorOutput = Mask(result.ErrorOutput)
        };
    }

    /// <summary>
    /// Whether a job result should be reported as successful: every step succeeded, was skipped,
    /// or failed with <c>continue-on-error</c>.
    /// </summary>
    public static bool AllStepsCountAsSuccess(IEnumerable<StepExecutionResult> results) =>
        results.All(r => r.CountsAsSuccess);
}
