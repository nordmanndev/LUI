namespace Nordmann.LUI.Sample.Api;

/// <summary>What an operation of the maintenance surface did, and how long it took.</summary>
internal sealed record OperationResult(string Operation, double ElapsedMs, object? Report);
