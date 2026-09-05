namespace PDK.Core.Expressions;

/// <summary>Base type for parsed expression nodes.</summary>
public abstract record ExpressionNode;

/// <summary>A literal value (string, number, boolean or null).</summary>
public sealed record LiteralNode(object? Value) : ExpressionNode;

/// <summary>One segment of a context access path.</summary>
public abstract record AccessSegment;

/// <summary>Property access: <c>.name</c>.</summary>
public sealed record PropertySegment(string Name) : AccessSegment;

/// <summary>Index access: <c>[expr]</c>.</summary>
public sealed record IndexSegment(ExpressionNode Index) : AccessSegment;

/// <summary>Wildcard access: <c>.*</c> (object filter).</summary>
public sealed record WildcardSegment : AccessSegment;

/// <summary>Context access such as <c>github.event.pull_request.head.ref</c> or <c>variables['Build.SourceBranch']</c>.</summary>
public sealed record ContextAccessNode(string Root, IReadOnlyList<AccessSegment> Segments) : ExpressionNode;

/// <summary>A function call such as <c>contains(github.ref, 'main')</c>.</summary>
public sealed record FunctionCallNode(string Name, IReadOnlyList<ExpressionNode> Arguments) : ExpressionNode;

/// <summary>Member access on an arbitrary expression, e.g. <c>fromJSON(x).name</c> or <c>(expr)[0]</c>.</summary>
public sealed record MemberAccessNode(ExpressionNode Target, IReadOnlyList<AccessSegment> Segments) : ExpressionNode;

/// <summary>Logical negation <c>!expr</c>.</summary>
public sealed record NotNode(ExpressionNode Operand) : ExpressionNode;

/// <summary>Binary operator node for <c>== != &lt; &lt;= &gt; &gt;= &amp;&amp; ||</c>.</summary>
public sealed record BinaryNode(string Operator, ExpressionNode Left, ExpressionNode Right) : ExpressionNode;
