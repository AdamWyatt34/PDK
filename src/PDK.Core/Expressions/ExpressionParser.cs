namespace PDK.Core.Expressions;

/// <summary>
/// Recursive-descent parser for pipeline expressions (GitHub and Azure dialects share the grammar;
/// Azure simply uses functions such as <c>and()</c>/<c>eq()</c> instead of operators).
/// </summary>
public sealed class ExpressionParser
{
    private readonly string _source;
    private readonly List<Token> _tokens;
    private int _pos;

    private ExpressionParser(string source)
    {
        _source = source;
        _tokens = ExpressionTokenizer.Tokenize(source);
    }

    /// <summary>Parses <paramref name="expression"/> into an AST.</summary>
    /// <exception cref="ExpressionException">Thrown when the expression is malformed.</exception>
    public static ExpressionNode Parse(string expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        var parser = new ExpressionParser(expression);
        var node = parser.ParseOr();
        if (parser.Current.Kind != TokenKind.End)
        {
            throw new ExpressionException(expression, $"unexpected '{parser.Current.Text}' at position {parser.Current.Position}");
        }

        return node;
    }

    private Token Current => _tokens[_pos];

    private Token Advance() => _tokens[_pos++];

    private bool Match(string punct)
    {
        if (Current.Kind == TokenKind.Punct && Current.Text == punct)
        {
            _pos++;
            return true;
        }

        return false;
    }

    private void Expect(string punct)
    {
        if (!Match(punct))
        {
            throw new ExpressionException(_source, $"expected '{punct}' at position {Current.Position} but found '{Current.Text}'");
        }
    }

    private ExpressionNode ParseOr()
    {
        var left = ParseAnd();
        while (Current.Kind == TokenKind.Punct && Current.Text == "||")
        {
            Advance();
            var right = ParseAnd();
            left = new BinaryNode("||", left, right);
        }

        return left;
    }

    private ExpressionNode ParseAnd()
    {
        var left = ParseEquality();
        while (Current.Kind == TokenKind.Punct && Current.Text == "&&")
        {
            Advance();
            var right = ParseEquality();
            left = new BinaryNode("&&", left, right);
        }

        return left;
    }

    private ExpressionNode ParseEquality()
    {
        var left = ParseRelational();
        while (Current.Kind == TokenKind.Punct && Current.Text is "==" or "!=")
        {
            var op = Advance().Text;
            var right = ParseRelational();
            left = new BinaryNode(op, left, right);
        }

        return left;
    }

    private ExpressionNode ParseRelational()
    {
        var left = ParseUnary();
        while (Current.Kind == TokenKind.Punct && Current.Text is "<" or "<=" or ">" or ">=")
        {
            var op = Advance().Text;
            var right = ParseUnary();
            left = new BinaryNode(op, left, right);
        }

        return left;
    }

    private ExpressionNode ParseUnary()
    {
        if (Match("!"))
        {
            return new NotNode(ParseUnary());
        }

        return ParsePrimary();
    }

    private ExpressionNode ParsePrimary()
    {
        var token = Current;
        switch (token.Kind)
        {
            case TokenKind.Number:
                Advance();
                if (!ExpressionTokenizer.TryParseNumber(token.Text, out var number))
                {
                    throw new ExpressionException(_source, $"invalid number '{token.Text}'");
                }

                return new LiteralNode(number);

            case TokenKind.StringLiteral:
                Advance();
                return new LiteralNode(token.Text);

            case TokenKind.Punct when token.Text == "(":
                Advance();
                var inner = ParseOr();
                Expect(")");
                return WrapPostfix(inner);

            case TokenKind.Identifier:
                return ParseIdentifierExpression();

            default:
                throw new ExpressionException(_source, $"unexpected '{token.Text}' at position {token.Position}");
        }
    }

    private ExpressionNode ParseIdentifierExpression()
    {
        var ident = Advance();

        // Keywords
        switch (ident.Text.ToLowerInvariant())
        {
            case "true" when !IsFollowedByAccess():
                return new LiteralNode(true);
            case "false" when !IsFollowedByAccess():
                return new LiteralNode(false);
            case "null" when !IsFollowedByAccess():
                return new LiteralNode(null);
        }

        // Function call
        if (Current.Kind == TokenKind.Punct && Current.Text == "(")
        {
            Advance();
            var args = new List<ExpressionNode>();
            if (!Match(")"))
            {
                do
                {
                    args.Add(ParseOr());
                }
                while (Match(","));
                Expect(")");
            }

            return WrapPostfix(new FunctionCallNode(ident.Text, args));
        }

        // Context access
        var segments = ParseSegments();
        return new ContextAccessNode(ident.Text, segments);
    }

    private ExpressionNode WrapPostfix(ExpressionNode target)
    {
        var segments = ParseSegments();
        return segments.Count == 0 ? target : new MemberAccessNode(target, segments);
    }

    private List<AccessSegment> ParseSegments()
    {
        var segments = new List<AccessSegment>();
        while (true)
        {
            if (Match("."))
            {
                if (Match("*"))
                {
                    segments.Add(new WildcardSegment());
                    continue;
                }

                if (Current.Kind != TokenKind.Identifier)
                {
                    throw new ExpressionException(_source, $"expected a property name after '.' at position {Current.Position}");
                }

                segments.Add(new PropertySegment(Advance().Text));
                continue;
            }

            if (Match("["))
            {
                if (Match("*"))
                {
                    Expect("]");
                    segments.Add(new WildcardSegment());
                    continue;
                }

                var index = ParseOr();
                Expect("]");
                segments.Add(new IndexSegment(index));
                continue;
            }

            break;
        }

        return segments;
    }

    private bool IsFollowedByAccess() =>
        Current.Kind == TokenKind.Punct && Current.Text is "." or "[" or "(";
}
