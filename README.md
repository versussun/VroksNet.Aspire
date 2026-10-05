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

### Container

| Method | What it does |
|---|---|
| `AddVroksNet(name, port?, providerPort?, tag?)` | Adds the container (`ghcr.io/versussun/vroksnet`, pinned to the image this package was tested with) with endpoints `http` (8080: API, Admin UI, `/mock/…`) and `provider` (7353: mocks at real paths), a readiness check on `/health`, OTLP export, and `Provider__PublicUrl`. Storage defaults to in-memory SQLite — fresh on every start. |
| `WithDataVolume(name?)` / `WithDataBindMount(source)` | Persist the SQLite database in `/app/data`. Without them every run starts clean, which is what tests want. |
| `WithProviderCors(origins...)` | `Provider__CorsOrigins`, for browser frontends calling the provider mock. |
| `WithProvisioningFailOnError(bool)` | `false`: a provisioning error is logged and VroksNet keeps running (reported `Degraded`) instead of exiting with code 3. |

### Specifications

Each call adds specs under `/app/provisioning/specs`, each under its own name; OpenAPI, Swagger and AsyncAPI are told apart by content. A spec is replaced by `info.title`, so two files with the same title end up as one spec.

| Method | Source |
|---|---|
| `WithSpecifications(dir)` | every `*.yaml`/`*.yml`/`*.json` in a host directory, nested ones included |
| `WithSpecification(file)` | one host file |
| `WithSpecificationFromUrl(url, fileName?)` | a URL, e.g. a third-party API's published OpenAPI document. The AppHost downloads it before each start; if that fails, VroksNet doesn't start |
| `WithSpecificationFrom(endpoint, path = "/openapi/v1.json", fileName?)` | the document a resource serves, e.g. an ASP.NET Core project's OpenAPI. VroksNet waits for that resource to be healthy, so the resource mustn't wait for VroksNet |

### Connections

VroksNet takes any number of connections; call a method once per connection. One resource may back several connections.

| Method | What it does |
|---|---|
| `WithConnection(name, resource[, type])` | A connection to a resource with a connection string: `Provisioning__Connections__<i>__*` with `ValueFrom=ConnectionStrings:<resource>`. The type is inferred for RabbitMQ, NATS, Kafka, Redis (and Garnet, Valkey) and Azure Service Bus resources; pass a `VroksNetConnectionType` for MQTT, SQS, SNS or anything else. VroksNet waits for the resource. |
| `WithConnection(name, endpoint)` | An HTTP connection to another resource's endpoint (the real service a Test Scenario checks). No wait, so that service may wait for VroksNet. |
| `WithConnection(name, type, value)` | A fixed value, e.g. an external URL. |
| `WithConnection(name, type, parameter)` | A value from a parameter — for credentials kept in user secrets. |
| `WithConnectionString(name, resource \| parameter \| value)` | Only sets `ConnectionStrings__<name>`, for a connection the **manifest** declares with `valueFrom: ConnectionStrings:<name>`. Declares no connection itself. |

Connection names must be unique — across `WithConnection` calls and the manifest: a name declared in both stops VroksNet with exit code 3.

### Provisioning: manifest, directory, export

| Method | What it does |
|---|---|
| `WithProvisioning(manifestFile)` | Read-only mount of `vroksnet.yaml`: connections, spec settings, Publishers, Test Scenarios and Test Suites, referenced by name. |
| `WithProvisioningDirectory(dir)` | Mounts a whole directory as `/app/provisioning`: `specs/` plus an optional `vroksnet.yaml` — the layout the export writes. Created when missing. Replaces the specification and manifest methods above; can't be combined with them. |
| `WithExportCommand(dir, includeConnectionValues = false)` | Adds an **Export configuration** command to the dashboard: downloads VroksNet's current configuration and writes it into `dir`, replacing `specs/` and `vroksnet.yaml` there (other files are left alone). |

Relative paths resolve against the AppHost directory; a missing file or directory fails at AppHost startup.

### Saving the configuration and restoring it

Configure VroksNet in the Admin UI, save it to files in your repository, and start every later run from them:

```csharp
var rabbitmq = builder.AddRabbitMQ("messaging");

var mocks = builder.AddVroksNet("mocks")
    .WithProvisioningDirectory("./mocks")                        // restore: specs/ + vroksnet.yaml
    .WithExportCommand("./mocks")                                // save: dashboard → Export configuration
    .WithConnectionString("notifications", rabbitmq)             // values for the manifest's valueFrom entries
    .WithConnectionString("payments-http", "http://payments:8080");
```

The export never writes connection values: each connection becomes `valueFrom: ConnectionStrings:<name>`, and the manifest's header lists the names to supply — with `WithConnectionString`, not `WithConnection` (which would declare the connection a second time). `includeConnectionValues: true` writes the values instead; use it only for a directory that stays private.

### Other container settings

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
- `samples/VroksNet.Aspire.Sample.AppHost` — a runnable AppHost: two specs, a manifest with provider mode, a RabbitMQ Publisher, a startup smoke suite and the export command.

## Release

Push a tag `v<version>` (e.g. `v0.1.0`); the CI workflow builds with that version and pushes the package to nuget.org (needs the `NUGET_API_KEY` repository secret).
