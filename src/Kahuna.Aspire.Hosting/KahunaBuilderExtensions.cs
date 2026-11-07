/*
 * This file is part of Kahuna
 *
 * For the full copyright and license information, please view the LICENSE.txt
 * file that was distributed with this source code.
 */

using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Utils;

namespace Aspire.Hosting;

/// <summary>Adds Kahuna resources to an Aspire application model.</summary>
public static class KahunaBuilderExtensions
{
    private const string PartitionsVariable = "KAHUNA_PARTITIONS";

    private const string HealthPath = "/v1/cluster/health";

    /// <summary>
    /// Adds a standalone Kahuna node that runs in a container.
    /// <para>
    /// The node exposes two endpoints: <c>grpc</c> (cleartext HTTP/2, the connection string) and
    /// <c>http</c> (REST, the health probe and the operator dashboard). Aspire waits for
    /// <c>/v1/cluster/health</c> to answer 200 before it reports the node healthy, so a resource that
    /// calls <c>WaitFor</c> on it starts only after the node can serve.
    /// </para>
    /// <para>
    /// The node sends its metrics, request spans and logs to the Aspire dashboard over OTLP when the
    /// server image supports it.
    /// </para>
    /// </summary>
    /// <param name="builder">The application builder.</param>
    /// <param name="name">The resource name. It is also the connection string name.</param>
    /// <param name="grpcPort">The host port of the gRPC endpoint. Random when not set.</param>
    /// <param name="httpPort">The host port of the HTTP endpoint. Random when not set.</param>
    public static IResourceBuilder<KahunaResource> AddKahuna(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        int? grpcPort = null,
        int? httpPort = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(name);

        KahunaResource resource = new(name);

        return builder.AddResource(resource).WithKahunaContainer(grpcPort, httpPort);
    }

    /// <summary>Sets the image, the endpoints, the health check and the telemetry of a Kahuna node.</summary>
    internal static IResourceBuilder<T> WithKahunaContainer<T>(this IResourceBuilder<T> builder, int? grpcPort, int? httpPort)
        where T : ContainerResource
    {
        return builder
            .WithImage(KahunaContainerImageTags.Image, KahunaContainerImageTags.Tag)
            .WithImageRegistry(KahunaContainerImageTags.Registry)
            .WithEndpoint(port: grpcPort, targetPort: KahunaResource.GrpcTargetPort, scheme: "http", name: KahunaResource.GrpcEndpointName)
            .WithEndpoint(KahunaResource.GrpcEndpointName, endpoint => endpoint.Transport = "http2")
            .WithHttpEndpoint(port: httpPort, targetPort: KahunaResource.HttpTargetPort, name: KahunaResource.HttpEndpointName)
            .WithHttpHealthCheck(HealthPath, endpointName: KahunaResource.HttpEndpointName)
            .WithUrlForEndpoint(KahunaResource.HttpEndpointName, url => url.DisplayText = "Dashboard")
            .WithUrlForEndpoint(KahunaResource.GrpcEndpointName, url => url.DisplayLocation = UrlDisplayLocation.DetailsOnly)
            .WithOtlpExporter();
    }

    /// <summary>
    /// Keeps the node's key-value data and Raft log in a named volume, so they survive a restart of
    /// the AppHost.
    /// </summary>
    /// <param name="builder">The Kahuna resource.</param>
    /// <param name="name">The volume name. Generated from the application and resource names when not set.</param>
    public static IResourceBuilder<KahunaResource> WithDataVolume(this IResourceBuilder<KahunaResource> builder, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithVolume(name ?? VolumeNameGenerator.Generate(builder, "data"), KahunaResource.DataTargetPath);
    }

    /// <summary>Keeps the node's key-value data and Raft log in a directory on the host.</summary>
    /// <param name="builder">The Kahuna resource.</param>
    /// <param name="source">The host directory.</param>
    public static IResourceBuilder<KahunaResource> WithDataBindMount(this IResourceBuilder<KahunaResource> builder, string source)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrEmpty(source);

        return builder.WithBindMount(source, KahunaResource.DataTargetPath);
    }

    /// <summary>
    /// Sets how many partitions the node creates when it starts with empty storage. Each partition
    /// is an independent Raft group. The image starts 3 when this is not set. A node that already
    /// has data keeps the partitions it has.
    /// </summary>
    /// <param name="builder">The Kahuna resource.</param>
    /// <param name="partitions">The number of partitions. Must be positive.</param>
    public static IResourceBuilder<KahunaResource> WithPartitions(this IResourceBuilder<KahunaResource> builder, int partitions)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(partitions);

        return builder.WithEnvironment(PartitionsVariable, partitions.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
