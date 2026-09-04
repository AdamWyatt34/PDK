using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace PDK.Providers.Common;

/// <summary>
/// Lets a single YAML scalar populate a <c>List&lt;string&gt;</c> property (one item), which both providers allow
/// in several places (Azure <c>demands: java</c>, branch filters written as a single name, ...).
/// </summary>
public sealed class ScalarToListNodeDeserializer : INodeDeserializer
{
    /// <inheritdoc />
    public bool Deserialize(IParser reader, Type expectedType, Func<IParser, Type, object?> nestedObjectDeserializer, out object? value)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (expectedType != typeof(List<string>) || !reader.Accept<Scalar>(out var scalar))
        {
            value = null;
            return false;
        }

        reader.MoveNext();

        var list = new List<string>();
        if (!YamlValues.IsNullScalar(scalar))
        {
            list.Add(scalar.Value);
        }

        value = list;
        return true;
    }
}
