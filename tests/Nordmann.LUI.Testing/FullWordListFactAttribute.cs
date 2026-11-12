using System.Runtime.CompilerServices;

using Xunit;

namespace Nordmann.LUI.Testing;

/// <summary>
/// A test that needs the whole German word list (685,829 words). The list is
/// not checked in — <c>tools/fetch-wordlist.sh</c> puts it there — so the test
/// is <b>skipped</b> when it is missing, and it says so in the report.
/// <para>
/// A skipped test that looks green is a test that proves nothing. The guard that
/// the list is really there moved to the fetch: the script checks the 685,829
/// lines and fails loudly (ADR-0058, D19). The lane that runs the parity fetches
/// the list first, so the test runs — and a list that is missing is a failure of
/// the fetch, not a green test.
/// </para>
/// <para>
/// The gate is a dynamic skip (xUnit.net v3, ADR-0051): the runner asks
/// <see cref="WordListGate.FullWordListRuns"/> when the test is about to run.
/// </para>
/// </summary>
public sealed class FullWordListFactAttribute : FactAttribute
{
    public FullWordListFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        Skip = $"The full word list is not present ({TestFiles.FullWordListPath}) — run tools/fetch-wordlist.sh.";
        SkipType = typeof(WordListGate);
        SkipUnless = nameof(WordListGate.FullWordListRuns);
    }
}
