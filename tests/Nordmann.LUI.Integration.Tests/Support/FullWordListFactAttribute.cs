namespace Nordmann.LUI.Integration.Tests.Support;

/// <summary>
/// A test that needs the whole German word list (685,829 words). The list is
/// not checked in — <c>tools/fetch-wordlist.sh</c> puts it there — so the test
/// is <b>skipped</b> when it is missing, and it says so in the report.
/// <para>
/// A skipped test that looks green is a test that proves nothing. The release
/// gate sets <c>LUI_REQUIRE_FULL_WORDLIST=1</c>: then the test runs and fails
/// with a clear message when the list is missing, instead of skipping.
/// </para>
/// </summary>
internal sealed class FullWordListFactAttribute : FactAttribute
{
    public FullWordListFactAttribute()
    {
        if (File.Exists(TestFiles.FullWordListPath))
        {
            return;
        }

        if (string.Equals(
            Environment.GetEnvironmentVariable("LUI_REQUIRE_FULL_WORDLIST"),
            "1",
            StringComparison.Ordinal))
        {
            // The gate demands the list: the test itself fails with the reason.
            return;
        }

        Skip = $"The full word list is not present ({TestFiles.FullWordListPath}) — run tools/fetch-wordlist.sh.";
    }
}
