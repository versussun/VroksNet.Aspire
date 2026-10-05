var builder = DistributedApplication.CreateBuilder(args);

var rabbitmq = builder.AddRabbitMQ("messaging");

var vroks = builder.AddVroksNet("vroks", port: 7400, providerPort: 7401)
    .WithSpecifications("./mocks/specs")         // imported at startup
    .WithProvisioning("./mocks/vroksnet.yaml")   // provider mode, a Publisher, a startup smoke suite
    .WithConnection("notifications", rabbitmq)   // the manifest's Publisher sends through it
    .WithExportCommand("./exported");            // dashboard: "Export configuration" → ./exported (git-ignored)

// A service under development would consume the mock like this:
// builder.AddProject<Projects.MyService>("myservice")
//     .WithEnvironment("Bookstore__BaseUrl", vroks.GetEndpoint("provider"))  // mocks at real paths
//     .WaitFor(vroks);                                                         // until provisioning is applied

builder.Build().Run();
