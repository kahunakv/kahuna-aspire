/*
 * This file is part of Kahuna
 *
 * For the full copyright and license information, please view the LICENSE.txt
 * file that was distributed with this source code.
 */

using System.Globalization;
using System.Text;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Aspire.Hosting;

/// <summary>Adds a Kahuna cluster to an Aspire application model.</summary>
public static class KahunaClusterBuilderExtensions
{
    private const string PartitionsVariable = "KAHUNA_PARTITIONS";

    /// <summary>
    /// Adds a Kahuna cluster. The cluster runs <paramref name="nodes"/> containers of the
    /// <c>kahunakv/kahuna</c> image, named <c>{name}-1</c>, <c>{name}-2</c>, and so on. The nodes reach
    /// each other by these names on the container network.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The nodes use the development profile of <c>scripts/run-cluster.sh</c> in the Kahuna repository.
    /// They connect to each other over TLS with the development certificate of the image, and they do not
    /// validate the certificate of a peer. The profile has no node authentication, so use it for local
    /// development only.
    /// </para>
    /// <para>
    /// The connection string lists the gRPC endpoints of all nodes. Every node accepts every operation and
    /// forwards it to the node that owns the resource.
    /// </para>
    /// </remarks>
    /// <param name="builder">The application builder.</param>
    /// <param name="name">The name of the cluster resource. It is also the connection string name.</param>
    /// <param name="nodes">
    /// The number of nodes. Use an odd number: a cluster commits while a majority of its nodes runs, so 3
    /// nodes continue after the loss of 1, and 5 nodes after the loss of 2. The default is 3.
    /// </param>
    public static IResourceBuilder<KahunaClusterResource> AddKahunaCluster(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        int nodes = 3)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentOutOfRangeException.ThrowIfLessThan(nodes, 1);

        KahunaClusterResource cluster = new(name);

        string healthCheckKey = $"{name}_check";

        builder.Services.AddHealthChecks().Add(new HealthCheckRegistration(
            healthCheckKey,
            _ => new KahunaClusterHealthCheck(cluster),
            failureStatus: default,
            tags: default));

        IResourceBuilder<KahunaClusterResource> clusterBuilder = builder.AddResource(cluster)
            .WithInitialState(new CustomResourceSnapshot
            {
                ResourceType = "KahunaCluster",
                State = KnownResourceStates.NotStarted,
                Properties = [],
            })
            .WithIconName("DatabaseMultiple")
            .WithHealthCheck(healthCheckKey)
            .OnInitializeResource(KahunaClusterStateTracker.StartAsync);

        for (int nodeId = 1; nodeId <= nodes; nodeId++)
            cluster.AddNode(new KahunaClusterNodeResource($"{name}-{nodeId}", cluster, nodeId));

        foreach (KahunaClusterNodeResource node in cluster.Nodes)
            AddNode(builder, node);

