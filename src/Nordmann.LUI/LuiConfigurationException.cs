namespace Nordmann.LUI;

/// <summary>
/// The configuration of the library is wrong (ADR-0073): an index definition that is not valid — a name that is no name, an
/// option that makes no sense, a key type that is not allowed, a step of the description that the definition does not
/// know. It is thrown where the configuration is <b>used</b> — a definition is validated on its first use by a catalog,
/// never in the initializer of the static field that holds it. An invalid <i>argument</i> of a call stays an
/// <see cref="ArgumentException"/>.
/// </summary>
public sealed class LuiConfigurationException : LuiException
{
    /// <summary>Creates the exception.</summary>
    public LuiConfigurationException()
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">What is wrong with the configuration, and how to put it right.</param>
    public LuiConfigurationException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the error that caused it.</summary>
    /// <param name="message">What is wrong with the configuration, and how to put it right.</param>
    /// <param name="innerException">The error of the description — the argument that was refused.</param>
    public LuiConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
