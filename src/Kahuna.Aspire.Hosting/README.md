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

## Cluster

`AddKahunaCluster` runs several nodes that replicate each write through Raft and act as one store.

```csharp
var kahuna = builder.AddKahunaCluster("kahuna", nodes: 3)
                    .WithDataVolume();

builder.AddProject<Projects.Api>("api")
       .WithReference(kahuna)
       .WaitFor(kahuna);
```

The nodes are the resources `kahuna-1`, `kahuna-2` and `kahuna-3`, under the `kahuna` resource. The
connection string lists the gRPC endpoint of each node, and `AddKahunaClient("kahuna")` in the service
reads it without a change.

| Method | Effect |
| --- | --- |
| `AddKahunaCluster(name, nodes)` | Adds the cluster and its nodes. Each node has `grpc` and `http` endpoints and a health check on `/v1/cluster/health`. The default is 3 nodes. |
| `WithDataVolume(name)` | Keeps the data of each node in its own named volume, `{name}-{node ID}`. |
| `WithDataBindMount(source)` | Keeps the data of each node in a subdirectory of `source` with the name of the node. |
| `WithPartitions(count)` | Sets the number of partitions for a cluster with empty storage. |
| `ConfigureNodes(configure)` | Applies a configuration to every node, for example `node => node.WithImageTag("local")`. |

Use an odd number of nodes. A cluster commits while a majority of its nodes runs, so 3 nodes continue
after the loss of 1, and 5 nodes continue after the loss of 2.

### Health

The `kahuna` resource is healthy after all nodes are healthy for the first time, so `WaitFor` starts a
service on a complete cluster. After that, it stays healthy while a majority of the nodes is healthy, and
its health description gives the count. Thus a service can restart while one node is down.

### For local development only

The nodes use the development profile of `scripts/run-cluster.sh` in the Kahuna repository:

- The nodes connect to each other over TLS with the development certificate of the image, which is public.
- A node does not validate the certificate of a peer.
- The nodes do not authenticate each other.
- The client endpoints serve cleartext.

The nodes also start with `--disable-routing-hints`. A routing hint names the Raft address of a node, and
a client outside the container network cannot reach that address. Without hints, the client sends each
request to the next node in turn, and that node forwards the request to the owner.
