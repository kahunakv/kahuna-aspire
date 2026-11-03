# Kahuna.Aspire.Hosting

Runs a [Kahuna](https://github.com/kahunakv/kahuna) node as a container resource in a .NET Aspire AppHost.

```csharp
var kahuna = builder.AddKahuna("kahuna")
                    .WithDataVolume();

builder.AddProject<Projects.Api>("api")
       .WithReference(kahuna)
       .WaitFor(kahuna);
```

The connection string is the URL of the node's cleartext HTTP/2 gRPC endpoint. Use the
`Kahuna.Aspire.Client` package in the referencing project to register a `KahunaClient` from it.

| Method | Effect |
| --- | --- |
| `AddKahuna(name, grpcPort, httpPort)` | Adds the node, its `grpc` and `http` endpoints, and a health check on `/v1/cluster/health`. |
| `WithDataVolume(name)` | Keeps data and the Raft log in a named volume. |
| `WithDataBindMount(source)` | Keeps data and the Raft log in a host directory. |
| `WithPartitions(count)` | Sets the number of partitions for a node with empty storage. |
