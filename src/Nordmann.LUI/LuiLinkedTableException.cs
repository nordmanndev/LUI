namespace Nordmann.LUI;

/// <summary>
/// The table of the application that a <c>Linked</c> index is linked to is not what the link says: the table or a
/// column is missing, or the key is not its <c>INTEGER PRIMARY KEY</c> (LUI-Spezifikation §12.8, ADR-0015). LUI does
/// not change the application's table to make it fit — the message says what is missing.
/// </summary>
public sealed class LuiLinkedTableException : LuiException
{
    /// <summary>Creates the exception.</summary>
    public LuiLinkedTableException()
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What is missing.</param>
    public LuiLinkedTableException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What is missing.</param>
    /// <param name="innerException">The cause.</param>
    public LuiLinkedTableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
