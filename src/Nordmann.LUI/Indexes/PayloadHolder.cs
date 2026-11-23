namespace Nordmann.LUI.Indexes;

/// <summary>RED: an index layer that holds a payload type of the application and its serializer — the guard has to refuse it.</summary>
/// <typeparam name="TPayload">The payload type of the application.</typeparam>
internal sealed class PayloadHolder<TPayload>
{
    internal ILuiPayloadSerializer<TPayload>? Serializer { get; set; }

    internal TPayload? Value { get; set; }
}
