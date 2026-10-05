var builder = DistributedApplication.CreateBuilder(args);

var rabbitmq = builder.AddRabbitMQ("messaging");
var nats = builder.AddNats("events");

var vroks = builder.AddVroksNet("vroks", port: 7400)
    .WithDataVolume()
    .WithRabbitMQ(rabbitmq)
    .WithNats(nats);

// Until the image is published to ghcr.io, build it from a local VroksNet checkout instead:
// vroks.WithDockerfile("../../../VroksNet/VroksNet");

// A service under development would consume the mock like this:
// builder.AddProject<Projects.MyService>("myservice")
//     .WithReference(vroks)                                         // services__vroks__http__0
//     .WithEnvironment("PaymentsApi__BaseUrl", vroks.GetEndpoint("http"))
//     .WaitFor(vroks);

builder.Build().Run();
