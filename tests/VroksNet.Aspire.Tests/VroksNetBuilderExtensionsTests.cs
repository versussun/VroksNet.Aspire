using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace VroksNet.Aspire.Tests;

// Application-model tests only: they inspect what AddVroksNet/With* put on the resource and
// never start a container, so no Docker is needed.
public sealed class VroksNetBuilderExtensionsTests : IDisposable
{
    private readonly string _tempDirectory = Directory.CreateTempSubdirectory("vroksnet-aspire-tests-").FullName;

    public void Dispose() => Directory.Delete(_tempDirectory, recursive: true);

    [Fact]
    public void AddVroksNet_ConfiguresPinnedImageFromGhcr()
    {
        var builder = DistributedApplication.CreateBuilder();

        var vroks = builder.AddVroksNet("vroks");

        Assert.True(vroks.Resource.TryGetLastAnnotation<ContainerImageAnnotation>(out var image));
        Assert.Equal("ghcr.io", image.Registry);
        Assert.Equal("versussun/vroksnet", image.Image);
        Assert.NotEqual("latest", image.Tag);
    }

    [Fact]
    public void AddVroksNet_UsesGivenTag()
    {
        var builder = DistributedApplication.CreateBuilder();

        var vroks = builder.AddVroksNet("vroks", tag: "0.2.0");

        Assert.True(vroks.Resource.TryGetLastAnnotation<ContainerImageAnnotation>(out var image));
        Assert.Equal("0.2.0", image.Tag);
    }

    [Fact]
    public void AddVroksNet_ExposesHttpAndProviderEndpoints()
    {
        var builder = DistributedApplication.CreateBuilder();

        var vroks = builder.AddVroksNet("vroks", port: 7400, providerPort: 7401);

        var endpoints = vroks.Resource.Annotations.OfType<EndpointAnnotation>().ToDictionary(e => e.Name);
        Assert.Equal(2, endpoints.Count);
        Assert.Equal("http", endpoints["http"].UriScheme);
        Assert.Equal(7400, endpoints["http"].Port);
        Assert.Equal(8080, endpoints["http"].TargetPort);
        Assert.Equal("http", endpoints["provider"].UriScheme);
        Assert.Equal(7401, endpoints["provider"].Port);
        Assert.Equal(7353, endpoints["provider"].TargetPort);
        Assert.Equal("provider", vroks.Resource.ProviderEndpoint.EndpointName);
    }

    [Fact]
    public void AddVroksNet_RegistersHealthCheck()
    {
        var builder = DistributedApplication.CreateBuilder();

        var vroks = builder.AddVroksNet("vroks");

        Assert.Single(vroks.Resource.Annotations.OfType<HealthCheckAnnotation>());
    }

    [Fact]
    public async Task AddVroksNet_DefaultsToInMemoryDatabase()
    {
        var builder = DistributedApplication.CreateBuilder();

        var vroks = builder.AddVroksNet("vroks");

        var env = await GetEnvironmentAsync(vroks.Resource);
        Assert.Equal(string.Empty, env["ConnectionStrings__VroksNetDb"]);
    }

    [Fact]
    public async Task AddVroksNet_PassesProviderPublicUrl()
    {
        var builder = DistributedApplication.CreateBuilder();

        var vroks = builder.AddVroksNet("vroks");

        var env = await GetEnvironmentAsync(vroks.Resource);
        Assert.Equal("{vroks.bindings.provider.url}", env["Provider__PublicUrl"]);
    }

    [Fact]
    public async Task WithDataVolume_MountsDataDirectoryAndSwitchesToFileDatabase()
    {
        var builder = DistributedApplication.CreateBuilder();

        var vroks = builder.AddVroksNet("vroks").WithDataVolume("vroks-data");

        var mount = Assert.Single(vroks.Resource.Annotations.OfType<ContainerMountAnnotation>());
        Assert.Equal(ContainerMountType.Volume, mount.Type);
        Assert.Equal("vroks-data", mount.Source);
        Assert.Equal("/app/data", mount.Target);

        var env = await GetEnvironmentAsync(vroks.Resource);
        Assert.Equal("Data Source=/app/data/vroksnet.db", env["ConnectionStrings__VroksNetDb"]);
    }

    [Fact]
    public void WithDataVolume_GeneratesVolumeNameWhenOmitted()
    {
        var builder = DistributedApplication.CreateBuilder();

        var vroks = builder.AddVroksNet("vroks").WithDataVolume();

        var mount = Assert.Single(vroks.Resource.Annotations.OfType<ContainerMountAnnotation>());
        Assert.False(string.IsNullOrWhiteSpace(mount.Source));
        Assert.EndsWith("vroks-data", mount.Source);
    }

