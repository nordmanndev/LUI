using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Nordmann.LUI.Sample;
using Nordmann.LUI.Testing;

namespace Nordmann.LUI.Sample.Tests;

/// <summary>
/// The sample application on a port of its own, over a database file of its own, with its real static client — a real
/// Kestrel on the loopback, because the abort of a request can only be seen on a real connection.
/// <para>
/// xUnit.net v3 disposes a fixture once: through <see cref="IAsyncDisposable"/> when it has it, which
/// <see cref="IAsyncLifetime"/> now brings. The file of the database goes in the same disposal (ADR-0051).
/// </para>
/// </summary>
public sealed class SampleHost : IAsyncLifetime
{
    private readonly TestDatabase _database = new();
    private WebApplication? _app;

    public HttpClient Client { get; private set; } = null!;

    /// <summary>The services of the running sample — the test looks at what is registered (ADR-0029).</summary>
    public IServiceProvider Services => _app!.Services;

    public async ValueTask InitializeAsync()
    {
        _app = SampleApp.Build(
        [
            $"--{SampleApp.ConnectionStringKey}={_database.ConnectionString};Default Timeout=10",
            // The curated word list of the tests: the sample must not download 5.5 MB in a test.
            $"--{SampleApp.WordListKey}={WordList}",
            "--urls=http://127.0.0.1:0",
            $"--contentRoot={Repository.SampleDirectory}",
            "--Logging:LogLevel:Default=Warning",
        ]);
        await _app.StartAsync().ConfigureAwait(false);

        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        Client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(30) };
    }

    /// <summary>The word list the sample of the tests uses — every 97th word of the German word list.</summary>
    private static string WordList => TestFiles.SampleWordListPath;

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync().ConfigureAwait(false);
            await _app.DisposeAsync().ConfigureAwait(false);
        }

        _database.Dispose();
    }
}
