# VroksNet.Aspire

.NET Aspire hosting integration for [VroksNet](https://github.com/versussun/VroksNet) — an OpenAPI / AsyncAPI mock server and contract-testing tool.

It adds the VroksNet container image to your Aspire AppHost and wires up the things VroksNet needs: an HTTP endpoint (mock API + management API + Admin UI on one port), SQLite storage, RabbitMQ / NATS brokers for async mocks, health checks and OpenTelemetry export to the Aspire dashboard.

## Install

```bash
dotnet add package VroksNet.Aspire
```

## Usage

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var rabbitmq = builder.AddRabbitMQ("messaging");
var nats = builder.AddNats("events");

var vroks = builder.AddVroksNet("vroks", port: 7400)
    .WithDataVolume()        // persist specs, mocks and call history
    .WithRabbitMQ(rabbitmq)  // AsyncAPI mocks over RabbitMQ
    .WithNats(nats);         // AsyncAPI mocks over NATS

builder.AddProject<Projects.MyService>("myservice")
    .WithReference(vroks)    // service discovery: http://vroks
    .WaitFor(vroks);

builder.Build().Run();
```

The Admin UI is linked from the dashboard as **Admin UI** on the `vroks` resource.

## API

| Method | What it does |
|---|---|
| `AddVroksNet(name, port?)` | Adds the container (`ghcr.io/versussun/vroksnet:latest`), HTTP endpoint on container port 8080, health check, OTLP export. Storage defaults to in-memory SQLite — fresh on every start. |
| `WithDataVolume(name?)` | Mounts a named volume at `/app/data` and switches VroksNet to a file database there. |
| `WithDataBindMount(source)` | Same, but with a host directory. |
| `WithRabbitMQ(resource)` | Passes the broker's connection string as `ConnectionStrings__rabbitmq` (the name VroksNet expects, whatever your resource is called) and waits for it. |
| `WithNats(resource)` | Same for NATS (`ConnectionStrings__nats`). |

Everything a regular Aspire container supports still applies, for example:

```csharp
vroks.WithImageTag("0.2.0");                    // pin a version
vroks.WithImageRegistry("myregistry.local");    // use a mirror
vroks.WithDockerfile("../../VroksNet");         // build from a local VroksNet checkout
vroks.WithLifetime(ContainerLifetime.Persistent);
```

### Pointing a service at a mock

Service discovery (`WithReference(vroks)`) gives the consumer `services__vroks__http__0`, so an `HttpClient` with base address `http://vroks` reaches VroksNet. If the service reads a plain base-URL setting instead, pass the endpoint directly:

```csharp
builder.AddProject<Projects.MyService>("myservice")
    .WithEnvironment("PaymentsApi__BaseUrl", vroks.GetEndpoint("http"))
    .WaitFor(vroks);
```

## Notes

- The health check probes `/`, not `/health`: VroksNet maps `/health` only in the Development environment, and the image runs as Production.
- Without a broker, VroksNet still starts — only publishing to that broker fails.

## Repository layout

- `src/VroksNet.Aspire` — the package.
- `tests/VroksNet.Aspire.Tests` — application-model tests (no Docker needed).
- `samples/VroksNet.Aspire.Sample.AppHost` — a runnable AppHost using the integration.

## Release

Push a tag `v<version>` (e.g. `v0.1.0`); the CI workflow builds with that version and pushes the package to nuget.org (needs the `NUGET_API_KEY` repository secret).