    [Fact]
    public async Task WithDataBindMount_MountsHostDirectoryAndSwitchesToFileDatabase()
    {
        var builder = DistributedApplication.CreateBuilder();

        var vroks = builder.AddVroksNet("vroks").WithDataBindMount("./vroks-data");

        var mount = Assert.Single(vroks.Resource.Annotations.OfType<ContainerMountAnnotation>());
        Assert.Equal(ContainerMountType.BindMount, mount.Type);
        Assert.Equal("/app/data", mount.Target);

        var env = await GetEnvironmentAsync(vroks.Resource);
        Assert.Equal("Data Source=/app/data/vroksnet.db", env["ConnectionStrings__VroksNetDb"]);
    }

    [Fact]
    public void WithSpecifications_MountsEachDirectoryReadOnlyUnderSpecs()
    {
        var payments = CreateDirectory("payments");
        var orders = CreateDirectory("orders");
        var builder = DistributedApplication.CreateBuilder();

        var vroks = builder.AddVroksNet("vroks").WithSpecifications(payments).WithSpecifications(orders + Path.DirectorySeparatorChar);

        var mounts = vroks.Resource.Annotations.OfType<ContainerMountAnnotation>().ToList();
        Assert.Equal(["/app/provisioning/specs/payments", "/app/provisioning/specs/orders"], mounts.Select(m => m.Target));
        Assert.All(mounts, m => Assert.Equal(ContainerMountType.BindMount, m.Type));
        Assert.All(mounts, m => Assert.True(m.IsReadOnly));
        Assert.Equal(payments, mounts[0].Source);
    }

    [Fact]
    public void WithSpecification_MountsFileUnderSpecsAndSuffixesClashingNames()
    {
        var first = CreateFile("a/bookstore.yaml");
        var second = CreateFile("b/bookstore.yaml");
        var builder = DistributedApplication.CreateBuilder();

        var vroks = builder.AddVroksNet("vroks").WithSpecification(first).WithSpecification(second);

        var targets = vroks.Resource.Annotations.OfType<ContainerMountAnnotation>().Select(m => m.Target);
        Assert.Equal(["/app/provisioning/specs/bookstore.yaml", "/app/provisioning/specs/bookstore-2.yaml"], targets);
    }

    [Fact]
    public void WithSpecifications_RejectsMissingPaths()
    {
        var builder = DistributedApplication.CreateBuilder();
        var vroks = builder.AddVroksNet("vroks");
        var missing = Path.Combine(_tempDirectory, "missing");

        Assert.Throws<DirectoryNotFoundException>(() => vroks.WithSpecifications(missing));
        Assert.Throws<FileNotFoundException>(() => vroks.WithSpecification(missing + ".yaml"));
        Assert.Throws<FileNotFoundException>(() => vroks.WithProvisioning(missing + ".yaml"));
    }

    [Fact]
    public void WithProvisioning_MountsManifestReadOnly()
    {
        var manifest = CreateFile("mocks/vroksnet.yaml");
        var builder = DistributedApplication.CreateBuilder();

        var vroks = builder.AddVroksNet("vroks").WithProvisioning(manifest);

        var mount = Assert.Single(vroks.Resource.Annotations.OfType<ContainerMountAnnotation>());
        Assert.Equal(manifest, mount.Source);
        Assert.Equal("/app/provisioning/vroksnet.yaml", mount.Target);
        Assert.True(mount.IsReadOnly);
    }

    [Fact]
    public void WithProvisioning_RejectsSecondManifest()
    {
        var manifest = CreateFile("vroksnet.yaml");
        var builder = DistributedApplication.CreateBuilder();
        var vroks = builder.AddVroksNet("vroks").WithProvisioning(manifest);

        Assert.Throws<InvalidOperationException>(() => vroks.WithProvisioning(manifest));
    }

    [Fact]
    public async Task WithConnection_DeclaresIndexedConnectionsReadingTheResourceConnectionString()
    {
        var builder = DistributedApplication.CreateBuilder();
        var rabbit = builder.AddRabbitMQ("messaging");
        var nats = builder.AddNats("events");

        var vroks = builder.AddVroksNet("vroks")
            .WithConnection("orders-rabbit", rabbit)
            .WithConnection("telemetry", nats);

        var env = await GetEnvironmentAsync(vroks.Resource);
        Assert.Equal("orders-rabbit", env["Provisioning__Connections__0__Name"]);
        Assert.Equal("RabbitMq", env["Provisioning__Connections__0__Type"]);
        Assert.Equal("ConnectionStrings:messaging", env["Provisioning__Connections__0__ValueFrom"]);
        Assert.Equal("telemetry", env["Provisioning__Connections__1__Name"]);
        Assert.Equal("Nats", env["Provisioning__Connections__1__Type"]);
        Assert.Equal("ConnectionStrings:events", env["Provisioning__Connections__1__ValueFrom"]);
        Assert.Contains("ConnectionStrings__messaging", env.Keys);
        Assert.Contains("ConnectionStrings__events", env.Keys);
    }

