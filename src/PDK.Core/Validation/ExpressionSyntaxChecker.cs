namespace PDK.Core.Validation;

/// <summary>
/// Performs the lightweight syntax checks applied to <c>${{ }}</c> expressions and condition
/// expressions: balanced parentheses and balanced quotes. Characters inside string literals are
/// ignored, so <c>contains(github.ref, ')')</c> or <c>eq(x, "it's")</c> are accepted.
/// </summary>
public static class ExpressionSyntaxChecker
{
    /// <summary>
    /// Validates the expression syntax.
    /// </summary>
    /// <param name="expression">The expression text (without the surrounding <c>${{ }}</c>).</param>
    /// <param name="error">The error description when the expression is invalid.</param>
    /// <returns>True when the expression passes the checks.</returns>
    public static bool Validate(string? expression, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(expression))
        {
            error = "Expression is empty";
            return false;
        }

        var depth = 0;
        char? quote = null;

        for (var i = 0; i < expression.Length; i++)
        {
            var c = expression[i];

            if (quote != null)
            {
                if (c == quote)
                {
                    // A doubled single quote is an escaped quote inside a single-quoted literal ('it''s').
                    if (quote == '\'' && i + 1 < expression.Length && expression[i + 1] == '\'')
                    {
                        i++;
                        continue;
                    }

                    quote = null;
                }
                else if (c == '\\' && quote == '"' && i + 1 < expression.Length)
                {
                    // Backslash escape inside a double-quoted literal.
                    i++;
                }

                continue;
            }

            switch (c)
            {
                case '\'':
                case '"':
                    quote = c;
                    break;
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    if (depth < 0)
                    {
                        error = "Unbalanced parentheses";
                        return false;
                    }
                    break;
            }
        }

        if (quote == '\'')
        {
            error = "Unbalanced single quotes";
            return false;
        }

        if (quote == '"')
        {
            error = "Unbalanced double quotes";
            return false;
        }

        if (depth != 0)
        {
            error = "Unbalanced parentheses";
            return false;
        }

        return true;
    }
}
