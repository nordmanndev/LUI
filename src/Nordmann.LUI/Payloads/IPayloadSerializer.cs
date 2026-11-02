namespace Nordmann.LUI.Payloads;

/// <summary>
/// Turns the payload of an entry into bytes and back
/// (LUI-Spezifikation §12.3). The payload is read for the hits only.
/// </summary>
/// <typeparam name="TPayload">The payload type.</typeparam>
public interface IPayloadSerializer<TPayload>
{
    /// <summary>Serialises a payload.</summary>
    /// <param name="payload">The payload; never <see langword="null"/>.</param>
    /// <returns>The bytes that are stored.</returns>
    byte[] Serialize(TPayload payload);

    /// <summary>Reads a payload back.</summary>
    /// <param name="bytes">The stored bytes.</param>
    /// <returns>The payload.</returns>
    TPayload Deserialize(ReadOnlySpan<byte> bytes);
}
