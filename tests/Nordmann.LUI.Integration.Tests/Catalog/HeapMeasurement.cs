namespace Nordmann.LUI.Integration.Tests.Catalog;

/// <summary>
/// The tests that read the heap of the process (<c>GC.GetTotalMemory</c>) run alone: the heap is the process's, and a test that
/// builds thousands of runtimes beside a test that asserts "a rebuild takes less than a megabyte" makes the second one fail
/// for the first one's sake.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HeapMeasurement
{
    /// <summary>The name the test classes name in <c>[Collection]</c>.</summary>
    public const string Name = "Heap measurement";
}