        return clusterBuilder;
    }

    /// <summary>
    /// Applies a configuration to every node, for example an image tag or an environment variable. All
    /// nodes must run the same image and the same settings.
    /// </summary>
    /// <param name="builder">The Kahuna cluster.</param>
    /// <param name="configure">The configuration to apply to the builder of each node.</param>
    public static IResourceBuilder<KahunaClusterResource> ConfigureNodes(
        this IResourceBuilder<KahunaClusterResource> builder,
        Action<IResourceBuilder<KahunaClusterNodeResource>> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        foreach (KahunaClusterNodeResource node in builder.Resource.Nodes)
            configure(builder.ApplicationBuilder.CreateResourceBuilder(node));

        return builder;
    }

    /// <summary>
    /// Keeps the key-value data and the Raft log of each node in its own named volume, so they survive a
    /// restart of the AppHost.
    /// </summary>
    /// <param name="builder">The Kahuna cluster.</param>
    /// <param name="name">
    /// The prefix of the volume names. Each node gets the volume <c>{name}-{node ID}</c>. Generated from the
    /// application and node names when not set.
    /// </param>
    public static IResourceBuilder<KahunaClusterResource> WithDataVolume(this IResourceBuilder<KahunaClusterResource> builder, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.ConfigureNodes(node => node.WithVolume(
            name is null ? VolumeNameGenerator.Generate(node, "data") : $"{name}-{node.Resource.NodeId}",
            KahunaResource.DataTargetPath));
    }

    /// <summary>
    /// Keeps the key-value data and the Raft log of each node in a directory on the host. Each node mounts
    /// the subdirectory of <paramref name="source"/> that has the name of the node.
    /// </summary>
    /// <param name="builder">The Kahuna cluster.</param>
    /// <param name="source">The host directory that holds one subdirectory for each node.</param>
    public static IResourceBuilder<KahunaClusterResource> WithDataBindMount(this IResourceBuilder<KahunaClusterResource> builder, string source)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(source);

        return builder.ConfigureNodes(node => node.WithBindMount(Path.Combine(source, node.Resource.Name), KahunaResource.DataTargetPath));
    }

    /// <summary>
    /// Sets how many partitions the cluster creates when it starts with empty storage. Each partition is
    /// an independent Raft group. The cluster starts 3 when this is not set. A cluster that already has
    /// data keeps the partitions it has.
    /// </summary>
    /// <param name="builder">The Kahuna cluster.</param>
    /// <param name="partitions">The number of partitions. Must be positive.</param>
    public static IResourceBuilder<KahunaClusterResource> WithPartitions(this IResourceBuilder<KahunaClusterResource> builder, int partitions)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(partitions);

        string value = partitions.ToString(CultureInfo.InvariantCulture);

        return builder.ConfigureNodes(node => node.WithEnvironment(PartitionsVariable, value));
    }

    private static void AddNode(IDistributedApplicationBuilder builder, KahunaClusterNodeResource node)
    {
        builder.AddResource(node)
            .WithKahunaContainer(grpcPort: null, httpPort: null)
            .WithIconName("Database")
            .WithParentRelationship(node.Cluster)
            .WithEntrypoint("sh")
            .WithArgs(context =>
            {
                context.Args.Add("-c");
                context.Args.Add(BuildNodeCommand(node));
            });
    }

    // The image entrypoint starts a standalone node with fixed flags, so a cluster node needs its own
    // command. The flags follow scripts/run-cluster.sh in the Kahuna repository, with these differences:
    //
    // - The host is the container name, so each node can use the same ports.
    // - --disable-routing-hints: a hint names the Raft address of a node, and a client outside the container
    //   network cannot reach it. The client refuses such a hint and counts it as rejected.
    // - The shell reads the partition count, so WithPartitions and a node configuration can change it.
    //
    // --initial-cluster lists every other node, never the node itself. A one-node cluster omits the flag,
    // because the server refuses a flag with no value.
    internal static string BuildNodeCommand(KahunaClusterNodeResource node)
    {
        string dataPath = $"{KahunaResource.DataTargetPath}/data";
        string walPath = $"{KahunaResource.DataTargetPath}/wal";

        StringBuilder command = new($"mkdir -p {dataPath} {walPath} && exec dotnet /app/Kahuna.Server.dll");

        command.Append($" --raft-nodename {node.Name}");
        command.Append($" --raft-nodeid {node.NodeId}");
        command.Append($" --raft-host {node.Name}");
        command.Append($" --raft-port {KahunaClusterResource.RaftTargetPort}");
        command.Append($" --http-ports {KahunaResource.HttpTargetPort}");
        command.Append($" --https-ports {KahunaClusterResource.RaftTargetPort}");
        command.Append($" --grpc-cleartext-ports {KahunaResource.GrpcTargetPort}");
        command.Append($" --https-certificate {KahunaClusterResource.CertificatePath}");
        command.Append(" --raft-allow-insecure-certificate-validation");
        command.Append(" --allow-plaintext-listener");
        command.Append(" --disable-routing-hints");
        command.Append($" --initial-cluster-partitions ${{{PartitionsVariable}:-3}}");
        command.Append($" --storage rocksdb --storage-path {dataPath} --storage-revision v1");
        command.Append($" --wal-storage rocksdb --wal-path {walPath} --wal-revision v1");

        List<KahunaClusterNodeResource> peers = [.. node.Cluster.Nodes.Where(peer => peer != node)];

        if (peers.Count > 0)
        {
            command.Append(" --initial-cluster");

            foreach (KahunaClusterNodeResource peer in peers)
                command.Append(' ').Append(peer.RaftAddress);
        }

        return command.ToString();
    }
}
