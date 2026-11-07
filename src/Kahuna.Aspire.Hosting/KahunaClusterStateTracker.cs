/*
 * This file is part of Kahuna
 *
 * For the full copyright and license information, please view the LICENSE.txt
 * file that was distributed with this source code.
 */

using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aspire.Hosting;

/// <summary>Publishes the state of a Kahuna cluster from the states of the node containers.</summary>
/// <remarks>
/// <para>
/// Aspire sets the state of a container. The cluster is not a container, so nothing else sets its state.
/// Without a state, the health check of the cluster never runs, the cluster never becomes ready, and
/// <c>WaitFor</c> on the cluster waits forever.
/// </para>
/// <para>
/// The cluster is <c>Starting</c> until all nodes are running for the first time. After that, it stays
/// <c>Running</c> while at least one node runs, because the health check reports the nodes that are down.
/// When all nodes stop, the cluster is <c>Exited</c>, or <c>FailedToStart</c> if a node failed to start.
/// </para>
/// </remarks>
internal static class KahunaClusterStateTracker
{
    public static Task StartAsync(KahunaClusterResource cluster, InitializeResourceEvent @event, CancellationToken cancellationToken)
    {
        IHostApplicationLifetime lifetime = @event.Services.GetRequiredService<IHostApplicationLifetime>();

        // The initialize event must not block, so the watch runs in the background until the AppHost stops.
        _ = Task.Run(() => WatchAsync(cluster, @event, lifetime.ApplicationStopping), CancellationToken.None);

        return Task.CompletedTask;
    }

    internal static string GetClusterState(IEnumerable<string?> nodeStates, ref bool reachedRunning)
    {
        List<string?> states = [.. nodeStates];

        if (states.All(state => state is not null && KnownResourceStates.TerminalStates.Contains(state)))
        {
            reachedRunning = false;
            return states.Contains(KnownResourceStates.FailedToStart) ? KnownResourceStates.FailedToStart : KnownResourceStates.Exited;
        }

        if (states.All(state => state == KnownResourceStates.Running))
            reachedRunning = true;

        if (reachedRunning)
            return KnownResourceStates.Running;

        if (states.Contains(KnownResourceStates.FailedToStart))
            return KnownResourceStates.FailedToStart;

        if (states.All(state => state is null || state == KnownResourceStates.NotStarted))
            return KnownResourceStates.NotStarted;

        return KnownResourceStates.Starting;
    }

    private static async Task WatchAsync(KahunaClusterResource cluster, InitializeResourceEvent @event, CancellationToken cancellationToken)
    {
        ResourceNotificationService notifications = @event.Notifications;
        Dictionary<KahunaClusterNodeResource, string?> nodeStates = cluster.Nodes.ToDictionary(node => node, _ => (string?)null);

        string? publishedState = null;
        bool reachedRunning = false;

        try
        {
            await foreach (ResourceEvent resourceEvent in notifications.WatchAsync(cancellationToken).ConfigureAwait(false))
            {
                if (resourceEvent.Resource is not KahunaClusterNodeResource node || node.Cluster != cluster)
                    continue;

                nodeStates[node] = resourceEvent.Snapshot.State?.Text;
                cluster.NodeStatus[node] = (resourceEvent.Snapshot.State?.Text, resourceEvent.Snapshot.HealthStatus);

                string state = GetClusterState(nodeStates.Values, ref reachedRunning);

                if (state == publishedState)
                    continue;

                bool becameRunning = state == KnownResourceStates.Running && publishedState != KnownResourceStates.Running;
                publishedState = state;

                if (KnownResourceStates.TerminalStates.Contains(state))
                    cluster.StartCompleted = false;

                await PublishStateAsync(cluster, notifications, state).ConfigureAwait(false);

                // The orchestrator shows a connection string in the dashboard when this event fires. It
                // fires the event for a container only, so the cluster fires it for itself.
                if (becameRunning)
                    await @event.Eventing.PublishAsync(new ConnectionStringAvailableEvent(cluster, @event.Services), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            @event.Logger.LogError(ex, "Stopped tracking the state of Kahuna cluster '{ClusterName}'.", cluster.Name);
        }
    }

    private static Task PublishStateAsync(KahunaClusterResource cluster, ResourceNotificationService notifications, string state)
    {
        DateTime? startTimeStamp = state == KnownResourceStates.Running ? DateTime.UtcNow : null;
        DateTime? stopTimeStamp = KnownResourceStates.TerminalStates.Contains(state) ? DateTime.UtcNow : null;

        return notifications.PublishUpdateAsync(cluster, snapshot => snapshot with
        {
            State = state,
            StartTimeStamp = startTimeStamp ?? snapshot.StartTimeStamp,
            StopTimeStamp = stopTimeStamp,
        });
    }
}
