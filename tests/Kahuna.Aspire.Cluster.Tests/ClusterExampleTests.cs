/*
 * This file is part of Kahuna
 *
 * For the full copyright and license information, please view the LICENSE.txt
 * file that was distributed with this source code.
 */

using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Kahuna.Aspire.Cluster.Tests;

/// <summary>
/// Starts the cluster example with three real containers. These tests need Docker. They use a temporary
/// directory for the data of the nodes, not the volumes of the example.
/// </summary>
public sealed class ClusterExampleTests
{
    private const string ClusterName = "kahuna";

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task ClusterContinuesWhileAMajorityOfTheNodesRuns()
    {
        using CancellationTokenSource timeout = new(Timeout);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, TestContext.Current.CancellationToken);
        CancellationToken cancellationToken = linked.Token;

        string dataDirectory = Directory.CreateTempSubdirectory("kahuna-cluster-tests-").FullName;

        try
        {
            IDistributedApplicationTestingBuilder builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.ClusterExample_AppHost>(cancellationToken);

            UseDataDirectory(builder, dataDirectory);

            await using DistributedApplication app = await builder.BuildAsync(cancellationToken);
            await app.StartAsync(cancellationToken);

            await app.ResourceNotifications.WaitForResourceHealthyAsync("api", cancellationToken);
            await app.ResourceNotifications.WaitForResourceHealthyAsync(ClusterName, cancellationToken);

            // The connection string names each node.
            string? connectionString = await app.GetConnectionStringAsync(ClusterName, cancellationToken);
            Assert.Equal(3, connectionString?.Split(',').Length);

            using HttpClient http = app.CreateHttpClient("api");

            await AddNoteAsync(http, "before the stop", cancellationToken);

            // Two of three nodes are a majority, so the cluster stays healthy with a count of the healthy nodes.
            await ExecuteAsync(app, "kahuna-3", KnownResourceCommands.StopCommand, cancellationToken);

            ResourceEvent majority = await WaitForHealthReportAsync(app, "2 of 3", cancellationToken);
            Assert.Equal(KnownResourceStates.Running, majority.Snapshot.State?.Text);
            Assert.Equal(HealthStatus.Healthy, majority.Snapshot.HealthStatus);

            await AddNoteAsync(http, "one node down", cancellationToken);

            // A resource that waits for the cluster can restart while the node is down.
            await ExecuteAsync(app, "api", KnownResourceCommands.RestartCommand, cancellationToken);
            await app.ResourceNotifications.WaitForResourceHealthyAsync("api", cancellationToken);

            using HttpClient restartedHttp = app.CreateHttpClient("api");

            await AddNoteAsync(restartedHttp, "after the api restart", cancellationToken);

            // The stopped node comes back and serves the notes that it did not see.
            await ExecuteAsync(app, "kahuna-3", KnownResourceCommands.StartCommand, cancellationToken);
            await WaitForHealthReportAsync(app, "All 3", cancellationToken);

            string page = await restartedHttp.GetStringAsync("/", cancellationToken);

            Assert.Contains("before the stop", page);
            Assert.Contains("one node down", page);
            Assert.Contains("after the api restart", page);
            Assert.DoesNotContain("class=\"down\"", page);

            // One node of three is not a majority.
            await ExecuteAsync(app, "kahuna-2", KnownResourceCommands.StopCommand, cancellationToken);
            await ExecuteAsync(app, "kahuna-3", KnownResourceCommands.StopCommand, cancellationToken);

            ResourceEvent noMajority = await WaitForHealthReportAsync(app, "1 of 3", cancellationToken);
            Assert.Equal(HealthStatus.Unhealthy, noMajority.Snapshot.HealthStatus);

            await app.StopAsync(cancellationToken);
        }
        finally
        {
            DeleteDataDirectory(dataDirectory);
        }
    }

    private static void UseDataDirectory(IDistributedApplicationTestingBuilder builder, string dataDirectory)
    {
        KahunaClusterResource cluster = builder.Resources.OfType<KahunaClusterResource>().Single();

        foreach (KahunaClusterNodeResource node in cluster.Nodes)
        {
            Directory.CreateDirectory(Path.Combine(dataDirectory, node.Name));

            foreach (ContainerMountAnnotation mount in node.Annotations.OfType<ContainerMountAnnotation>().ToList())
                node.Annotations.Remove(mount);
        }

        builder.CreateResourceBuilder(cluster).WithDataBindMount(dataDirectory);
    }

    // The example sends a note again when a node does not answer, so a stopped node does not fail this call.
    private static async Task AddNoteAsync(HttpClient http, string text, CancellationToken cancellationToken)
    {
        using FormUrlEncodedContent form = new(new Dictionary<string, string> { ["text"] = text });
        using HttpResponseMessage response = await http.PostAsync("/notes", form, cancellationToken);

        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
    }

    private static async Task ExecuteAsync(DistributedApplication app, string resourceName, string command, CancellationToken cancellationToken)
    {
        IResource resource = app.Services.GetRequiredService<DistributedApplicationModel>().Resources.Single(r => r.Name == resourceName);

        ExecuteCommandResult result = await app.Services.GetRequiredService<ResourceCommandService>().ExecuteCommandAsync(resource, command, cancellationToken);

        Assert.True(result.Success, result.Message);
    }

    private static Task<ResourceEvent> WaitForHealthReportAsync(DistributedApplication app, string description, CancellationToken cancellationToken) =>
        app.ResourceNotifications.WaitForResourceAsync(
            ClusterName,
            e => e.Snapshot.HealthReports.Any(report => report.Name == $"{ClusterName}_check" && report.Description?.StartsWith(description) == true),
            cancellationToken);

    // A node can leave files that the test user cannot delete, and they must not fail the test.
    private static void DeleteDataDirectory(string dataDirectory)
    {
        try
        {
            Directory.Delete(dataDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
