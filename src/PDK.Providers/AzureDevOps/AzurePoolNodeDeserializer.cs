using PDK.Providers.AzureDevOps.Models;
using PDK.Providers.Common;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace PDK.Providers.AzureDevOps;

/// <summary>
/// Accepts the string form of <c>pool:</c> (<c>pool: Default</c>, <c>pool: 'ubuntu-latest'</c>) in addition to the
/// mapping form, which is handled by YamlDotNet's default object deserializer.
/// </summary>
public sealed class AzurePoolNodeDeserializer : INodeDeserializer
{
    /// <inheritdoc />
    public bool Deserialize(IParser reader, Type expectedType, Func<IParser, Type, object?> nestedObjectDeserializer, out object? value)
    {
        ArgumentNullException.ThrowIfNull(reader);

        if (expectedType != typeof(AzurePool) || !reader.Accept<Scalar>(out var scalar))
        {
            value = null;
            return false;
        }

        reader.MoveNext();
        value = YamlValues.IsNullScalar(scalar) ? null : AzurePool.FromString(scalar.Value);
        return true;
    }
}
