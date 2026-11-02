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

    private static string Describe(TestResult result) =>
        result.FailingTypeNames is { Count: > 0 } names
            ? string.Join(", ", names)
            : "none";
}
