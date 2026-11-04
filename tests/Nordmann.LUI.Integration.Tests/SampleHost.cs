using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Nordmann.LUI.Sample;
using Nordmann.LUI.Testing;

namespace Nordmann.LUI.Integration.Tests;

/// <summary>
/// The sample application on a port of its own, over a database file of its own, with its real static client — a real
/// Kestrel on the loopback, because the abort of a request can only be seen on a real connection.
/// </summary>
public sealed class SampleHost : IAsyncLifetime, IDisposable
{
    private readonly TestDatabase _database = new();
    private WebApplication? _app;

    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _app = SampleApp.Build(
        [
            $"--{SampleApp.ConnectionStringKey}={_database.ConnectionString};Default Timeout=10",
            "--urls=http://127.0.0.1:0",
            $"--contentRoot={Repository.SampleDirectory}",
            "--Logging:LogLevel:Default=Warning",
        ]);
        await _app.StartAsync().ConfigureAwait(false);

        var address = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        Client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync().ConfigureAwait(false);
            await _app.DisposeAsync().ConfigureAwait(false);
        }
    }

    public void Dispose() => _database.Dispose();
}
