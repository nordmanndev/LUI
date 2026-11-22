#pragma warning disable MA0048 // the generic form of a name that has a non-generic file: <Name>OfPayload.cs (ADR-0072)
namespace Nordmann.LUI;

/// <summary>
/// Turns the payload of <b>one type</b> into bytes and back (ADR-0072): the serializer of the typed path, bound to its payload
/// type through the <see cref="LuiPayloadRegistry"/> of the catalog (<see cref="LuiCatalogOptions.Payloads"/>) — there is no
/// catch-all that writes every type. <see cref="LuiPayloadSerializer.Json{TPayload}"/> is the JSON one LUI brings; any other format
/// (MessagePack, protocol buffers, a reflection-based <c>JsonSerializerOptions</c>, a format of the application) is an implementation
/// of this interface, registered with <see cref="LuiPayloadRegistry.Use{TPayload}"/>.
/// <para>
/// An embedded index stores the <see cref="Format"/> of the serializer that wrote its payloads, next to the name of the payload
/// type, and compares both when it is opened: neither another type nor the same type in another format is read silently.
/// <b>A serializer that changes its wire format must change its <see cref="Format"/></b> — LUI compares the name, not the bytes.
/// An implementation is used from many threads at once and holds no state that a call changes.
/// </para>
/// </summary>
/// <typeparam name="TPayload">The payload type this serializer writes and reads.</typeparam>
public interface ILuiPayloadSerializer<TPayload>
{
    /// <summary>
    /// The name of the wire format — <c>json-strict-v1</c>, <c>msgpack-v1</c> —: stored with an embedded index and compared when it
    /// is opened. Constant for the lifetime of the serializer; it is read once, when the serializer is registered.
    /// </summary>
    string Format { get; }

    /// <summary>Serializes a payload.</summary>
    /// <param name="payload">The payload; never <see langword="null"/>.</param>
    /// <returns>The bytes that are stored.</returns>
    byte[] Serialize(TPayload payload);

    /// <summary>Reads a payload back.</summary>
    /// <param name="bytes">The stored bytes.</param>
    /// <returns>The payload.</returns>
    TPayload Deserialize(ReadOnlySpan<byte> bytes);
}
#pragma warning restore MA0048
