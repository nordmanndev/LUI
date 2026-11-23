using System.Collections.ObjectModel;
using System.Globalization;

namespace Nordmann.LUI;

/// <summary>
/// One or more indexes failed in <see cref="LuiCatalog.Initialize"/> (ADR-0076). A failing index does not stop the others: its failing step is recorded, its later steps
/// are skipped, every other index is initialized — and at the end the call throws this exception, which carries the complete <see cref="Report"/> and the
/// <see cref="Errors"/> by index name. The message names every failed index and the step it failed at. What was finished stays; the next call continues by state, and
/// <see cref="LuiIndexInfo.NeedsInitialization"/> of the failed index still names what is missing.
/// </summary>
public sealed class LuiInitializeException : LuiException
{
    internal LuiInitializeException(LuiInitializeReport report, IReadOnlyDictionary<string, Exception> errors)
        : base(Describe(report, errors), errors.Values.First())
    {
        Report = report;
        Errors = new ReadOnlyDictionary<string, Exception>(new Dictionary<string, Exception>(errors, StringComparer.Ordinal));
    }

    /// <summary>What was done for every index of the run — the failed ones and the others.</summary>
    public LuiInitializeReport Report { get; }

    /// <summary>The error of every failed index, by its name — the exception of the step that failed, as it was thrown.</summary>
    public IReadOnlyDictionary<string, Exception> Errors { get; }

    private static string Describe(LuiInitializeReport report, IReadOnlyDictionary<string, Exception> errors)
    {
        var failed = string.Join(
            "; ",
            errors.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair =>
            {
                var step = report.Indexes.FirstOrDefault(line => string.Equals(line.Name, pair.Key, StringComparison.Ordinal))?.FailedStep;
                return string.Create(CultureInfo.InvariantCulture, $"'{pair.Key}' at {step}: {pair.Value.Message}");
            }));

        var tail = "What was finished stays: Report says what was done, Errors holds the exception of each index, and the next Initialize() continues by the state of the indexes.";
        return string.Create(CultureInfo.InvariantCulture, $"Initialize failed for {errors.Count} of {report.Indexes.Count} indexes, and the others were initialized: {failed}. {tail}");
    }
}
