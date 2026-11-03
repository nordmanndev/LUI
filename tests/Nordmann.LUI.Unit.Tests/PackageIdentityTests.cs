using System.Reflection;
using Shouldly;

namespace Nordmann.LUI.Unit.Tests;

/// <summary>
/// The package identity is part of the contract: consumers pin the id and the
/// version, and the assembly they load has to answer to both.
/// </summary>
public class PackageIdentityTests
{
    private static Assembly LibraryAssembly => Assembly.Load("Nordmann.LUI");

    [Fact]
    public void Assembly_is_named_Nordmann_LUI()
    {
        LibraryAssembly.GetName().Name.ShouldBe("Nordmann.LUI");
    }

    [Fact]
    public void Informational_version_carries_the_version_prefix()
    {
        var informational = LibraryAssembly
            .GetCustomAttributes<AssemblyInformationalVersionAttribute>()
            .FirstOrDefault();

        informational.ShouldNotBeNull();
        informational!.InformationalVersion.ShouldStartWith("0.1.0");
    }

    [Fact]
    public void Library_references_no_undocumented_dependency()
    {
        // What the code pulls in is part of the contract (ADR-0004): a new
        // dependency has to be decided, not inherited. The *package's*
        // dependency list is asserted against the project file in the
        // architecture tests — this one guards the compiled surface.
        var allowed = new[]
        {
            "Microsoft.Data.Sqlite",
            "SQLitePCLRaw.core",
            "SQLitePCLRaw.provider.e_sqlite3",
            "SQLitePCLRaw.lib.e_sqlite3",
            "SymSpell",   // the assembly of the package `symspell` (ADR-0004)
            "F23.StringSimilarity"
        };

        var unexpected = LibraryAssembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => !name.StartsWith("System", StringComparison.Ordinal))
            .Where(name => !name.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal))
            .Where(name => !allowed.Contains(name, StringComparer.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        unexpected.ShouldBeEmpty(
            "The package may only pull the documented dependencies (ADR-0004).");
    }
}
