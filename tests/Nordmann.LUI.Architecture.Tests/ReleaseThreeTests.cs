using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Nordmann.LUI.Architecture.Tests;

/// <summary>
/// The freeze of 0.1.0 is lifted for 0.3.0 — in named places, and only there — and holds again from it (ADR-0069). The guard
/// holds the public API of 0.3.0 against the table of the record: what moved, what is gone, what is new — and that the
/// migration note names every one of them, the two breaks that the compiler does not see first.
/// </summary>
public sealed class ReleaseThreeTests
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static Assembly Library => Assembly.Load("Nordmann.LUI");

    private static string Changelog => File.ReadAllText(Path.Combine(Nordmann.LUI.Testing.Repository.RootPath, "CHANGELOG.md"));

    [Fact]
    public void The_catalogue_opens_by_path_in_three_ways_and_keeps_no_connection_string()
    {
        var catalog = typeof(LuiCatalog);

        catalog.GetProperty("ConnectionString", Declared).ShouldBeNull("LuiCatalog.Path replaces ConnectionString");
        catalog.GetProperty("Path", Declared).ShouldNotBeNull();

        foreach (var name in new[] { "Open", "OpenOrCreate", "OpenReadOnly" })
        {
            var method = catalog.GetMethods(Declared).Where(m => string.Equals(m.Name, name, StringComparison.Ordinal)).ShouldHaveSingleItem(name);
            method.GetParameters().Select(p => (p.ParameterType, p.Name)).ShouldBe([(typeof(string), "path"), (typeof(LuiCatalogOptions), "options")], name);
        }
    }

    [Fact]
    public void The_options_carry_their_settings_in_the_pragmas_and_nothing_of_the_old_ones()
    {
        var names = typeof(LuiCatalogOptions).GetProperties(Declared).Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();

        names.ShouldBe(["PayloadSerializer", "Pragmas", "TimeProvider"]);  // WriteAheadLog, MemoryMapMegabytes, BusyTimeout and CacheSizeMegabytes are gone

        typeof(LuiPragmas).GetProperties(Declared).Select(p => p.Name).Order(StringComparer.Ordinal)
            .ShouldBe(["BusyTimeout", "CacheSizeMegabytes", "Synchronous"]);  // the three settings that make sense, and no more
        Enum.GetNames<LuiSynchronous>().Order(StringComparer.Ordinal).ShouldBe(["Full", "Normal"]);
    }

    [Fact]
    public void The_host_takes_a_factory_and_no_connection_string()
    {
        var hosting = Assembly.Load("Nordmann.LUI.Hosting").GetExportedTypes().Single(type => string.Equals(type.Name, "LuiServiceCollectionExtensions", StringComparison.Ordinal));
        var overloads = hosting.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => string.Equals(m.Name, "AddLui", StringComparison.Ordinal)).ToArray();

        overloads.ShouldHaveSingleItem("one AddLui");
        overloads[0].GetParameters().Select(p => p.ParameterType).ShouldBe([typeof(IServiceCollection), typeof(Func<IServiceProvider, LuiCatalog>)]);
    }

    [Fact]
    public void The_package_is_a_version_of_the_round_of_0_3()
    {
        var version = Library.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

        // the version of the round is 0.3, not 0.2: the change is not only additive (ADR-0069); a patch release keeps the guard green
        version.ShouldStartWith("0.3.", Case.Sensitive);
    }

    [Fact]
    public void The_changelog_of_0_3_0_carries_the_migration_note_with_the_two_breaks_the_compiler_does_not_see_first()
    {
        var text = Changelog;
        text.ShouldContain("## [0.3.0]");
        var section = text[text.IndexOf("## [0.3.0]", StringComparison.Ordinal)..];

        section.ShouldContain("### Migration");
        var migration = section.IndexOf("### Migration", StringComparison.Ordinal);
        section.IndexOf("### Changed", migration, StringComparison.Ordinal).ShouldBeGreaterThan(migration, "the note ends where the changes begin");
        var note = section[migration..section.IndexOf("### Changed", migration, StringComparison.Ordinal)];

        var firstBreak = note.IndexOf("`Open` no longer", StringComparison.Ordinal);
        var secondBreak = note.IndexOf("Read-only is the method `OpenReadOnly`", StringComparison.Ordinal);
        var table = note.IndexOf("| before (0.2.x) | after (0.3.0) |", StringComparison.Ordinal);
        firstBreak.ShouldBeGreaterThanOrEqualTo(0, "Open no longer creates anything");
        secondBreak.ShouldBeGreaterThan(firstBreak, "read-only is a method: the second break follows the first");
        table.ShouldBeGreaterThan(secondBreak, "the table of every moved member follows the two breaks the compiler does not see");

        foreach (var moved in new[] { "ConnectionString", "WriteAheadLog", "MemoryMapMegabytes", "BusyTimeout", "CacheSizeMegabytes", "AddLui", "LuiPragmas", "LuiSynchronous", "LuiCatalogNotFoundException", "LuiCatalog.Path", ":memory:" })
        {
            note.IndexOf(moved, table, StringComparison.Ordinal).ShouldBeGreaterThanOrEqualTo(0, $"the migration note names {moved} in or after its table");
        }
    }

    [Fact]
    public void The_record_of_the_freeze_is_superseded_by_the_record_that_lifts_it()
    {
        var path = Path.Combine(Nordmann.LUI.Testing.Repository.RootPath, ".agents", "adr", "0026-the-public-api-from-0-1-0-on-grows-only-additively.md");
        var text = File.ReadAllText(path);

        text.ShouldContain("**Status:** Superseded by [0069]");  // with the decision of the owner and its date
    }
}
