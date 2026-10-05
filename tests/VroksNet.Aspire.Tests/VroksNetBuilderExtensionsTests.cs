using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace VroksNet.Aspire.Tests;

// Application-model tests only: they inspect what AddVroksNet/With* put on the resource and
// never start a container, so no Docker is needed.
public sealed class VroksNetBuilderExtensionsTests
{
    [Fact]
    public void AddVroksNet_ConfiguresImageFromGhcr()
    {
        var builder = DistributedApplication.CreateBuilder();

        var vroks = builder.AddVroksNet("vroks");

        Assert.True(vroks.Resource.TryGetLastAnnotation<ContainerImageAnnotation>(out var image));
        Assert.Equal("ghcr.io", image.Registry);
        Assert.Equal("versussun/vroksnet", image.Image);
        Assert.Equal("latest", image.Tag);
    }

    [Fact]
    public void AddVroksNet_ExposesHttpEndpointOnContainerPort8080()
    {
        var builder = DistributedApplication.CreateBuilder();

        var vroks = builder.AddVroksNet("vroks", port: 7400);

        var endpoint = Assert.Single(vroks.Resource.Annotations.OfType<EndpointAnnotation>());
        Assert.Equal("http", endpoint.Name);
        Assert.Equal("http", endpoint.UriScheme);
        Assert.Equal(7400, endpoint.Port);
        Assert.Equal(8080, endpoint.TargetPort);
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
    public async Task WithBrokers_UseVroksNetConnectionNamesRegardlessOfResourceNames()
    {
        var builder = DistributedApplication.CreateBuilder();
        var rabbit = builder.AddRabbitMQ("messaging");
        var nats = builder.AddNats("events");

        var vroks = builder.AddVroksNet("vroks").WithRabbitMQ(rabbit).WithNats(nats);

        var env = await GetEnvironmentAsync(vroks.Resource, DistributedApplicationOperation.Publish);
        Assert.Contains("ConnectionStrings__rabbitmq", env.Keys);
        Assert.Contains("ConnectionStrings__nats", env.Keys);
        Assert.DoesNotContain("ConnectionStrings__messaging", env.Keys);
        Assert.DoesNotContain("ConnectionStrings__events", env.Keys);
    }

    [Fact]
    public void WithBrokers_WaitForBrokers()
    {
        var builder = DistributedApplication.CreateBuilder();
        var rabbit = builder.AddRabbitMQ("messaging");
        var nats = builder.AddNats("events");

        var vroks = builder.AddVroksNet("vroks").WithRabbitMQ(rabbit).WithNats(nats);

        var waitedOn = vroks.Resource.Annotations.OfType<WaitAnnotation>().Select(w => w.Resource).ToList();
        Assert.Contains(rabbit.Resource, waitedOn);
        Assert.Contains(nats.Resource, waitedOn);
    }

    [Fact]
    public async Task WithReference_GivesConsumersServiceDiscoveryForVroksNet()
    {
        var builder = DistributedApplication.CreateBuilder();
        var vroks = builder.AddVroksNet("vroks");

        var consumer = builder.AddContainer("consumer", "alpine").WithReference(vroks);

        var env = await GetEnvironmentAsync(consumer.Resource, DistributedApplicationOperation.Publish);
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

    // Publish mode renders endpoint/connection-string references as manifest expressions
    // instead of resolving allocated ports, so it works without running the app.
    private static async Task<Dictionary<string, string?>> GetEnvironmentAsync(
        IResourceWithEnvironment resource,
        DistributedApplicationOperation operation = DistributedApplicationOperation.Run)
    {
#pragma warning disable CS0618 // Still the documented way to inspect env vars in unit tests.
        var values = await resource.GetEnvironmentVariableValuesAsync(operation);
#pragma warning restore CS0618
        return values.ToDictionary(kv => kv.Key, kv => (string?)kv.Value);
    }
}
