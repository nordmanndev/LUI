namespace Nordmann.LUI.Indexes;

/// <summary>RED: a method of the index layer with a type parameter — the guard has to refuse it.</summary>
internal static class PayloadReading
{
    internal static TPayload? Read<TPayload>(byte[] bytes) => default;
}
