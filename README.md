# VroksNet.Aspire

.NET Aspire hosting integration for [VroksNet](https://github.com/versussun/VroksNet) — an OpenAPI / AsyncAPI mock server and contract-testing tool.

It runs VroksNet next to your services in an Aspire AppHost and provisions it at startup: specs, connections to your brokers and services, Publishers, Test Scenarios and Test Suites come from files in your repository, so every run (in dev and in `Aspire.Hosting.Testing`) starts with the mocks ready. `WaitFor(vroks)` releases your services only once provisioning has been applied.

The package configures nothing through VroksNet's REST API. It only mounts files and sets environment variables, as described in the [image contract](https://github.com/versussun/VroksNet/blob/master/docs/container-contract.md) (contract version 1). The design is in [ADR 0001](https://github.com/versussun/VroksNet/blob/master/docs/adr/0001-aspire-integration-and-provisioning.md).

## Install

```bash
dotnet add package VroksNet.Aspire
```

## Usage

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var kafka = builder.AddKafka("kafka");

var mocks = builder.AddVroksNet("mocks")
    .WithSpecifications("./mocks/specs")        // every spec in the directory is imported
    .WithProvisioning("./mocks/vroksnet.yaml")  // provider mode, Publishers, Test Scenarios, …
    .WithConnection("orders-kafka", kafka);     // the manifest refers to it by name

builder.AddProject<Projects.Orders>("orders")
    .WithEnvironment("Payments__BaseUrl", mocks.GetEndpoint("provider"))  // mocks at the spec's real paths
    .WaitFor(mocks);

builder.Build().Run();
```

The manifest references everything by name; its schema is [provisioning-manifest.v1.schema.json](https://github.com/versussun/VroksNet/blob/master/docs/schemas/provisioning-manifest.v1.schema.json). The dashboard links the `http` endpoint as **Admin UI** and the `provider` endpoint as **Provider mock**.

## API

| Method | What it does |
|---|---|
| `AddVroksNet(name, port?, providerPort?, tag?)` | Adds the container (`ghcr.io/versussun/vroksnet`, pinned to the image this package was tested with) with endpoints `http` (8080: API, Admin UI, `/mock/…`) and `provider` (7353: mocks at real paths), a readiness check on `/health`, OTLP export, and `Provider__PublicUrl`. Storage defaults to in-memory SQLite — fresh on every start. |
| `WithSpecifications(dir)` / `WithSpecification(file)` | Read-only bind mount under `/app/provisioning/specs`. Can be called several times; OpenAPI, Swagger and AsyncAPI are told apart by content. |
| `WithProvisioning(manifestFile)` | Read-only bind mount of `vroksnet.yaml`. |
| `WithConnection(name, resource[, type])` | A connection to a resource with a connection string, passed as `Provisioning__Connections__<i>__*` with `ValueFrom=ConnectionStrings:<resource>`. The type is inferred for RabbitMQ, NATS, Kafka and Redis resources; pass a `VroksNetConnectionType` for the others. VroksNet waits for the resource. |
| `WithConnection(name, endpoint)` | An HTTP connection to another resource's endpoint (the real service a Test Scenario checks). No wait, so that service may wait for VroksNet. |
| `WithConnection(name, type, value)` | A connection with a fixed value, e.g. an external URL. |
| `WithProviderCors(origins...)` | `Provider__CorsOrigins`, for browser frontends calling the provider mock. |
| `WithProvisioningFailOnError(bool)` | `false`: a provisioning error is logged and VroksNet keeps running (reported `Degraded`) instead of exiting with code 3. |
| `WithDataVolume(name?)` / `WithDataBindMount(source)` | Persist the SQLite database in `/app/data`. Without them every run starts clean, which is what tests want. |

Relative paths resolve against the AppHost directory; a missing file or directory fails at AppHost startup. Connection names must be unique.

Everything a regular Aspire container supports still applies, for example:

```csharp
mocks.WithImageTag("master");                    // a newer image than the pinned one
mocks.WithImageRegistry("myregistry.local");     // use a mirror
mocks.WithDockerfile("../../VroksNet");          // build from a local VroksNet checkout
mocks.WithLifetime(ContainerLifetime.Persistent);
```

## Notes

- Connection strings are resolved on the container network, so a broker is reached at its container address, not the host's `localhost:<port>`.
- Provisioned objects are brought back in line with the manifest on every start; edits made to them in the Admin UI are lost. Objects created in the UI aren't touched, and provisioning never deletes.
- If provisioning fails, the container exits with code 3. Its log names the file or manifest entry that failed; `GET /api/system/info` on the `http` endpoint shows the provisioning state.

## Repository layout

- `src/VroksNet.Aspire` — the package.
- `tests/VroksNet.Aspire.Tests` — application-model tests (no Docker needed).
- `samples/VroksNet.Aspire.Sample.AppHost` — a runnable AppHost: two specs, a manifest with provider mode, a RabbitMQ Publisher and a startup smoke suite.

## Release

Push a tag `v<version>` (e.g. `v0.1.0`); the CI workflow builds with that version and pushes the package to nuget.org (needs the `NUGET_API_KEY` repository secret).
