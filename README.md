# Kahuna for .NET Aspire

.NET Aspire integrations for [Kahuna](https://github.com/kahunakv/kahuna), a distributed lock manager,
key-value store and sequencer.

| Package | NuGet | Use it in | What it does |
| --- | --- | --- | --- |
| [`Kahuna.Aspire.Hosting`](src/Kahuna.Aspire.Hosting) | [![NuGet](https://img.shields.io/nuget/v/Kahuna.Aspire.Hosting.svg)](https://www.nuget.org/packages/Kahuna.Aspire.Hosting) | The AppHost | Runs a Kahuna node or a cluster of nodes as container resources, with health checks and data volumes. |
| [`Kahuna.Aspire.Client`](src/Kahuna.Aspire.Client) | [![NuGet](https://img.shields.io/nuget/v/Kahuna.Aspire.Client.svg)](https://www.nuget.org/packages/Kahuna.Aspire.Client) | Each service | Registers a `KahunaClient` from the connection string, with a health check, spans and metrics. |

## Install

In the AppHost project:

```bash
dotnet add package Kahuna.Aspire.Hosting
```

In each service project:

```bash
dotnet add package Kahuna.Aspire.Client
```

## Quick start

AppHost:

```csharp
var kahuna = builder.AddKahuna("kahuna").WithDataVolume();

builder.AddProject<Projects.Api>("api")
       .WithReference(kahuna)
       .WaitFor(kahuna);
```

Service:

```csharp
builder.AddKahunaClient("kahuna");
```

## Cluster

Use `AddKahunaCluster` in the AppHost to run several nodes. The service code does not change.

```csharp
var kahuna = builder.AddKahunaCluster("kahuna", nodes: 3).WithDataVolume();
```

The cluster is for local development only. See
[`Kahuna.Aspire.Hosting`](src/Kahuna.Aspire.Hosting#cluster) for its methods, its health rules and its
security limits. [`examples/ClusterExample`](examples/ClusterExample) is a small application to test it.

## What the Aspire dashboard shows

- **Client spans.** One span for each operation that a `KahunaClient` sends to a node (`GetKeyValue`,
  `AcquireLock`, `NextSequenceValue` and so on), under the request that caused it. The span has
  `server.address` and `server.port`, so the dashboard links it to the Kahuna resource.
- **Client metrics.** `kahuna.client.operation.duration` for each operation, and the routing-cache
  counters.
- **Server telemetry.** The node's `Kahuna` and `Kommander` meters, its request spans and its logs.
  The server exports them only when Aspire sets `OTEL_EXPORTER_OTLP_ENDPOINT`.

The client package needs `Kahuna.Client` 1.15.1 or later. The server export needs a server image
built from 1.15.1 or later. The hosting package pins image `kahunakv/kahuna:1.17.3`.

## Sample

`samples/` holds an AppHost with one Kahuna node and two replicas of an API service. The API shows a
key-value store, a job under a distributed lock, a cluster-wide sequence, and a rate limiter that
shares one budget between the replicas.

```bash
dotnet run --project samples/Kahuna.Aspire.AppHost --launch-profile http
```

To run a server image built from a local Kahuna checkout:

```bash
# in the Kahuna checkout
docker build -f docker/Dockerfile.standalone -t kahunakv/kahuna:local .

# here
dotnet run --project samples/Kahuna.Aspire.AppHost --launch-profile http -- --Kahuna:ImageTag=local
```

## Building against a local Kahuna checkout

With a Kahuna checkout at `../kahuna`, the projects reference its `Kahuna.Client` and
`Kahuna.Client.AspNetCore` source instead of the packages, so a change on both sides builds and
tests together. Set `-p:KahunaRepoRoot=<path>` for another location, or `-p:UseLocalKahuna=false`
to use the packages.

```bash
dotnet build Kahuna.Aspire.slnx
dotnet test tests/Kahuna.Aspire.Hosting.Tests
dotnet test tests/Kahuna.Aspire.Client.Tests
```

These two test projects need no Docker and no running node. `tests/Kahuna.Aspire.Cluster.Tests` starts
the cluster example with real containers, so it needs Docker.

## NuGet packages

```bash
scripts/set-version.sh 0.2.0             # set the version in Directory.Build.props, then pack
scripts/set-version.sh 0.2.0 --dry-run   # show what would change, touch nothing
```

The script writes the packages to `nupkg/`. They always depend on the released `Kahuna.Client`
package, also when a local checkout is present. The script does not upload them.

## License

Kahuna.Aspire is released under the MIT License. See [LICENSE](LICENSE).
