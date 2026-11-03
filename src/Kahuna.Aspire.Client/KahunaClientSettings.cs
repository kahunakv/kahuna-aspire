/*
 * This file is part of Kahuna
 *
 * For the full copyright and license information, please view the LICENSE.txt
 * file that was distributed with this source code.
 */

namespace Kahuna.Aspire.Client;

/// <summary>
/// Settings of the Kahuna client integration. They bind from the <c>Kahuna:Client</c> configuration
/// section, and from <c>Kahuna:Client:{name}</c> for a keyed client. A connection string in
/// <c>ConnectionStrings:{name}</c> replaces <see cref="ConnectionString"/>.
/// </summary>
public sealed class KahunaClientSettings
{
    /// <summary>
    /// The node endpoints. Accepted forms:
    /// <list type="bullet">
    /// <item>one URL: <c>http://localhost:8083</c></item>
    /// <item>a comma-separated list of URLs: <c>http://node1:8083,http://node2:8083</c></item>
    /// <item>a key-value string: <c>Endpoints=http://node1:8083,http://node2:8083</c></item>
    /// </list>
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>When true, no health check is registered. The default is false.</summary>
    public bool DisableHealthChecks { get; set; }

    /// <summary>How long the health check waits for the node. Unlimited when not set.</summary>
    public TimeSpan? HealthCheckTimeout { get; set; }

    /// <summary>When true, the client spans are not exported. The default is false.</summary>
    public bool DisableTracing { get; set; }

    /// <summary>When true, the client metrics are not exported. The default is false.</summary>
    public bool DisableMetrics { get; set; }
}
