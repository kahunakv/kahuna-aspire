/*
 * This file is part of Kahuna
 *
 * For the full copyright and license information, please view the LICENSE.txt
 * file that was distributed with this source code.
 */

namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// A Kahuna node that runs as a container.
/// <para>
/// The connection string is the URL of the node's cleartext HTTP/2 (h2c) gRPC endpoint. That is the
/// endpoint <c>KahunaClient</c> uses by default: a gRPC client cannot reach the plain HTTP endpoint
/// without TLS, because nothing negotiates HTTP/2 there.
/// </para>
/// </summary>
/// <param name="name">The name of the resource.</param>
public sealed class KahunaResource(string name) : ContainerResource(name), IResourceWithConnectionString
{
    /// <summary>The name of the cleartext HTTP/2 gRPC endpoint (container port 8083).</summary>
    public const string GrpcEndpointName = "grpc";

    /// <summary>The name of the HTTP endpoint for REST, the health probe and the dashboard (container port 8081).</summary>
    public const string HttpEndpointName = "http";

    internal const int GrpcTargetPort = 8083;

    internal const int HttpTargetPort = 8081;

    internal const string DataTargetPath = "/data";

    private EndpointReference? grpcEndpoint;

    private EndpointReference? httpEndpoint;

    /// <summary>The cleartext HTTP/2 gRPC endpoint.</summary>
    public EndpointReference GrpcEndpoint => grpcEndpoint ??= new(this, GrpcEndpointName);

    /// <summary>The HTTP endpoint for REST, the health probe and the dashboard.</summary>
    public EndpointReference HttpEndpoint => httpEndpoint ??= new(this, HttpEndpointName);

    /// <summary>The host of the gRPC endpoint.</summary>
    public EndpointReferenceExpression Host => GrpcEndpoint.Property(EndpointProperty.Host);

    /// <summary>The port of the gRPC endpoint.</summary>
    public EndpointReferenceExpression Port => GrpcEndpoint.Property(EndpointProperty.Port);

    /// <summary>The URL of the gRPC endpoint, for example <c>http://localhost:52341</c>.</summary>
    public ReferenceExpression ConnectionStringExpression =>
        ReferenceExpression.Create($"{GrpcEndpoint.Property(EndpointProperty.Url)}");

    /// <summary>
    /// The parts of the connection, for a consumer that is not .NET. Each becomes an environment
    /// variable named <c>{RESOURCE}_{PROPERTY}</c> on a resource that references this one.
    /// </summary>
    IEnumerable<KeyValuePair<string, ReferenceExpression>> IResourceWithConnectionString.GetConnectionProperties()
    {
        yield return new("Host", ReferenceExpression.Create($"{Host}"));
        yield return new("Port", ReferenceExpression.Create($"{Port}"));
        yield return new("Uri", ConnectionStringExpression);
        yield return new("HttpUri", ReferenceExpression.Create($"{HttpEndpoint.Property(EndpointProperty.Url)}"));
    }
}
