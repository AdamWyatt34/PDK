using YamlDotNet.Core;

namespace PDK.Providers.Common;

/// <summary>
/// Where a node of a rewritten YAML document comes from: the file it was read from (or the placeholder name of
/// inline content) and the position of the source node in that file. Nodes synthesized from template expressions
/// carry the position of the expression that produced them.
/// </summary>
/// <param name="File">The file path or placeholder name.</param>
/// <param name="Start">The start position of the source node.</param>
/// <param name="End">The end position of the source node.</param>
public sealed record YamlNodeOrigin(string File, Mark Start, Mark End);
