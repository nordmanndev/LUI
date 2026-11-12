namespace Nordmann.LUI.Testing;

/// <summary>
/// The gate of the tests that need the whole German word list (685,829 words). The list is not checked in —
/// <c>tools/fetch-wordlist.sh</c> puts it there, and the fetch is the guard: it checks the number of lines and fails
/// loudly (ADR-0058, D19).
/// <para>
/// The gate is a dynamic skip (xUnit.net v3, ADR-0051): the runner asks <see cref="FullWordListRuns"/> when the test is
/// about to run, and the report says why it did not.
/// </para>
/// </summary>
public static class WordListGate
{
    /// <summary>The parity over the full word list runs when the list is there — otherwise the test is skipped with the reason.</summary>
    public static bool FullWordListRuns => File.Exists(TestFiles.FullWordListPath);
}
