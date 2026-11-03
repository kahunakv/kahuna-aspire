/*
 * This file is part of Kahuna
 *
 * For the full copyright and license information, please view the LICENSE.txt
 * file that was distributed with this source code.
 */

using System.Text;
using Kahuna.Client;
using Kahuna.Shared.Sequences;
using Microsoft.AspNetCore.RateLimiting;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// "kahuna" is the name of the resource in the AppHost, so it is also the connection string name.
builder.AddKahunaClient("kahuna");

// One budget for every replica of this service: the limiter keeps its counter in Kahuna, not in
// process memory. Call /limited more than 5 times in 10 seconds, on any replica, and the rest get 429.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddKahunaFixedWindowLimiter("shared", limiter =>
    {
        limiter.Key = "sample/rate/shared";
        limiter.PermitLimit = 5;
        limiter.Window = TimeSpan.FromSeconds(10);
    });
});

WebApplication app = builder.Build();

app.UseRateLimiter();

string replica = $"{Environment.MachineName}:{Environment.ProcessId}";

app.MapGet("/", () => Results.Text(
    """
    Kahuna Aspire sample

    PUT  /kv/{key}      store the request body
    GET  /kv/{key}      read it back
    POST /jobs/{id}     run a job under a distributed lock (2 s); a concurrent call gets 409
    POST /ids/{name}    next value of a cluster-wide sequence
    GET  /limited       5 calls per 10 s across every replica, then 429
    """));

app.MapPut("/kv/{key}", async (string key, HttpRequest request, KahunaClient kahuna, CancellationToken cancellationToken) =>
{
    using StreamReader reader = new(request.Body, Encoding.UTF8);
    string body = await reader.ReadToEndAsync(cancellationToken);

    KahunaKeyValue result = await kahuna.SetKeyValue($"sample/kv/{key}", Encoding.UTF8.GetBytes(body), cancellationToken: cancellationToken);

    return Results.Ok(new { key, result.Revision, replica });
});

app.MapGet("/kv/{key}", async (string key, KahunaClient kahuna, CancellationToken cancellationToken) =>
{
    KahunaKeyValue result = await kahuna.GetKeyValue($"sample/kv/{key}", cancellationToken: cancellationToken);

    return result.Success
        ? Results.Ok(new { key, value = result.ValueAsString(), result.Revision, replica })
        : Results.NotFound();
});

app.MapPost("/jobs/{id}", async (string id, KahunaClient kahuna, CancellationToken cancellationToken) =>
{
    // The lease outlives the job by a wide margin. If this replica dies mid-job, the lease runs out and
    // another caller can take the lock; the fencing token tells a downstream store which holder is newer.
    await using KahunaLock jobLock = await kahuna.GetOrCreateLock($"sample/jobs/{id}", TimeSpan.FromSeconds(30), cancellationToken: cancellationToken);

    if (!jobLock.IsAcquired)
        return Results.Conflict(new { id, message = "The job is running on another caller.", replica });

    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);

    return Results.Ok(new { id, jobLock.FencingToken, replica });
});

app.MapPost("/ids/{name}", async (string name, KahunaClient kahuna, CancellationToken cancellationToken) =>
{
    string sequence = $"sample/ids/{name}";

    try
    {
        return Results.Ok(new { name, id = await kahuna.NextSequenceValue(sequence, cancellationToken: cancellationToken), replica });
    }
    catch (KahunaException ex) when (ex.SequenceErrorCode == SequenceResponseType.NotFound)
    {
        try
        {
            await kahuna.CreateSequence(sequence, initialValue: 0, cancellationToken: cancellationToken);
        }
        catch (KahunaException created) when (created.SequenceErrorCode == SequenceResponseType.AlreadyExists)
        {
            // Another caller created it first.
        }

        return Results.Ok(new { name, id = await kahuna.NextSequenceValue(sequence, cancellationToken: cancellationToken), replica });
    }
});

app.MapGet("/limited", () => Results.Ok(new { message = "Within the shared budget.", replica }))
   .RequireRateLimiting("shared");

app.MapDefaultEndpoints();

app.Run();
