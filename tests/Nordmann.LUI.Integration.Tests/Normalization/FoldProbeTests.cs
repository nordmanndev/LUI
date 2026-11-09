using Nordmann.LUI.Normalization;
using Shouldly;

namespace Nordmann.LUI.Integration.Tests.Normalization;

/// <summary>
/// <c>LuiCatalog.Open</c> asks the loaded SQLite how it folds text
/// (docs/normalization.md §7).
/// </summary>
public sealed class FoldProbeTests
{
    [Fact]
    public void The_sqlite_of_the_package_folds_exactly_like_the_table()
    {
        FoldProbe.Verify(TokenizerFold.Listed).ShouldBeEmpty();
    }

    [Fact]
    public void A_table_that_folds_a_letter_differently_is_found_out()
    {
        // É is folded to e by the tokenizer; a table that says E would hide every capital É
        var wrong = TokenizerFold.Listed.Select(e => e.From == 0x00C9 ? (e.From, 'E') : e).ToArray();

        var mismatches = FoldProbe.Verify(wrong);

        mismatches.Select(m => m.CodePoint).ShouldBe([0x00C9]);
        mismatches[0].Actual.ShouldBe("e");
    }

    [Fact]
    public void A_table_that_forgets_a_mapping_is_found_out()
    {
        var incomplete = TokenizerFold.Listed.Where(e => e.From != 0x00E9).ToArray();

        FoldProbe.Verify(incomplete).Select(m => m.CodePoint).ShouldBe([0x00E9]);
    }

    [Fact]
    public void A_table_that_keeps_a_mark_the_tokenizer_drops_is_found_out()
    {
        var wrong = TokenizerFold.Listed.Select(e => e.From == 0x0301 ? (e.From, 'x') : e).ToArray();

        FoldProbe.Verify(wrong).Select(m => m.CodePoint).ShouldBe([0x0301]);
    }

    [Fact]
    [Trait("Category", "Timing")]
    public void The_probe_costs_a_fraction_of_a_second()
    {
        // the probe runs at the first Open of every process. Measured: about 17 ms. The bound is the order of magnitude
        // of the claim — less than a second, sixty times the measurement — not its milliseconds (issue #78).
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        FoldProbe.Verify(TokenizerFold.Listed);
        System.Diagnostics.Stopwatch.GetElapsedTime(started).ShouldBeLessThan(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void The_version_of_the_table_is_stable_and_short()
    {
        TokenizerFold.Version.Length.ShouldBe(16);
        TokenizerFold.Version.ShouldBe(TokenizerFold.Version);
    }
}
