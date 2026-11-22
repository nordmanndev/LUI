using Nordmann.LUI.Schema;

namespace Nordmann.LUI.Querying;

/// <summary>
/// The serializer that the registry holds for one payload type, with what an embedded index stores about it (ADR-0072): the
/// name of the type and the format of the serializer. Built once per registration (or once per type that a context knows), and
/// shared by every index of the catalog that has that payload type.
/// </summary>
internal abstract class PayloadCodec : PayloadSerialization
{
    /// <summary>The stable name of the payload type: the value of <c>[LuiPayloadName]</c>, or the computed name.</summary>
    internal abstract string TypeName { get; }

    /// <summary>The format of the serializer, as it was when the serializer was registered.</summary>
    internal abstract string Format { get; }
}

/// <summary>The codec of one payload type.</summary>
/// <typeparam name="TPayload">The payload type.</typeparam>
internal sealed class PayloadCodec<TPayload> : PayloadCodec
{
    /// <summary>Takes the serializer, reads its format once, and computes the name of the type.</summary>
    /// <param name="serializer">The serializer of the type.</param>
    /// <exception cref="LuiConfigurationException">The serializer has no format, or the attribute of the type gives no name.</exception>
    internal PayloadCodec(ILuiPayloadSerializer<TPayload> serializer)
    {
        var format = serializer.Format;
        if (string.IsNullOrWhiteSpace(format))
        {
            throw new LuiConfigurationException(
                $"The serializer registered for the payload type '{typeof(TPayload).FullName}' has no Format: ILuiPayloadSerializer<T>.Format "
                + "names the wire format, it is stored with the index and compared when the index is opened.");
        }

        Serializer = serializer;
        Format = format;
        TypeName = PayloadTypeName.Of(typeof(TPayload));
    }

    /// <summary>The serializer of the type.</summary>
    internal ILuiPayloadSerializer<TPayload> Serializer { get; }

    internal override string TypeName { get; }

    internal override string Format { get; }

    internal override byte[] Serialize(object payload, string indexName) => Serializer.Serialize((TPayload)payload);

    internal override T Deserialize<T>(ReadOnlySpan<byte> bytes, string indexName)
    {
        PayloadCodec self = this;
        return self is PayloadCodec<T> same
            ? same.Serializer.Deserialize(bytes)
            : throw new InvalidCastException(
                $"The index '{indexName}' stores payloads of the type '{typeof(TPayload).FullName}', and was asked for '{typeof(T).FullName}'.");
    }
}
