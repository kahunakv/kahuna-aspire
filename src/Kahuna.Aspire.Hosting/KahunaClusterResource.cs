/*
 * This file is part of Kahuna
 *
 * For the full copyright and license information, please view the LICENSE.txt
 * file that was distributed with this source code.
 */

using System.Collections.Concurrent;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// A Kahuna cluster: a group of <see cref="KahunaClusterNodeResource"/> containers that replicate each
/// write through Raft and act as one store. The resource has no process of its own. Its state and its
/// health come from its nodes.
/// <para>
/// The connection string is a comma-separated list of the cleartext HTTP/2 (h2c) gRPC endpoints of the
/// nodes, in the form that <c>KahunaClient</c> accepts.
/// </para>
/// </summary>
/// <param name="name">The name of the resource. It is also the connection string name.</param>
public sealed class KahunaClusterResource(string name) : Resource(name), IResourceWithConnectionString
{
    /// <summary>The HTTPS port of each node. The nodes send Raft and forwarded traffic to it.</summary>
    internal const int RaftTargetPort = 8082;

    /// <summary>The certificate that the image holds. It is public, so it is for development only.</summary>
    internal const string CertificatePath = "/app/certificate.pfx";

    private readonly List<KahunaClusterNodeResource> nodes = [];

    private volatile bool startCompleted;

    /// <summary>The nodes of the cluster, in node ID order.</summary>
    public IReadOnlyList<KahunaClusterNodeResource> Nodes => nodes;

    /// <summary>The comma-separated URLs of the gRPC endpoints of all nodes.</summary>
    public ReferenceExpression ConnectionStringExpression => BuildEndpointList(node => node.GrpcEndpoint);

    // The last state and health that Aspire published for each node. The state tracker writes them and
    // the cluster health check reads them.
    internal ConcurrentDictionary<KahunaClusterNodeResource, (string? State, HealthStatus? Health)> NodeStatus { get; } = new();

    // True after all nodes were healthy at the same time. The cluster health check sets it, and the state
    // tracker clears it when all nodes stop.
    internal bool StartCompleted
    {
        get => startCompleted;
        set => startCompleted = value;
    }

    internal void AddNode(KahunaClusterNodeResource node) => nodes.Add(node);

    /// <summary>
    /// The parts of the connection, for a consumer that is not .NET. Each becomes an environment
    /// variable named <c>{RESOURCE}_{PROPERTY}</c> on a resource that references this one.
    /// </summary>
    IEnumerable<KeyValuePair<string, ReferenceExpression>> IResourceWithConnectionString.GetConnectionProperties()
    {
        yield return new("Uri", ConnectionStringExpression);
        yield return new("HttpUri", BuildEndpointList(node => node.HttpEndpoint));
    }

    private ReferenceExpression BuildEndpointList(Func<KahunaClusterNodeResource, EndpointReference> selectEndpoint)
    {
        ReferenceExpressionBuilder builder = new();

        for (int i = 0; i < nodes.Count; i++)
        {
            if (i > 0)
                builder.AppendLiteral(",");

            builder.Append($"{selectEndpoint(nodes[i]).Property(EndpointProperty.Url)}");
        }

        return builder.Build();
    }
}
