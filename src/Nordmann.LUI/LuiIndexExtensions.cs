namespace Nordmann.LUI;

/// <summary>
/// The typed view of an index (issue #59, ADR-0044): <c>index.As&lt;TagChip&gt;()</c> gives the index as the application
/// sees it, with the payload in the type of the application. The extension method keeps the type out of the index
/// (ADR-0035): the index knows no type, the view is the application's.
/// </summary>
public static class LuiIndexExtensions
{
    /// <summary>
    /// The index as the application sees it, with the payload in the type of the application. The view is a view: it
    /// opens nothing, writes nothing, and holds no index of its own.
    /// </summary>
    /// <typeparam name="TPayload">The type of the payload — the application's to know.</typeparam>
    /// <param name="index">The index.</param>
    /// <returns>The typed view of the index.</returns>
    public static LuiTypedIndex<TPayload> As<TPayload>(this ILuiIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);
        return new LuiTypedIndex<TPayload>(index);
    }
}
