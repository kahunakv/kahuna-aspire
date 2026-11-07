/*
 * This file is part of Kahuna
 *
 * For the full copyright and license information, please view the LICENSE.txt
 * file that was distributed with this source code.
 */

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Kahuna.Aspire.Hosting.Tests;

/// <summary>Checks the application model of a cluster. No container is started.</summary>
public sealed class KahunaClusterTests
{
    private static IDistributedApplicationBuilder CreateBuilder() => DistributedApplication.CreateBuilder();

    [Fact]
    public void AddKahunaClusterAddsNodeContainersWithImageEndpointsAndParent()
    {
        IDistributedApplicationBuilder builder = CreateBuilder();

        KahunaClusterResource cluster = builder.AddKahunaCluster("kahuna").Resource;

        Assert.Same(cluster, Assert.Single(builder.Resources.OfType<KahunaClusterResource>()));
        Assert.Equal(["kahuna-1", "kahuna-2", "kahuna-3"], cluster.Nodes.Select(node => node.Name));
        Assert.Equal([1, 2, 3], cluster.Nodes.Select(node => node.NodeId));
        Assert.Equal(3, builder.Resources.OfType<KahunaClusterNodeResource>().Count());

        Assert.Equal("kahuna_check", Assert.Single(cluster.Annotations.OfType<HealthCheckAnnotation>()).Key);

        foreach (KahunaClusterNodeResource node in cluster.Nodes)
        {
            Assert.Same(cluster, node.Cluster);
            Assert.Equal("sh", node.Entrypoint);

            ContainerImageAnnotation image = Assert.Single(node.Annotations.OfType<ContainerImageAnnotation>());
            Assert.Equal("docker.io", image.Registry);
            Assert.Equal("kahunakv/kahuna", image.Image);
            Assert.Equal(KahunaContainerImageTags.Tag, image.Tag);

            Dictionary<string, EndpointAnnotation> endpoints = node.Annotations.OfType<EndpointAnnotation>().ToDictionary(e => e.Name);
            Assert.Equal(2, endpoints.Count);
            Assert.Equal(8083, endpoints[KahunaResource.GrpcEndpointName].TargetPort);
            Assert.Equal("http2", endpoints[KahunaResource.GrpcEndpointName].Transport);
            Assert.Equal(8081, endpoints[KahunaResource.HttpEndpointName].TargetPort);

            Assert.Single(node.Annotations.OfType<HealthCheckAnnotation>());
            Assert.Single(node.Annotations.OfType<OtlpExporterAnnotation>());

            // The node is shown under the cluster, but it must not inherit the health check of the cluster.
            ResourceRelationshipAnnotation parent = Assert.Single(node.Annotations.OfType<ResourceRelationshipAnnotation>(), r => r.Type == "Parent");
            Assert.Same(cluster, parent.Resource);
            Assert.IsNotAssignableFrom<IResourceWithParent>(node);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AddKahunaClusterRefusesLessThanOneNode(int nodes)
    {
        IDistributedApplicationBuilder builder = CreateBuilder();

        Assert.Throws<ArgumentOutOfRangeException>(() => builder.AddKahunaCluster("kahuna", nodes));
    }

    [Fact]
    public void ConnectionStringListsTheGrpcEndpointOfEveryNode()
    {
        IDistributedApplicationBuilder builder = CreateBuilder();

        KahunaClusterResource cluster = builder.AddKahunaCluster("kahuna").Resource;

        Assert.Equal(
            "{kahuna-1.bindings.grpc.url},{kahuna-2.bindings.grpc.url},{kahuna-3.bindings.grpc.url}",
            cluster.ConnectionStringExpression.ValueExpression);
    }

    [Fact]
    public void ConnectionPropertiesListEveryNode()
    {
        IDistributedApplicationBuilder builder = CreateBuilder();

        IResourceWithConnectionString cluster = builder.AddKahunaCluster("kahuna", nodes: 2).Resource;

        Dictionary<string, string> properties = cluster.GetConnectionProperties().ToDictionary(p => p.Key, p => p.Value.ValueExpression);

        Assert.Equal("{kahuna-1.bindings.grpc.url},{kahuna-2.bindings.grpc.url}", properties["Uri"]);
        Assert.Equal("{kahuna-1.bindings.http.url},{kahuna-2.bindings.http.url}", properties["HttpUri"]);
    }

    [Fact]
    public void NodeCommandListsTheOtherNodesAsPeers()
    {
        IDistributedApplicationBuilder builder = CreateBuilder();

        KahunaClusterResource cluster = builder.AddKahunaCluster("kahuna").Resource;

        Assert.Equal(
            "mkdir -p /data/data /data/wal && exec dotnet /app/Kahuna.Server.dll " +
            "--raft-nodename kahuna-2 --raft-nodeid 2 --raft-host kahuna-2 --raft-port 8082 " +
            "--http-ports 8081 --https-ports 8082 --grpc-cleartext-ports 8083 " +
            "--https-certificate /app/certificate.pfx --raft-allow-insecure-certificate-validation " +
            "--allow-plaintext-listener --disable-routing-hints " +
            "--initial-cluster-partitions ${KAHUNA_PARTITIONS:-3} " +
            "--storage rocksdb --storage-path /data/data --storage-revision v1 " +
            "--wal-storage rocksdb --wal-path /data/wal --wal-revision v1 " +
            "--initial-cluster kahuna-1:8082 kahuna-3:8082",
            KahunaClusterBuilderExtensions.BuildNodeCommand(cluster.Nodes[1]));
    }

    [Fact]
    public async Task NodeArgumentsRunTheCommandInAShell()
    {
        IDistributedApplicationBuilder builder = CreateBuilder();

        KahunaClusterNodeResource node = builder.AddKahunaCluster("kahuna").Resource.Nodes[0];

        List<object> args = [];

        foreach (CommandLineArgsCallbackAnnotation callback in node.Annotations.OfType<CommandLineArgsCallbackAnnotation>())
            await callback.Callback(new CommandLineArgsCallbackContext(args));

        Assert.Equal(["-c", KahunaClusterBuilderExtensions.BuildNodeCommand(node)], args);
    }

    [Fact]
    public void NodeCommandHasNoPeersInAOneNodeCluster()
    {
        IDistributedApplicationBuilder builder = CreateBuilder();

        KahunaClusterResource cluster = builder.AddKahunaCluster("kahuna", nodes: 1).Resource;

        Assert.DoesNotContain("--initial-cluster ", KahunaClusterBuilderExtensions.BuildNodeCommand(Assert.Single(cluster.Nodes)));
    }

    [Fact]
    public void WithDataVolumeMountsOneVolumePerNode()
    {
        IDistributedApplicationBuilder builder = CreateBuilder();

        KahunaClusterResource cluster = builder.AddKahunaCluster("kahuna").WithDataVolume("kahuna-data").Resource;

        Assert.Equal(
            ["kahuna-data-1", "kahuna-data-2", "kahuna-data-3"],
            cluster.Nodes.Select(node =>
            {
                ContainerMountAnnotation mount = Assert.Single(node.Annotations.OfType<ContainerMountAnnotation>());
                Assert.Equal("/data", mount.Target);
                Assert.Equal(ContainerMountType.Volume, mount.Type);
                return mount.Source;
            }));
    }

    [Fact]
    public void WithDataVolumeGeneratesDistinctNamesWithoutAPrefix()
    {
        IDistributedApplicationBuilder builder = CreateBuilder();

        KahunaClusterResource cluster = builder.AddKahunaCluster("kahuna").WithDataVolume().Resource;

        Assert.Equal(3, cluster.Nodes.Select(node => node.Annotations.OfType<ContainerMountAnnotation>().Single().Source).Distinct().Count());
    }

    [Fact]
    public void WithDataBindMountMountsOneSubdirectoryPerNode()
    {
        IDistributedApplicationBuilder builder = CreateBuilder();

        string source = Path.Combine(Path.GetTempPath(), "kahuna-cluster-data");

        KahunaClusterResource cluster = builder.AddKahunaCluster("kahuna", nodes: 2).WithDataBindMount(source).Resource;

        Assert.Equal(
            [Path.Combine(source, "kahuna-1"), Path.Combine(source, "kahuna-2")],
            cluster.Nodes.Select(node => node.Annotations.OfType<ContainerMountAnnotation>().Single().Source));
    }

    [Fact]
    public async Task WithPartitionsSetsThePartitionVariableOnEveryNode()
    {
        IDistributedApplicationBuilder builder = CreateBuilder();

        KahunaClusterResource cluster = builder.AddKahunaCluster("kahuna").WithPartitions(8).Resource;

        foreach (KahunaClusterNodeResource node in cluster.Nodes)
        {
            Dictionary<string, object> variables = [];

            foreach (EnvironmentCallbackAnnotation callback in node.Annotations.OfType<EnvironmentCallbackAnnotation>())
                await callback.Callback(new EnvironmentCallbackContext(new DistributedApplicationExecutionContext(DistributedApplicationOperation.Run), variables));

            Assert.Equal("8", variables["KAHUNA_PARTITIONS"]);
        }
    }

    [Fact]
    public void ConfigureNodesAppliesToEveryNode()
    {
        IDistributedApplicationBuilder builder = CreateBuilder();

        KahunaClusterResource cluster = builder.AddKahunaCluster("kahuna").ConfigureNodes(node => node.WithImageTag("local")).Resource;

        Assert.All(cluster.Nodes, node => Assert.Equal("local", node.Annotations.OfType<ContainerImageAnnotation>().Single().Tag));
    }

    [Theory]
    [InlineData(new[] { "NotStarted", "NotStarted", "NotStarted" }, false, "NotStarted", false)]
    [InlineData(new[] { "Starting", "Running", "NotStarted" }, false, "Starting", false)]
    [InlineData(new[] { "Running", "Running", "Running" }, false, "Running", true)]
    [InlineData(new[] { "Running", "Exited", "Running" }, true, "Running", true)]
    [InlineData(new[] { "Running", "Starting", "Running" }, true, "Running", true)]
    [InlineData(new[] { "Exited", "Exited", "Exited" }, true, "Exited", false)]
    [InlineData(new[] { "FailedToStart", "Exited", "Exited" }, false, "FailedToStart", false)]
    [InlineData(new[] { "FailedToStart", "Starting", "Running" }, false, "FailedToStart", false)]
    public void ClusterStateFollowsTheNodes(string[] nodeStates, bool reachedRunning, string expected, bool expectedReachedRunning)
    {
        string state = KahunaClusterStateTracker.GetClusterState(nodeStates, ref reachedRunning);

        Assert.Equal(expected, state);
        Assert.Equal(expectedReachedRunning, reachedRunning);
    }

    [Theory]
    [InlineData(3, false, HealthStatus.Healthy)]
    [InlineData(2, false, HealthStatus.Unhealthy)]
    [InlineData(2, true, HealthStatus.Healthy)]
    [InlineData(1, true, HealthStatus.Unhealthy)]
    [InlineData(0, true, HealthStatus.Unhealthy)]
    public async Task ClusterHealthNeedsAllNodesToStartAndAMajorityAfterThat(int healthyNodes, bool startCompleted, HealthStatus expected)
    {
        IDistributedApplicationBuilder builder = CreateBuilder();

        KahunaClusterResource cluster = builder.AddKahunaCluster("kahuna").Resource;
        cluster.StartCompleted = startCompleted;

        for (int i = 0; i < cluster.Nodes.Count; i++)
            cluster.NodeStatus[cluster.Nodes[i]] = (KnownResourceStates.Running, i < healthyNodes ? HealthStatus.Healthy : HealthStatus.Unhealthy);

        KahunaClusterHealthCheck check = new(cluster);

        HealthCheckContext context = new()
        {
            Registration = new HealthCheckRegistration("kahuna_check", check, HealthStatus.Unhealthy, tags: null),
        };

        HealthCheckResult result = await check.CheckHealthAsync(context, TestContext.Current.CancellationToken);

        Assert.Equal(expected, result.Status);
        Assert.Contains($"{healthyNodes}", result.Description);
        Assert.Equal(startCompleted || healthyNodes == cluster.Nodes.Count, cluster.StartCompleted);
    }
}
