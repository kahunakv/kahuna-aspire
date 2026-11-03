/*
 * This file is part of Kahuna
 *
 * For the full copyright and license information, please view the LICENSE.txt
 * file that was distributed with this source code.
 */

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);

IResourceBuilder<KahunaResource> kahuna = builder.AddKahuna("kahuna")
    .WithDataVolume();

// To run a server image built from a local Kahuna checkout, set Kahuna:ImageTag, for example
//   dotnet run -- --Kahuna:ImageTag=local
// after `docker build -f docker/Dockerfile.standalone -t kahunakv/kahuna:local .` in that checkout.
if (builder.Configuration["Kahuna:ImageTag"] is { Length: > 0 } imageTag)
    kahuna.WithImageTag(imageTag);

// Two replicas, so the rate limiter, the job lock and the sequence show that they hold across processes.
builder.AddProject<Projects.Kahuna_Aspire_ApiService>("api")
    .WithReference(kahuna)
    .WaitFor(kahuna)
    .WithReplicas(2);

builder.Build().Run();
