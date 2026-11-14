using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace Nordmann.LUI.Unit.Tests.Support;

/// <summary>
/// The probe of a measured window (issue #134): it measures the allocation of a window
/// iteration by iteration and writes the deltas down, so that a dirty window names the
/// iteration and the amount.
/// <para>
/// The order is the whole point of it. The buffers are allocated <b>before</b> the window, and
/// the report is written <b>after</b> it: a probe that prints inside the window measures
/// itself — the interpolated string and the console buffer allocate, and every number is then
/// the probe's own.
/// </para>
/// </summary>
internal sealed class AllocationProbe
{
    /// <summary>The iterations of one window — the shape of the window of the rule (issue #132).</summary>
    public const int IterationsPerWindow = 100;

    private readonly int _windows;
    private readonly int _iterations;

    /// <summary>The buffer of the deltas — allocated before the window.</summary>
    private readonly long[] _deltas;

    /// <summary>The buffer of the totals — allocated before the window.</summary>
    private readonly long[] _totals;

    public AllocationProbe(int windows, int iterations)
    {
        _windows = windows;
        _iterations = iterations;
        _deltas = new long[windows * iterations];
        _totals = new long[windows];
    }

    /// <summary>The bytes the windows allocated — the number the rule holds down.</summary>
    public long Allocated { get; private set; }

    /// <summary>
    /// The sum of the measured values. It keeps the calls alive: a call whose result is
    /// discarded may be removed by an optimising JIT, and then the window measures nothing
    /// at all (issue #132).
    /// </summary>
    public double Sum { get; private set; }

    /// <summary>
    /// Measures the windows. Nothing may allocate inside the window: the buffers are already
    /// there, and the report is written afterwards.
    /// </summary>
    public long Measure(Func<double> measurement)
    {
        var start = GC.GetAllocatedBytesForCurrentThread();
        var windowStart = start;

        for (var w = 0; w < _windows; w++)
        {
            var seen = windowStart;

            for (var i = 0; i < _iterations; i++)
            {
                Sum += measurement();

                var now = GC.GetAllocatedBytesForCurrentThread();
                _deltas[(w * _iterations) + i] = now - seen;
                seen = now;
            }

            _totals[w] = seen - windowStart;
            windowStart = seen;
        }

        Allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        return Allocated;
    }

    /// <summary>
    /// The report of the probe: the machine it ran on, the windows it measured, and the
    /// iterations that allocated. It is written after the window — the writing allocates, and
    /// it must not be part of the measurement.
    /// </summary>
    public string Report()
    {
        var text = new StringBuilder();
        text.AppendLine("# The probe of the measured window (issue #134)");
        text.AppendLine(CultureInfo.InvariantCulture, $"runtime: {RuntimeInformation.FrameworkDescription}");
        text.AppendLine(CultureInfo.InvariantCulture, $"machine: {RuntimeInformation.OSDescription}, {Environment.ProcessorCount} cores");
        text.AppendLine(CultureInfo.InvariantCulture, $"windows: {_windows}, iterations per window: {_iterations}");
        text.AppendLine(CultureInfo.InvariantCulture, $"allocated in the windows: {Allocated} bytes");

        var dirty = 0;
        for (var w = 0; w < _windows; w++)
        {
            if (_totals[w] == 0)
            {
                continue;
            }

            dirty++;
            text.AppendLine(CultureInfo.InvariantCulture, $"window {w}: {_totals[w]} bytes");

            for (var i = 0; i < _iterations; i++)
            {
                var delta = _deltas[(w * _iterations) + i];
                if (delta != 0)
                {
                    text.AppendLine(CultureInfo.InvariantCulture, $"  iteration {i}: {delta} bytes");
                }
            }
        }

        text.AppendLine(CultureInfo.InvariantCulture, $"dirty windows: {dirty}");
        return text.ToString();
    }
}
