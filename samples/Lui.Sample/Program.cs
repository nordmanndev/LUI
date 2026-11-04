using Nordmann.LUI.Sample;

// The sample wires the pieces together; the library does the work. See SampleApp for the composition.
await SampleApp.Build(args).RunAsync().ConfigureAwait(false);

/// <summary>Exposed so the integration tests can host the sample.</summary>
public partial class Program;
