namespace Nordmann.LUI;

/// <summary>
/// Turns a payload into bytes and back (LUI-Spezifikation §12.3, ADR-0035). There is one per catalog
/// (<see cref="LuiCatalogOptions.PayloadSerializer"/>), and it knows no type in advance: a payload is written with the
/// type it <i>has</i>, and read with the type the caller asks for (<see cref="LuiHit.GetPayload{T}"/>). The index stores
/// only the bytes. <see cref="LuiPayloadSerializer.Json"/> is the one LUI brings.
/// </summary>
public interface ILuiPayloadSerializer
{
    /// <summary>Serialises a payload, with the type it has at run time.</summary>
    /// <param name="payload">The payload; never <see langword="null"/>.</param>
    /// <returns>The bytes that are stored.</returns>
    byte[] Serialize(object payload);

    /// <summary>Reads a payload back as the type the caller asks for.</summary>
    /// <typeparam name="T">The type the caller asks for.</typeparam>
    /// <param name="bytes">The stored bytes.</param>
    /// <returns>The payload.</returns>
    T Deserialize<T>(ReadOnlySpan<byte> bytes);
}
