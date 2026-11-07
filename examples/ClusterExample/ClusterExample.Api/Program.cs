/*
 * This file is part of Kahuna
 *
 * For the full copyright and license information, please view the LICENSE.txt
 * file that was distributed with this source code.
 */

using System.Diagnostics;
using System.Net;
using System.Text;
using Kahuna.Client;
using Kahuna.Shared.Communication.Rest;
using Kahuna.Shared.KeyValue;

const string NotesBucket = "notes/";

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// The AppHost gives "kahuna" a connection string that lists the gRPC endpoint of each cluster node.
builder.AddKahunaClient("kahuna");

WebApplication app = builder.Build();

// One more client for each node, so the page can ask each node by itself.
string[] endpoints = app.Configuration.GetConnectionString("kahuna")!.Split(',');
Dictionary<string, KahunaClient> nodes = endpoints.ToDictionary(endpoint => endpoint, endpoint => new KahunaClient(endpoint));

app.MapHealthChecks("/health");

// Stop a node in the dashboard, and the page shows that node as down while you can still add notes.
app.MapGet("/", async (KahunaClient kahuna, CancellationToken cancellationToken) =>
{
    StringBuilder html = new();

    html.Append("""
        <!doctype html>
        <html><head><meta charset="utf-8"><title>Kahuna cluster example</title>
        <style>
          body { font-family: system-ui, sans-serif; margin: 2rem; max-width: 56rem; }
          table { border-collapse: collapse; width: 100%; margin-bottom: 1.5rem; }
          th, td { border-bottom: 1px solid #ccc; padding: .4rem; text-align: left; }
          .up { color: #18794e; } .down { color: #c62828; }
        </style></head><body>
        <h1>Kahuna cluster example</h1>
        <h2>Nodes</h2>
        <table><tr><th>Endpoint</th><th>Status</th><th>Role</th><th>Members</th><th>Notes through this node</th><th>Time</th></tr>
        """);

    foreach ((string endpoint, KahunaClient node) in nodes)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        // A stopped node may not refuse the connection at once, so the page waits 2 seconds at most.
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));

        try
        {
            KahunaClusterMembershipResponse membership = await node.GetClusterMembership(timeout.Token);

            // A running node cannot read when the cluster has no majority, so this read has its own result.
            string notes;

            try
            {
                notes = (await node.GetByBucket(NotesBucket, KeyValueDurability.Persistent, cancellationToken: timeout.Token)).Count.ToString();
            }
            catch (Exception)
            {
                notes = "cannot read";
            }

            html.Append($"<tr><td>{Encode(endpoint)}</td><td class=\"up\">up</td><td>{Encode(membership.LocalRole)}</td><td>{membership.Members.Count}</td><td>{notes}</td><td>{stopwatch.ElapsedMilliseconds} ms</td></tr>");
        }
        catch (Exception ex)
        {
            html.Append($"<tr><td>{Encode(endpoint)}</td><td class=\"down\">down</td><td>-</td><td>-</td><td title=\"{Encode(ex.Message)}\">-</td><td>{stopwatch.ElapsedMilliseconds} ms</td></tr>");
        }
    }

    html.Append("""
        </table>
        <h2>Notes</h2>
        <form method="post" action="/notes">
          <input name="text" required maxlength="200" placeholder="Write a note" autofocus>
          <button type="submit">Add</button>
        </form>
        <ul>
        """);

    try
    {
        // This read uses the client of the cluster, so it goes to the next node in turn.
        List<KahunaKeyValue> notes = await RetryAsync(token => 
            kahuna.GetByBucket(
                NotesBucket, 
                KeyValueDurability.Persistent, 
                cancellationToken: token
            ), cancellationToken);

        foreach (KahunaKeyValue note in notes.OrderByDescending(note => note.Key).Take(20))
            html.Append($"<li>{Encode(note.ValueAsString() ?? "")}</li>");
    }
    catch (Exception ex)
    {
        html.Append($"<li class=\"down\" title=\"{Encode(ex.Message)}\">The cluster cannot read now.</li>");
    }

    html.Append("</ul></body></html>");

    return Results.Content(html.ToString(), "text/html");
});

app.MapPost("/notes", async (KahunaClient kahuna, HttpRequest request, CancellationToken cancellationToken) =>
{
    IFormCollection form = await request.ReadFormAsync(cancellationToken);
    string text = form["text"].ToString().Trim();

    if (text.Length > 0)
    {
        // A failed request can still have run. All tries use the same key, so a retry cannot add a second note.
        string key = $"{NotesBucket}{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds():D16}-{Guid.NewGuid():N}";

        await RetryAsync(token => kahuna.SetKeyValue(key, text, cancellationToken: token), cancellationToken);
    }

    return Results.Redirect("/");
});

app.Run();

// The client sends each request to the next node in turn, also when that node is down, and it does not
// send a failed request again. The example thus tries again, and the next try goes to another node.
//
// Each try has a time limit, because the Aspire proxy of a stopped container accepts the connection and
// does not answer. The tries also cover the short time that the partitions of a stopped leader need to
// elect a new leader.
async Task<T> RetryAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
{
    for (int attempt = 1; ; attempt++)
    {
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));

        try
        {
            return await action(timeout.Token);
        }
        catch (Exception) when (attempt < 2 * endpoints.Length && !cancellationToken.IsCancellationRequested)
        {
        }
    }
}

static string Encode(string value) => WebUtility.HtmlEncode(value);
