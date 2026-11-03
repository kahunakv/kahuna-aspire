/*
 * This file is part of Kahuna
 *
 * For the full copyright and license information, please view the LICENSE.txt
 * file that was distributed with this source code.
 */

using Kahuna.Aspire.Client;
using Kahuna.Client;
using Kahuna.Client.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Kahuna.Aspire.Client.Tests;

public sealed class AspireKahunaClientTests
{
    private static HostApplicationBuilder CreateBuilder(params (string Key, string Value)[] configuration)
    {
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new());

        foreach ((string key, string value) in configuration)
            builder.Configuration[key] = value;

        return builder;
    }

    [Fact]
    public void ConnectionStringRegistersOneSingletonClient()
    {
        HostApplicationBuilder builder = CreateBuilder(("ConnectionStrings:kahuna", "http://localhost:8083"));

        builder.AddKahunaClient("kahuna");

        using IHost host = builder.Build();

        KahunaClient client = host.Services.GetRequiredService<KahunaClient>();

        Assert.Same(client, host.Services.GetRequiredService<KahunaClient>());
        Assert.Equal(KahunaRoutingMode.RoundRobin, client.EffectiveRouting);
    }

    [Fact]
    public void SeveralEndpointsGiveARoutingClient()
    {
        HostApplicationBuilder builder = CreateBuilder(("ConnectionStrings:kahuna", "http://node1:8083,http://node2:8083"));

        builder.AddKahunaClient("kahuna");

        using IHost host = builder.Build();

        Assert.Equal(KahunaRoutingMode.Learned, host.Services.GetRequiredService<KahunaClient>().EffectiveRouting);
    }

    [Fact]
    public void ConfigureOptionsReachesTheClient()
    {
        HostApplicationBuilder builder = CreateBuilder(("ConnectionStrings:kahuna", "http://node1:8083,http://node2:8083"));

        builder.AddKahunaClient("kahuna", configureOptions: options => options.Routing = KahunaRoutingMode.RoundRobin);

        using IHost host = builder.Build();

        Assert.Equal(KahunaRoutingMode.RoundRobin, host.Services.GetRequiredService<KahunaClient>().EffectiveRouting);
    }

    [Fact]
    public void SettingsSectionIsUsedWhenThereIsNoConnectionString()
    {
        HostApplicationBuilder builder = CreateBuilder(("Kahuna:Client:ConnectionString", "http://localhost:8083"));

        builder.AddKahunaClient("kahuna");

        using IHost host = builder.Build();

        Assert.NotNull(host.Services.GetService<KahunaClient>());
    }

    [Fact]
    public void MissingConnectionStringFailsAtRegistration()
    {
        HostApplicationBuilder builder = CreateBuilder();

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => builder.AddKahunaClient("kahuna"));

        Assert.Contains("ConnectionStrings:kahuna", error.Message);
    }

    [Fact]
    public void KeyedClientsKeepClustersApart()
    {
        HostApplicationBuilder builder = CreateBuilder(
            ("ConnectionStrings:orders", "http://orders:8083"),
            ("ConnectionStrings:sessions", "http://sessions-1:8083,http://sessions-2:8083"));

        builder.AddKeyedKahunaClient("orders");
        builder.AddKeyedKahunaClient("sessions");

        using IHost host = builder.Build();

        KahunaClient orders = host.Services.GetRequiredKeyedService<KahunaClient>("orders");
        KahunaClient sessions = host.Services.GetRequiredKeyedService<KahunaClient>("sessions");

        Assert.NotSame(orders, sessions);
        Assert.Null(host.Services.GetService<KahunaClient>());

        HealthCheckServiceOptions health = host.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;
        Assert.Equal(["Kahuna_orders", "Kahuna_sessions"], health.Registrations.Select(r => r.Name).Order());
    }

    [Fact]
    public void HealthCheckCanBeTurnedOff()
    {
        HostApplicationBuilder builder = CreateBuilder(
            ("ConnectionStrings:kahuna", "http://localhost:8083"),
            ("Kahuna:Client:DisableHealthChecks", "true"));

        builder.AddKahunaClient("kahuna");

        using IHost host = builder.Build();

        HealthCheckServiceOptions health = host.Services.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;
        Assert.Empty(health.Registrations);
    }

    [Fact]
    public async Task UnreachableNodeIsUnhealthy()
    {
        // Port 1 on the loopback address refuses the connection at once.
        HostApplicationBuilder builder = CreateBuilder(("ConnectionStrings:kahuna", "http://127.0.0.1:1"));

        builder.AddKahunaClient("kahuna", settings => settings.HealthCheckTimeout = TimeSpan.FromSeconds(10));

        using IHost host = builder.Build();

        HealthReport report = await host.Services.GetRequiredService<HealthCheckService>().CheckHealthAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HealthStatus.Unhealthy, report.Status);
        Assert.Equal(HealthStatus.Unhealthy, report.Entries["Kahuna"].Status);
    }

    [Theory]
    [InlineData("http://localhost:8083", new[] { "http://localhost:8083" })]
    [InlineData(" http://a:8083 , http://b:8083 ", new[] { "http://a:8083", "http://b:8083" })]
    [InlineData("Endpoints=http://a:8083,http://b:8083", new[] { "http://a:8083", "http://b:8083" })]
    [InlineData("Endpoint=https://a:8082", new[] { "https://a:8082" })]
    public void ConnectionStringFormsAreAccepted(string connectionString, string[] expected)
    {
        Assert.True(KahunaConnectionString.TryParse(connectionString, out string[] endpoints, out string? error), error);
        Assert.Equal(expected, endpoints);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Host=localhost")]
    [InlineData("Endpoints=")]
    [InlineData("http://a:8083,ftp://b:21")]
    [InlineData("Endpoints=localhost:8083")]
    public void MalformedConnectionStringsAreRejected(string connectionString)
    {
        Assert.False(KahunaConnectionString.TryParse(connectionString, out _, out string? error));
        Assert.NotNull(error);
    }
}
