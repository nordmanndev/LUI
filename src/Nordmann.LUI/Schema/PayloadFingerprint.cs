namespace Nordmann.LUI.Schema;

/// <summary>What an embedded index stores about its payloads (ADR-0072): the name of the payload type and the format of the serializer.</summary>
/// <param name="TypeName">The stable name of the payload type.</param>
/// <param name="Format">The format of the serializer that writes the payloads.</param>
internal sealed record PayloadFingerprint(string TypeName, string Format);
