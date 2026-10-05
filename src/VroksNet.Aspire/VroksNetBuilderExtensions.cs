using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting;

/// <summary>Extension methods for adding and configuring VroksNet in an Aspire AppHost.</summary>
public static class VroksNetBuilderExtensions
{
    /// <summary>
    /// Adds a VroksNet container to the application model. By default it stores data in an
    /// in-memory SQLite database (fresh on every start) — call <see cref="WithDataVolume"/> or
    /// <see cref="WithDataBindMount"/> to persist it.
    /// </summary>
    /// <param name="builder">The <see cref="IDistributedApplicationBuilder"/>.</param>
    /// <param name="name">The name of the resource. Also the service-discovery name consumers use.</param>
    /// <param name="port">The host port for the HTTP endpoint. A random port is used when omitted.</param>
    /// <returns>A reference to the <see cref="IResourceBuilder{T}"/>.</returns>
    /// <example>
    /// <code lang="csharp">
    /// var vroks = builder.AddVroksNet("vroks", port: 7400)
    ///     .WithDataVolume();
    ///
    /// builder.AddProject&lt;Projects.MyService&gt;("myservice")
    ///     .WithReference(vroks)
    ///     .WaitFor(vroks);
    /// </code>
    /// </example>
    public static IResourceBuilder<VroksNetResource> AddVroksNet(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        int? port = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var resource = new VroksNetResource(name);

        return builder.AddResource(resource)
            .WithImage(VroksNetContainerImageTags.Image, VroksNetContainerImageTags.Tag)
            .WithImageRegistry(VroksNetContainerImageTags.Registry)
            .WithHttpEndpoint(port: port, targetPort: VroksNetResource.ContainerHttpPort, name: VroksNetResource.HttpEndpointName)
            // The image's Dockerfile defaults to a file-backed database on its declared volume;
            // without a mount that file is lost with the container anyway, so start from the
            // explicit in-memory mode instead (empty value → VroksNet falls back to in-memory).
            .WithEnvironment(VroksNetResource.DatabaseConnectionStringVariable, string.Empty)
            // VroksNet's ServiceDefaults only export telemetry when OTEL_EXPORTER_OTLP_ENDPOINT
            // is set — this is what makes its traces/logs/metrics show up in the dashboard.
            .WithOtlpExporter()
            // Not /health: VroksNet maps it only in the Development environment, and the image
            // runs as Production. "/" is always mapped and answers 200 once startup (including
            // database migrations) has finished.
            .WithHttpHealthCheck("/", endpointName: VroksNetResource.HttpEndpointName)
            .WithUrlForEndpoint(VroksNetResource.HttpEndpointName, url => url.DisplayText = "Admin UI");
    }

    /// <summary>
    /// Persists VroksNet's SQLite database in a named Docker volume, so specifications, mocks and
    /// call history survive container restarts.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="name">The volume name. Generated from the application and resource names when omitted.</param>
    /// <returns>A reference to the <see cref="IResourceBuilder{T}"/>.</returns>
    public static IResourceBuilder<VroksNetResource> WithDataVolume(this IResourceBuilder<VroksNetResource> builder, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder
            .WithVolume(name ?? VolumeNameGenerator.Generate(builder, "data"), VroksNetResource.DataDirectory)
            .WithFileDatabase();
    }

    /// <summary>Persists VroksNet's SQLite database in a host directory.</summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="source">The host directory. Relative paths resolve against the AppHost directory.</param>
    /// <returns>A reference to the <see cref="IResourceBuilder{T}"/>.</returns>
    public static IResourceBuilder<VroksNetResource> WithDataBindMount(this IResourceBuilder<VroksNetResource> builder, string source)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        return builder
            .WithBindMount(source, VroksNetResource.DataDirectory)
            .WithFileDatabase();
    }

    /// <summary>
    /// Connects VroksNet to a RabbitMQ broker for publishing AsyncAPI mock messages. Accepts any
    /// resource with a connection string (e.g. <c>builder.AddRabbitMQ("messaging")</c>), whatever it is named.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="rabbitMQ">The RabbitMQ resource.</param>
    /// <returns>A reference to the <see cref="IResourceBuilder{T}"/>.</returns>
    public static IResourceBuilder<VroksNetResource> WithRabbitMQ(
        this IResourceBuilder<VroksNetResource> builder,
        IResourceBuilder<IResourceWithConnectionString> rabbitMQ) =>
        builder.WithBroker(VroksNetResource.RabbitMQConnectionStringVariable, rabbitMQ);

    /// <summary>
    /// Connects VroksNet to a NATS server for publishing AsyncAPI mock messages. Accepts any
    /// resource with a connection string (e.g. <c>builder.AddNats("events")</c>), whatever it is named.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="nats">The NATS resource.</param>
    /// <returns>A reference to the <see cref="IResourceBuilder{T}"/>.</returns>
    public static IResourceBuilder<VroksNetResource> WithNats(
        this IResourceBuilder<VroksNetResource> builder,
        IResourceBuilder<IResourceWithConnectionString> nats) =>
        builder.WithBroker(VroksNetResource.NatsConnectionStringVariable, nats);

    private static IResourceBuilder<VroksNetResource> WithFileDatabase(this IResourceBuilder<VroksNetResource> builder) =>
        // Same path the image's own Dockerfile uses; overrides the in-memory default set in AddVroksNet.
        builder.WithEnvironment(VroksNetResource.DatabaseConnectionStringVariable, $"Data Source={VroksNetResource.DataDirectory}/vroksnet.db");

    private static IResourceBuilder<VroksNetResource> WithBroker(
        this IResourceBuilder<VroksNetResource> builder,
        string connectionStringVariable,
        IResourceBuilder<IResourceWithConnectionString> broker)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(broker);

        // Not WithReference(broker): that would name the variable after the broker resource
        // ("ConnectionStrings__messaging"), while VroksNet always looks for "rabbitmq"/"nats".
        return builder
            .WithEnvironment(connectionStringVariable, broker.Resource.ConnectionStringExpression)
            .WithReferenceRelationship(broker)
            .WaitFor(broker);
    }
}
