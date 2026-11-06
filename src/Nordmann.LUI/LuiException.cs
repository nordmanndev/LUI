namespace Nordmann.LUI;

/// <summary>
/// The base of the exceptions that LUI throws about an index or its environment: <see cref="LuiEnvironmentException"/>,
/// <see cref="LuiDefinitionMismatchException"/>, <see cref="LuiLinkedTableException"/>, <see cref="LuiIndexNotFoundException"/>
/// and <see cref="LuiReadOnlyException"/> (review of 2026-11-04, A.8).
/// <c>catch (LuiException)</c> separates the library's own conditions from the programming errors of the application;
/// it is still an <see cref="InvalidOperationException"/>, so a <c>catch (InvalidOperationException)</c> that exists
/// catches them as before. Abstract: the library throws only these.
/// </summary>
public abstract class LuiException : InvalidOperationException
{
    /// <summary>Creates the exception.</summary>
    protected LuiException()
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What is wrong, and what to do about it.</param>
    protected LuiException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What is wrong, and what to do about it.</param>
    /// <param name="innerException">The error that caused it.</param>
    protected LuiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
