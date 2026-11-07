/*
 * This file is part of Kahuna
 *
 * For the full copyright and license information, please view the LICENSE.txt
 * file that was distributed with this source code.
 */

using Kahuna.Aspire.Client;
using Kahuna.Client;
using Kahuna.Client.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

/// <summary>Registers a <see cref="KahunaClient"/> from an Aspire connection string.</summary>
public static class AspireKahunaClientExtensions
{
    private const string DefaultConfigSectionName = "Kahuna:Client";

    /// <summary>
    /// Registers a singleton <see cref="KahunaClient"/> for the connection named
    /// <paramref name="connectionName"/>, a health check, and the export of the client spans and metrics.
    /// </summary>
    /// <param name="builder">The host builder.</param>
    /// <param name="connectionName">The name of the connection string, normally the name of the Kahuna resource in the AppHost.</param>
    /// <param name="configureSettings">Changes the settings after they are read from configuration.</param>
    /// <param name="configureOptions">Changes the client options, for example the routing mode or the timeouts.</param>
    /// <exception cref="InvalidOperationException">The connection string is missing or not valid.</exception>
    public static void AddKahunaClient(
        this IHostApplicationBuilder builder,
        string connectionName,
        Action<KahunaClientSettings>? configureSettings = null,
        Action<KahunaOptions>? configureOptions = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionName);

        AddKahunaClient(builder, DefaultConfigSectionName, connectionName, serviceKey: null, configureSettings, configureOptions);
    }

    /// <summary>
    /// Registers a singleton <see cref="KahunaClient"/> keyed by <paramref name="name"/>, for an
    /// application that uses more than one Kahuna cluster. Resolve it with
    /// <c>[FromKeyedServices("name")]</c>.
    /// </summary>
    /// <param name="builder">The host builder.</param>
    /// <param name="name">The name of the connection string and the service key.</param>
    /// <param name="configureSettings">Changes the settings after they are read from configuration.</param>
    /// <param name="configureOptions">Changes the client options.</param>
    /// <exception cref="InvalidOperationException">The connection string is missing or not valid.</exception>
    public static void AddKeyedKahunaClient(
        this IHostApplicationBuilder builder,
        string name,
        Action<KahunaClientSettings>? configureSettings = null,
        Action<KahunaOptions>? configureOptions = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        AddKahunaClient(builder, $"{DefaultConfigSectionName}:{name}", name, serviceKey: name, configureSettings, configureOptions);
    }

    private static void AddKahunaClient(
        IHostApplicationBuilder builder,
        string configurationSectionName,
        string connectionName,
        string? serviceKey,
        Action<KahunaClientSettings>? configureSettings,
        Action<KahunaOptions>? configureOptions)
    {
        ArgumentNullException.ThrowIfNull(builder);

        KahunaClientSettings settings = new();
        builder.Configuration.GetSection(configurationSectionName).Bind(settings);

        if (builder.Configuration.GetConnectionString(connectionName) is { } connectionString)
            settings.ConnectionString = connectionString;

        configureSettings?.Invoke(settings);

        if (!KahunaConnectionString.TryParse(settings.ConnectionString, out string[] endpoints, out string? error))
            throw new InvalidOperationException(
                $"The Kahuna client for '{connectionName}' cannot start: {error} " +
                $"Set 'ConnectionStrings:{connectionName}' or '{configurationSectionName}:ConnectionString' in configuration.");

        if (serviceKey is null)
            builder.Services.AddKahunaClient(endpoints, configureOptions);
        else
            builder.Services.AddKeyedKahunaClient(serviceKey, endpoints, configureOptions);

        if (!settings.DisableHealthChecks)
        {
            builder.Services.AddHealthChecks().Add(new HealthCheckRegistration(
                serviceKey is null ? "Kahuna" : $"Kahuna_{connectionName}",
                services => new KahunaHealthCheck(
                    serviceKey is null
                        ? services.GetRequiredService<KahunaClient>()
                        : services.GetRequiredKeyedService<KahunaClient>(serviceKey),
                    endpoints.Length),
                failureStatus: null,
                tags: null,
                timeout: settings.HealthCheckTimeout));
        }

        if (!settings.DisableTracing)
            builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing.AddSource(KahunaClientTelemetry.ActivitySourceName));

        if (!settings.DisableMetrics)
            builder.Services.AddOpenTelemetry().WithMetrics(metrics => metrics.AddMeter(KahunaClientTelemetry.MeterName, KahunaClientTelemetry.RoutingMeterName));
    }
}
