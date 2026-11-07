/*
 * This file is part of Kahuna
 *
 * For the full copyright and license information, please view the LICENSE.txt
 * file that was distributed with this source code.
 */

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

// Three Kahuna nodes: kahuna-1, kahuna-2 and kahuna-3. Each node keeps its data in its own volume, so the
// notes stay when you run the example again.
IResourceBuilder<KahunaClusterResource> kahuna = builder.AddKahunaCluster("kahuna", nodes: 3)
    .WithDataVolume();

builder.AddProject<Projects.ClusterExample_Api>("api")
    .WithReference(kahuna)
    .WaitFor(kahuna)
    .WithHttpHealthCheck("/health")
    .WithUrlForEndpoint("http", url => url.DisplayText = "Notes");

builder.Build().Run();
