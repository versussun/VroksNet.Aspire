using Projects;
using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var nats = builder.AddNats("events");

builder.AddVroksNet("vroksnet-aspire-sample-vroksnet")
    .WithConnection("events", nats)
    .WithSpecifications("../specifications")
    .WithDataVolume();

builder.AddProject<Projects.VroksNet_Aspire_Sample_WebApi>("vroksnet-aspire-sample-webapi")
    .WithReference(nats)
    .WaitFor(nats);

builder.Build().Run();
