namespace Nordmann.LUI;

/// <summary>
/// The payload of a hit cannot be read as the type that was asked for (ADR-0035): the stored bytes are not that type, the
/// catalog has no serialiser, or a <c>Linked</c> index built another type. The type of a payload is the application's to
/// keep; the message names the index, the key and the type.
/// </summary>
public sealed class LuiPayloadException : LuiException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="indexName">The index of the hit.</param>
    /// <param name="key">The key of the hit.</param>
    /// <param name="requestedType">The type that was asked for.</param>
    /// <param name="reason">Why it cannot be read.</param>
    /// <param name="innerException">What the serialiser threw, if anything.</param>
    public LuiPayloadException(string indexName, LuiKey key, Type requestedType, string reason, Exception? innerException = null)
        : base(
            $"The payload of the key '{key}' in the index '{indexName}' cannot be read as '{requestedType?.FullName}': {reason}",
            innerException!)
    {
        ArgumentNullException.ThrowIfNull(requestedType);
        IndexName = indexName;
        Key = key;
        RequestedType = requestedType;
    }

    /// <summary>The index of the hit.</summary>
    public string IndexName { get; }

    /// <summary>The key of the hit.</summary>
    public LuiKey Key { get; }

    /// <summary>The type that was asked for.</summary>
    public Type RequestedType { get; }
}
