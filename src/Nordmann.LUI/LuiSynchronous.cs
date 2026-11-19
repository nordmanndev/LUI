namespace Nordmann.LUI;

/// <summary>
/// How the connections of a catalogue write (ADR-0066). There are two: <see cref="Normal"/> and <see cref="Full"/>.
/// <c>Off</c> would put the file at risk on a power loss, and <c>Extra</c> would cost without a gain in the
/// write-ahead log — a setting that makes no sense does not exist.
/// </summary>
public enum LuiSynchronous
{
    /// <summary>
    /// The usual one: the write-ahead log carries the durability of the file, and a write is not flushed to the disk
    /// with every statement.
    /// </summary>
    Normal,

    /// <summary>
    /// Every write is flushed to the disk: the safest, and the slowest.
    /// </summary>
    Full,
}
