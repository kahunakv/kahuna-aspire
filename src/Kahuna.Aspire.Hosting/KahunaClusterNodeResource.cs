/*
 * This file is part of Kahuna
 *
 * For the full copyright and license information, please view the LICENSE.txt
 * file that was distributed with this source code.
 */

namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// One node of a Kahuna cluster. Each node is a container. The other nodes reach it on the container
/// network by its resource name.
/// </summary>
/// <remarks>
/// A node has a parent relationship with its <see cref="KahunaClusterResource"/> for the dashboard, but it
/// is not an <see cref="IResourceWithParent"/>. The health of the cluster comes from the health of the
/// nodes, so a node must not inherit the health check of the cluster.
/// </remarks>
public sealed class KahunaClusterNodeResource : ContainerResource
{
    private EndpointReference? grpcEndpoint;

    private EndpointReference? httpEndpoint;

    /// <summary>Initializes a new node.</summary>
    /// <param name="name">The name of the resource. It is also the host name of the node on the container network.</param>
    /// <param name="cluster">The cluster that the node is part of.</param>
    /// <param name="nodeId">The numeric Raft node ID, from 1.</param>
    public KahunaClusterNodeResource(string name, KahunaClusterResource cluster, int nodeId) : base(name)
    {
        ArgumentNullException.ThrowIfNull(cluster);
        ArgumentOutOfRangeException.ThrowIfLessThan(nodeId, 1);

        Cluster = cluster;
        NodeId = nodeId;
    }

    /// <summary>The cluster that the node is part of.</summary>
    public KahunaClusterResource Cluster { get; }

    /// <summary>The numeric Raft node ID. The first node has the ID 1.</summary>
    public int NodeId { get; }

    /// <summary>The cleartext HTTP/2 gRPC endpoint.</summary>
    public EndpointReference GrpcEndpoint => grpcEndpoint ??= new(this, KahunaResource.GrpcEndpointName);

    /// <summary>The HTTP endpoint for REST, the health probe and the dashboard.</summary>
    public EndpointReference HttpEndpoint => httpEndpoint ??= new(this, KahunaResource.HttpEndpointName);

    /// <summary>The Raft address of the node (<c>host:port</c>), as the other nodes dial it.</summary>
    public string RaftAddress => $"{Name}:{KahunaClusterResource.RaftTargetPort}";
}