    [Fact]
    public void WithConnection_WaitsForTheResource()
    {
        var builder = DistributedApplication.CreateBuilder();
        var rabbit = builder.AddRabbitMQ("messaging");

        var vroks = builder.AddVroksNet("vroks").WithConnection("orders-rabbit", rabbit);

        Assert.Contains(rabbit.Resource, vroks.Resource.Annotations.OfType<WaitAnnotation>().Select(w => w.Resource));
    }

    [Fact]
    public async Task WithConnection_UsesExplicitType()
    {
        var builder = DistributedApplication.CreateBuilder();
        var bus = builder.AddConnectionString("bus");

        var vroks = builder.AddVroksNet("vroks").WithConnection("warehouse", bus, VroksNetConnectionType.ServiceBus);

        var env = await GetEnvironmentAsync(vroks.Resource);
        Assert.Equal("ServiceBus", env["Provisioning__Connections__0__Type"]);
        Assert.Equal("ConnectionStrings:bus", env["Provisioning__Connections__0__ValueFrom"]);
    }

    [Fact]
    public void WithConnection_RejectsResourceWhoseTypeCantBeInferred()
    {
        var builder = DistributedApplication.CreateBuilder();
        var bus = builder.AddConnectionString("bus");
        var vroks = builder.AddVroksNet("vroks");

        Assert.Throws<ArgumentException>(() => vroks.WithConnection("warehouse", bus));
    }

    [Fact]
    public void WithConnection_RejectsDuplicateNames()
    {
        var builder = DistributedApplication.CreateBuilder();
        var vroks = builder.AddVroksNet("vroks").WithConnection("payments", VroksNetConnectionType.Http, "http://payments");

        Assert.Throws<InvalidOperationException>(() => vroks.WithConnection("Payments", VroksNetConnectionType.Http, "http://other"));
    }

    [Fact]
    public async Task WithConnection_FixedValueAndEndpoint()
    {
        var builder = DistributedApplication.CreateBuilder();
        var orders = builder.AddContainer("orders", "alpine").WithHttpEndpoint(targetPort: 8080, name: "http");

        var vroks = builder.AddVroksNet("vroks")
            .WithConnection("payments", VroksNetConnectionType.Http, "https://payments.example.com")
            .WithConnection("orders-http", orders.GetEndpoint("http"));

        var env = await GetEnvironmentAsync(vroks.Resource);
        Assert.Equal("https://payments.example.com", env["Provisioning__Connections__0__Value"]);
        Assert.Equal("Http", env["Provisioning__Connections__1__Type"]);
        Assert.Equal("{orders.bindings.http.url}", env["Provisioning__Connections__1__Value"]);
        Assert.DoesNotContain(orders.Resource, vroks.Resource.Annotations.OfType<WaitAnnotation>().Select(w => w.Resource));
    }

    [Fact]
    public async Task WithProviderCorsAndFailOnError_SetContractVariables()
    {
        var builder = DistributedApplication.CreateBuilder();

        var vroks = builder.AddVroksNet("vroks")
            .WithProviderCors("http://localhost:5173", "http://localhost:4200")
            .WithProvisioningFailOnError(false);

        var env = await GetEnvironmentAsync(vroks.Resource);
        Assert.Equal("http://localhost:5173,http://localhost:4200", env["Provider__CorsOrigins"]);
        Assert.Equal("false", env["Provisioning__FailOnError"]);
    }

    [Fact]
    public async Task WithReference_GivesConsumersServiceDiscoveryForVroksNet()
    {
        var builder = DistributedApplication.CreateBuilder();
        var vroks = builder.AddVroksNet("vroks");

        var consumer = builder.AddContainer("consumer", "alpine").WithReference(vroks);

        var env = await GetEnvironmentAsync(consumer.Resource);
        Assert.Contains("services__vroks__http__0", env.Keys);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void AddVroksNet_RejectsBlankName(string name)
    {
        var builder = DistributedApplication.CreateBuilder();

        Assert.ThrowsAny<ArgumentException>(() => builder.AddVroksNet(name));
    }

    private string CreateDirectory(string relativePath) =>
        Directory.CreateDirectory(Path.Combine(_tempDirectory, relativePath)).FullName;

    private string CreateFile(string relativePath)
    {
        var path = Path.GetFullPath(Path.Combine(_tempDirectory, relativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    // Publish mode renders endpoint/connection-string references as manifest expressions
    // instead of resolving allocated ports, so it works without running the app (in Run mode
    // Provider__PublicUrl would wait forever for the provider endpoint to be allocated).
    private static async Task<Dictionary<string, string?>> GetEnvironmentAsync(IResourceWithEnvironment resource)
    {
#pragma warning disable CS0618 // Still the documented way to inspect env vars in unit tests.
        var values = await resource.GetEnvironmentVariableValuesAsync(DistributedApplicationOperation.Publish);
#pragma warning restore CS0618
        return values.ToDictionary(kv => kv.Key, kv => (string?)kv.Value);
    }
}
