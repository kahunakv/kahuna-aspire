# Kahuna.Aspire.Client

Registers a [Kahuna](https://github.com/kahunakv/kahuna) `KahunaClient` from a .NET Aspire connection string.

```csharp
builder.AddKahunaClient("kahuna");

app.MapPost("/orders/{id}", async (string id, KahunaClient kahuna) =>
{
    await using KahunaLock orderLock = await kahuna.GetOrCreateLock($"order/{id}", TimeSpan.FromSeconds(10));
    // ...
});
```

The call registers:

1. A singleton `KahunaClient` for the endpoints in `ConnectionStrings:kahuna`.
2. A health check that asks a node for its cluster membership.
3. The export of the `Kahuna.Client` spans and the `Kahuna.Client` and `Kahuna.Client.Routing` metrics.

Settings bind from the `Kahuna:Client` configuration section:

```json
{
  "Kahuna": {
    "Client": {
      "DisableHealthChecks": false,
      "HealthCheckTimeout": "00:00:05",
      "DisableTracing": false,
      "DisableMetrics": false
    }
  }
}
```

The connection string can name one node (`http://localhost:8083`), a comma-separated list of nodes,
or use the `Endpoints=` key. For two clusters, call `AddKeyedKahunaClient("name")` once for each.
