# Kahuna cluster example

A small Aspire application that runs a Kahuna cluster of three nodes, and a web page that uses it.

| Resource | What it is |
| --- | --- |
| `kahuna` | The cluster. It has no container of its own. |
| `kahuna-1`, `kahuna-2`, `kahuna-3` | The three nodes, each in its own container and with its own data volume. |
| `api` | A minimal ASP.NET Core application with one page. |

## Run it

The example needs Docker and the .NET 10 SDK. From the root of the repository:

```bash
dotnet run --project examples/ClusterExample/ClusterExample.AppHost --launch-profile http
```

The output shows the login URL of the Aspire dashboard. When all resources are healthy, open the
**Notes** URL of the `api` resource.

## What the page shows

- **Nodes:** the page asks each node, by itself, for its role, its list of members and the number of
  notes. All nodes show the same number. Every node accepts every request and forwards it to the node
  that owns the data, so this number shows that each node can serve. It does not show the local copy
  of the node.
- **Notes:** the form adds a note through the connection string of the cluster. The client sends each
  request to the next node in turn.

## Stop a node

1. In the dashboard, stop `kahuna-3`.
2. Reload the page. `kahuna-3` shows as down, and the other two nodes still answer.
3. Add a note. The write succeeds, because two of three nodes are a majority.
4. Start `kahuna-3` again, and reload the page. `kahuna-3` catches up and shows all the notes.

While `kahuna-3` is down, the `kahuna` resource stays healthy, and its health description says
"2 of 3 nodes are healthy". A write or a page can take 2 seconds more than usual, because of how the
client and the Aspire proxy behave:

- The client sends each request to the next node in turn, also when that node is down, and it does not
  send a failed request again.
- The Aspire proxy of a stopped container accepts the connection and does not answer.

Thus the API gives each try a limit of 2 seconds and tries again, and the next try goes to another node.
All tries of one note use the same key, so a retry cannot add a second note. See `RetryAsync` in
`ClusterExample.Api/Program.cs`.

## Stop a second node

One node of three is not a majority, so the cluster cannot read or write:

- The `kahuna` resource becomes unhealthy, and its health description says "1 of 3 nodes are healthy".
- The page shows the running node as up, but it cannot read the notes.
- A new note fails with status 500.
- The `api` resource stays healthy. Its health check asks a node for its membership, and a running node
  answers without a majority.

## Clean data

The nodes keep their data in volumes, so the notes stay when you run the example again. To start with
no data, stop the AppHost and remove the three volumes:

```bash
docker volume ls --filter name=kahuna --format '{{.Name}}'
docker volume rm <the three volume names>
```

## Test it

`tests/Kahuna.Aspire.Cluster.Tests` runs the same steps on this example with real containers. It uses a
temporary directory for the data, not the volumes of the example.

```bash
dotnet test tests/Kahuna.Aspire.Cluster.Tests
```
