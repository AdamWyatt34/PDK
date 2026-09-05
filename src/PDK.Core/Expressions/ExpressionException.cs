using PDK.Core.Models;

namespace PDK.Core.Expressions;

/// <summary>
/// Thrown when a pipeline expression cannot be parsed or evaluated.
/// </summary>
public class ExpressionException : PdkException
{
    /// <summary>Error code for expression failures.</summary>
    public const string Code = "PDK-E-EXPR-001";

    /// <summary>Gets the expression text that failed.</summary>
    public string Expression { get; }

    /// <summary>Creates a new expression exception.</summary>
    public ExpressionException(string expression, string message)
        : base(Code, $"Expression '{Truncate(expression)}': {message}", context: null, suggestions: new[]
        {
            "Check the expression syntax for your CI provider",
            "Supported: literals, context access (github.*, env.*, secrets.*, matrix.*, steps.*, needs.*, variables.*), functions and the operators ! == != < <= > >= && ||"
        })
    {
        Expression = expression;
    }

    private static string Truncate(string s) => s.Length <= 80 ? s : s[..77] + "...";
}
