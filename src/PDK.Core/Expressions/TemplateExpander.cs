using System.Text;

namespace PDK.Core.Expressions;

/// <summary>
/// Replaces expression placeholders inside pipeline text:
/// GitHub <c>${{ expr }}</c>; Azure <c>${{ expr }}</c> (template), <c>$[ expr ]</c> (runtime) and <c>$(name)</c> macros.
/// </summary>
public static class TemplateExpander
{
    /// <summary>True when <paramref name="text"/> contains any placeholder the expander would touch.</summary>
    public static bool ContainsPlaceholders(string? text, ExpressionSyntax syntax)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        if (text.Contains("${{", StringComparison.Ordinal))
        {
            return true;
        }

        return syntax == ExpressionSyntax.Azure &&
               (text.Contains("$[", StringComparison.Ordinal) || text.Contains("$(", StringComparison.Ordinal));
    }

    /// <summary>
    /// Expands every placeholder in <paramref name="text"/>. Unknown Azure macros are left as-is.
    /// </summary>
    /// <exception cref="ExpressionException">Thrown when an expression inside a placeholder is invalid.</exception>
    public static string Expand(string? text, ExpressionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrEmpty(text) || !ContainsPlaceholders(text, context.Syntax))
        {
            return text ?? string.Empty;
        }

        var sb = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            // ${{ expr }}
            if (StartsWithAt(text, i, "${{"))
            {
                var close = text.IndexOf("}}", i + 3, StringComparison.Ordinal);
                if (close > 0)
                {
                    var expression = text[(i + 3)..close];
                    sb.Append(ExpressionValue.ToText(ExpressionEvaluator.Evaluate(expression, context)));
                    i = close + 2;
                    continue;
                }
            }

            if (context.Syntax == ExpressionSyntax.Azure)
            {
                // $[ expr ]
                if (StartsWithAt(text, i, "$["))
                {
                    var close = FindClosing(text, i + 2, '[', ']');
                    if (close > 0)
                    {
                        var expression = text[(i + 2)..close];
                        sb.Append(ExpressionValue.ToText(ExpressionEvaluator.Evaluate(expression, context)));
                        i = close + 1;
                        continue;
                    }
                }

                // $(name) — only known variables are replaced; anything else (shell command
                // substitution, unknown names) is left untouched, as Azure does.
                if (StartsWithAt(text, i, "$("))
                {
                    var close = text.IndexOf(')', i + 2);
                    if (close > 0)
                    {
                        var name = text[(i + 2)..close];
                        if (IsMacroName(name))
                        {
                            var value = context.ResolveMacro(name);
                            if (value != null)
                            {
                                sb.Append(value);
                                i = close + 1;
                                continue;
                            }
                        }
                    }
                }
            }

            sb.Append(text[i]);
            i++;
        }

        return sb.ToString();
    }

    private static bool StartsWithAt(string text, int index, string token) =>
        string.CompareOrdinal(text, index, token, 0, token.Length) == 0;

    private static int FindClosing(string text, int start, char open, char close)
    {
        var depth = 1;
        for (var i = start; i < text.Length; i++)
        {
            if (text[i] == open)
            {
                depth++;
            }
            else if (text[i] == close)
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private static bool IsMacroName(string name)
    {
        if (name.Length == 0 || name.Length > 200)
        {
            return false;
        }

        foreach (var c in name)
        {
            if (!(char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '-'))
            {
                return false;
            }
        }

        return true;
    }
}
