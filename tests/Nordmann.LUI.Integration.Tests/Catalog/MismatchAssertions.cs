using Shouldly;

namespace Nordmann.LUI.Integration.Tests.Catalog;

/// <summary>Assertions on the mismatch message.</summary>
internal static class MismatchAssertions
{
    /// <summary>The field is in the line "differs in:" of the message — not merely in the JSON of the two definitions above it.</summary>
    internal static void ShouldDifferIn(this LuiDefinitionMismatchException exception, string field) =>
        exception.Message.Split('\n').Single(line => line.TrimStart().StartsWith("differs in:", StringComparison.Ordinal))
            .ShouldContain(field, Case.Sensitive);
}
