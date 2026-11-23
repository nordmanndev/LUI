namespace Nordmann.LUI;

/// <summary>
/// The settings of the outbox loop of the host (ADR-0076): how often the outbox of the linked indexes is applied, and how far the wait grows when
/// rounds fail. <c>AddLuiOutbox(options =&gt; …)</c> sets them once, at the registration.
/// </summary>
public sealed class LuiOutboxOptions
{
    /// <summary>How long the service waits between two rounds that worked; positive. Default: 30 seconds.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The longest wait between two rounds while rounds fail: after each failed round the wait doubles — <see cref="Interval"/> × 2, × 4, … — up to this, and
    /// the first round that works goes back to <see cref="Interval"/>; positive. A value below <see cref="Interval"/> is the interval: the wait never
    /// shrinks below it. Default: 5 minutes.
    /// </summary>
    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromMinutes(5);
}
