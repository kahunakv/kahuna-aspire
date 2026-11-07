/*
 * This file is part of Kahuna
 *
 * For the full copyright and license information, please view the LICENSE.txt
 * file that was distributed with this source code.
 */

using Kahuna.Client;
using Kahuna.Shared.Communication.Rest;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Kahuna.Aspire.Client;

/// <summary>
/// Asks the nodes for their cluster membership. The call needs no admin key and reads in-memory state
/// only, so it is cheap enough to run on every probe. A node can serve when its Raft state is initialized.
/// <para>
/// The client sends each call to the next endpoint, also when that node is down. The check thus makes one
/// call for each endpoint, and it is healthy when one node can serve. One stopped node of a cluster does
/// not make the service unhealthy.
/// </para>
/// <para>
/// With more than one endpoint, each call has a time limit. In Aspire, the proxy of a stopped container
/// accepts the connection and does not answer, so the call would wait and hold back the other endpoints.
/// </para>
/// </summary>
internal sealed class KahunaHealthCheck(
    Func<CancellationToken, Task<KahunaClusterMembershipResponse>> getMembership,
    int endpoints,
    TimeSpan attemptTimeout) : IHealthCheck
{
    private static readonly TimeSpan DefaultAttemptTimeout = TimeSpan.FromSeconds(2);

    public KahunaHealthCheck(KahunaClient client, int endpoints) : this(client.GetClusterMembership, endpoints, DefaultAttemptTimeout)
    {
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        bool answered = false;
        Exception? error = null;

        for (int attempt = 0; attempt < endpoints; attempt++)
        {
            using CancellationTokenSource attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            if (endpoints > 1)
                attemptCancellation.CancelAfter(attemptTimeout);

            try
            {
                KahunaClusterMembershipResponse membership = await getMembership(attemptCancellation.Token).ConfigureAwait(false);

                if (membership.Initialized)
                    return HealthCheckResult.Healthy();

                answered = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                error = ex;
            }
        }

        return answered
            ? new(context.Registration.FailureStatus, "A Kahuna node is up, but no node has initialized its Raft state yet.", error)
            : new(context.Registration.FailureStatus, "No Kahuna node answered.", error);
    }
}
