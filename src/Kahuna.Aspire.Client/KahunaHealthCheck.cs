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
/// Asks one node for its cluster membership. The call needs no admin key and reads in-memory state
/// only, so it is cheap enough to run on every probe. The node is healthy when its Raft state is
/// initialized, which is the point from which it can serve.
/// </summary>
internal sealed class KahunaHealthCheck(KahunaClient client) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            KahunaClusterMembershipResponse membership = await client.GetClusterMembership(cancellationToken).ConfigureAwait(false);

            return membership.Initialized
                ? HealthCheckResult.Healthy()
                : new(context.Registration.FailureStatus, "The Kahuna node is up, but its Raft state is not initialized yet.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return new(context.Registration.FailureStatus, "The Kahuna node did not answer.", ex);
        }
    }
}
