namespace Nordmann.LUI;

/// <summary>
/// The SQLite LUI is talking to cannot carry the library: too old, no FTS5
/// trigram tokenizer, or a tokenizer that folds text differently from the
/// table the library was measured with (docs/normalization.md).
/// </summary>
public sealed class LuiEnvironmentException : LuiException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What is wrong, and what to do about it.</param>
    public LuiEnvironmentException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What is wrong, and what to do about it.</param>
    /// <param name="innerException">The error SQLite reported.</param>
    public LuiEnvironmentException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
