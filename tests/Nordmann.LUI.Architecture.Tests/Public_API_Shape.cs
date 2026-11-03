using System.Reflection;
using NetArchTest.Rules;
using Shouldly;

using Nordmann.LUI.Testing;

namespace Nordmann.LUI.Architecture.Tests;

/// <summary>
/// The shape of the public API is a promise: a library that talks to the web
/// stack or hands out <c>Task</c>s the caller never had to await is a library
/// that misleads its consumers (LUI-Spezifikation §13).
/// </summary>
public sealed class Public_API_Shape
{
    private static Assembly LibraryAssembly => Assembly.Load("Nordmann.LUI");

    [Fact]
    public void The_library_does_not_depend_on_the_web_stack()
    {
        var result = Types
            .InAssembly(LibraryAssembly)
            .That()
            .ArePublic()
            .ShouldNot()
            .HaveDependencyOn("Microsoft.AspNetCore.App")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"LUI is a library, not a web app. Failing types: {Describe(result)}");
    }

    [Fact]
    public void The_public_api_is_synchronous()
    {
        // Microsoft.Data.Sqlite has no async I/O; a Task-returning API would be
        // a lie about latency (LUI-Spezifikation §13).
        var offenders = LibraryAssembly
            .GetExportedTypes()
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(method => typeof(Task).IsAssignableFrom(method.ReturnType))
            .Select(method => $"{method.DeclaringType?.Name}.{method.Name}")
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        offenders.ShouldBeEmpty("The public API of LUI is synchronous.");
    }

    [Fact]
    public void The_library_exposes_no_web_or_hosting_types()
    {
        var offenders = LibraryAssembly
            .GetExportedTypes()
            .Where(type => type.Namespace?.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) == true
                        || type.Namespace?.StartsWith("Microsoft.Extensions.Hosting", StringComparison.Ordinal) == true)
            .Select(type => type.FullName!)
            .ToArray();

        offenders.ShouldBeEmpty("LUI does not leak hosting types into its public surface.");
    }

    [Fact]
    public void A_consumer_needs_one_using_and_every_public_type_says_it_is_lui()
    {
        // One namespace for the consumer (`using Nordmann.LUI;`), and a prefix that does not collide with the
        // types of an application — `Priority` would, in almost every one of them (ADR-0013).
        var offenders = LibraryAssembly
            .GetExportedTypes()
            .Where(type => !string.Equals(type.Namespace, "Nordmann.LUI", StringComparison.Ordinal)
                        || !(type.Name.StartsWith("Lui", StringComparison.Ordinal) || type.Name.StartsWith("ILui", StringComparison.Ordinal)))
            .Select(type => type.FullName!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        offenders.ShouldBeEmpty("Every public type lives in Nordmann.LUI and starts with Lui or ILui.");
    }

    [Fact]
    public void The_results_and_options_have_named_properties_not_positions()
    {
        // A positional record breaks every caller when a field is added — and two fields of the same type
        // that swap places do not even fail to compile (ADR-0013).
        string[] named = ["LuiHit`2", "LuiResult`2", "LuiTimings", "LuiQueryOptions", "LuiCatalogOptions"];

        var offenders = LibraryAssembly
            .GetExportedTypes()
            .Where(type => named.Contains(type.Name, StringComparer.Ordinal))
            .Where(type => type.GetConstructors().Any(constructor =>
                constructor.GetParameters().Length > 0
                && !(constructor.GetParameters().Length == 1 && constructor.GetParameters()[0].ParameterType == type)))
            .Select(type => type.Name)
            .ToArray();

        offenders.ShouldBeEmpty("The results and options are built with named properties.");
        LibraryAssembly.GetExportedTypes().Count(type => named.Contains(type.Name, StringComparer.Ordinal)).ShouldBe(named.Length);
    }

    private static string Describe(TestResult result) =>
        result.FailingTypeNames is { Count: > 0 } names
            ? string.Join(", ", names)
            : "none";
}
