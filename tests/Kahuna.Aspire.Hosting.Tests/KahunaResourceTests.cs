/*
 * This file is part of Kahuna
 *
 * For the full copyright and license information, please view the LICENSE.txt
 * file that was distributed with this source code.
 */

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace Kahuna.Aspire.Hosting.Tests;

/// <summary>Checks the application model that the extensions build. No container is started.</summary>
public sealed class KahunaResourceTests
{
    private static IDistributedApplicationBuilder CreateBuilder() => DistributedApplication.CreateBuilder();

    [Fact]
    public void AddKahunaRunsThePinnedImage()
    {
        IDistributedApplicationBuilder builder = CreateBuilder();

        KahunaResource kahuna = builder.AddKahuna("kahuna").Resource;

        ContainerImageAnnotation image = Assert.Single(kahuna.Annotations.OfType<ContainerImageAnnotation>());
        Assert.Equal("docker.io", image.Registry);
        Assert.Equal("kahunakv/kahuna", image.Image);
        Assert.Equal(KahunaContainerImageTags.Tag, image.Tag);
    }

    [Fact]
    public void AddKahunaExposesAnH2cGrpcEndpointAndAnHttpEndpoint()
    {
        IDistributedApplicationBuilder builder = CreateBuilder();

        KahunaResource kahuna = builder.AddKahuna("kahuna", grpcPort: 18083, httpPort: 18081).Resource;

        Dictionary<string, EndpointAnnotation> endpoints = kahuna.Annotations.OfType<EndpointAnnotation>().ToDictionary(e => e.Name);

        Assert.Equal(2, endpoints.Count);

        EndpointAnnotation grpc = endpoints[KahunaResource.GrpcEndpointName];
        Assert.Equal(8083, grpc.TargetPort);
        Assert.Equal(18083, grpc.Port);
        Assert.Equal("http", grpc.UriScheme);
        Assert.Equal("http2", grpc.Transport);

        EndpointAnnotation http = endpoints[KahunaResource.HttpEndpointName];
        Assert.Equal(8081, http.TargetPort);
        Assert.Equal(18081, http.Port);
        Assert.Equal("http", http.UriScheme);
    }

    [Fact]
    public void ConnectionStringIsTheGrpcEndpointUrl()
    {
        IDistributedApplicationBuilder builder = CreateBuilder();

        KahunaResource kahuna = builder.AddKahuna("kahuna").Resource;

        Assert.Equal("{kahuna.bindings.grpc.url}", kahuna.ConnectionStringExpression.ValueExpression);
    }

    [Fact]
    public void ConnectionPropertiesNameTheGrpcAndHttpEndpoints()
    {
        IDistributedApplicationBuilder builder = CreateBuilder();

        IResourceWithConnectionString kahuna = builder.AddKahuna("kahuna").Resource;

        Dictionary<string, string> properties = kahuna.GetConnectionProperties().ToDictionary(p => p.Key, p => p.Value.ValueExpression);

        Assert.Equal("{kahuna.bindings.grpc.host}", properties["Host"]);
        Assert.Equal("{kahuna.bindings.grpc.port}", properties["Port"]);
        Assert.Equal("{kahuna.bindings.grpc.url}", properties["Uri"]);
        Assert.Equal("{kahuna.bindings.http.url}", properties["HttpUri"]);
    }

    [Fact]
    public void AddKahunaRegistersOneHealthCheckAndOtlpExport()
    {
        IDistributedApplicationBuilder builder = CreateBuilder();

        KahunaResource kahuna = builder.AddKahuna("kahuna").Resource;

        Assert.Single(kahuna.Annotations.OfType<HealthCheckAnnotation>());
        Assert.Single(kahuna.Annotations.OfType<OtlpExporterAnnotation>());
    }

    [Fact]
    public void WithDataVolumeMountsTheDataDirectory()
    {
        IDistributedApplicationBuilder builder = CreateBuilder();

        KahunaResource kahuna = builder.AddKahuna("kahuna").WithDataVolume("kahuna-data").Resource;

        ContainerMountAnnotation mount = Assert.Single(kahuna.Annotations.OfType<ContainerMountAnnotation>());
        Assert.Equal("kahuna-data", mount.Source);
        Assert.Equal("/data", mount.Target);
        Assert.Equal(ContainerMountType.Volume, mount.Type);
        Assert.False(mount.IsReadOnly);
    }

    [Fact]
    public void WithDataVolumeGeneratesANameWhenNoneIsGiven()
    {
        IDistributedApplicationBuilder builder = CreateBuilder();

        KahunaResource kahuna = builder.AddKahuna("kahuna").WithDataVolume().Resource;

        ContainerMountAnnotation mount = Assert.Single(kahuna.Annotations.OfType<ContainerMountAnnotation>());
        Assert.False(string.IsNullOrEmpty(mount.Source));
        Assert.EndsWith("kahuna-data", mount.Source);
    }

    [Fact]
    public void WithDataBindMountMountsTheHostDirectory()
    {
        IDistributedApplicationBuilder builder = CreateBuilder();

        string source = Path.Combine(Path.GetTempPath(), "kahuna-data");

        KahunaResource kahuna = builder.AddKahuna("kahuna").WithDataBindMount(source).Resource;

        ContainerMountAnnotation mount = Assert.Single(kahuna.Annotations.OfType<ContainerMountAnnotation>());
        Assert.Equal(ContainerMountType.BindMount, mount.Type);
        Assert.Equal("/data", mount.Target);
    }

    [Fact]
    public async Task WithPartitionsSetsThePartitionVariable()
    {
        IDistributedApplicationBuilder builder = CreateBuilder();

        KahunaResource kahuna = builder.AddKahuna("kahuna").WithPartitions(8).Resource;

        Dictionary<string, object> variables = [];

        foreach (EnvironmentCallbackAnnotation callback in kahuna.Annotations.OfType<EnvironmentCallbackAnnotation>())
            await callback.Callback(new EnvironmentCallbackContext(new DistributedApplicationExecutionContext(DistributedApplicationOperation.Run), variables));

        Assert.Equal("8", variables["KAHUNA_PARTITIONS"]);
    }

    [Fact]
    public void WithPartitionsRejectsZero()
    {
        IDistributedApplicationBuilder builder = CreateBuilder();

        IResourceBuilder<KahunaResource> kahuna = builder.AddKahuna("kahuna");

        Assert.Throws<ArgumentOutOfRangeException>(() => kahuna.WithPartitions(0));
    }
}
