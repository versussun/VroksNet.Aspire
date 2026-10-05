using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting;

/// <summary>Extension methods for adding and configuring VroksNet in an Aspire AppHost.</summary>
/// <remarks>
/// The integration configures nothing through VroksNet's REST API: it only mounts files and sets
/// environment variables, as the image contract describes
/// (https://github.com/versussun/VroksNet/blob/master/docs/container-contract.md).
/// </remarks>
public static partial class VroksNetBuilderExtensions
{
    /// <summary>
    /// Adds a VroksNet container to the application model. By default it stores data in an
    /// in-memory SQLite database (fresh on every start) — call <see cref="WithDataVolume"/> or
    /// <see cref="WithDataBindMount"/> to persist it.
    /// </summary>
    /// <param name="builder">The <see cref="IDistributedApplicationBuilder"/>.</param>
    /// <param name="name">The name of the resource. Also the service-discovery name consumers use.</param>
    /// <param name="port">The host port for the <c>http</c> endpoint (API, Admin UI). A random port is used when omitted.</param>
    /// <param name="providerPort">The host port for the <c>provider</c> endpoint (mocks at real paths). A random port is used when omitted.</param>
    /// <param name="tag">The image tag. The version this package was released with is used when omitted.</param>
    /// <returns>A reference to the <see cref="IResourceBuilder{T}"/>.</returns>
    /// <example>
    /// <code lang="csharp">
    /// var mocks = builder.AddVroksNet("mocks")
    ///     .WithSpecifications("./mocks/specs")
    ///     .WithProvisioning("./mocks/vroksnet.yaml");
    ///
    /// builder.AddProject&lt;Projects.Orders&gt;("orders")
    ///     .WithEnvironment("Payments__BaseUrl", mocks.GetEndpoint("provider"))
    ///     .WaitFor(mocks);
    /// </code>
    /// </example>
    public static IResourceBuilder<VroksNetResource> AddVroksNet(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        int? port = null,
        int? providerPort = null,
        string? tag = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var resource = new VroksNetResource(name);

        return builder.AddResource(resource)
            .WithImage(VroksNetContainerImageTags.Image, tag ?? VroksNetContainerImageTags.Tag)
            .WithImageRegistry(VroksNetContainerImageTags.Registry)
            .WithHttpEndpoint(port: port, targetPort: VroksNetResource.ContainerHttpPort, name: VroksNetResource.HttpEndpointName)
            .WithHttpEndpoint(port: providerPort, targetPort: VroksNetResource.ContainerProviderPort, name: VroksNetResource.ProviderEndpointName)
            // The image's Dockerfile defaults to a file-backed database on its declared volume;
            // without a mount that file is lost with the container anyway, so start from the
            // explicit in-memory mode instead (empty value → VroksNet falls back to in-memory).
            .WithEnvironment(VroksNetResource.DatabaseConnectionStringVariable, string.Empty)
            // Shown in the Admin UI's hints, so it must be the address as the user sees it —
            // resolved on the host network, not the container network VroksNet itself is on.
            .WithEnvironment(
                VroksNetResource.ProviderPublicUrlVariable,
                new EndpointReference(resource, VroksNetResource.ProviderEndpointName, KnownNetworkIdentifiers.LocalhostNetwork))
            // VroksNet's ServiceDefaults only export telemetry when OTEL_EXPORTER_OTLP_ENDPOINT
            // is set — this is what makes its traces/logs/metrics show up in the dashboard.
            .WithOtlpExporter()
            // Readiness: Unhealthy until provisioning has been applied, so WaitFor(vroks) releases
            // consumers only once the mocks are loaded.
            .WithHttpHealthCheck(VroksNetResource.HealthPath, endpointName: VroksNetResource.HttpEndpointName)
            .WithUrlForEndpoint(VroksNetResource.HttpEndpointName, url => url.DisplayText = "Admin UI")
            .WithUrlForEndpoint(VroksNetResource.ProviderEndpointName, url => url.DisplayText = "Provider mock");
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
    /// Declares a VroksNet connection to a resource with a connection string — a broker or a
    /// service VroksNet sends to, listens on or tests. The type is inferred for RabbitMQ, NATS,
    /// Kafka, Redis (and Garnet, Valkey) and Azure Service Bus resources; use the overload taking a
    /// <see cref="VroksNetConnectionType"/> for anything else. VroksNet waits for the resource.
    /// Call it once per connection: VroksNet takes any number of them, and one resource may back several.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="name">The connection name the manifest's Publishers and Test Scenarios refer to.</param>
    /// <param name="resource">The resource to connect to.</param>
    /// <returns>A reference to the <see cref="IResourceBuilder{T}"/>.</returns>
    /// <exception cref="ArgumentException">The type can't be inferred from the resource.</exception>
    public static IResourceBuilder<VroksNetResource> WithConnection(
        this IResourceBuilder<VroksNetResource> builder,
        string name,
        IResourceBuilder<IResourceWithConnectionString> resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        return builder.WithConnection(name, resource, InferConnectionType(resource.Resource));
    }

    /// <summary>
    /// Declares a VroksNet connection of the given type to a resource with a connection string.
    /// VroksNet waits for the resource.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="name">The connection name the manifest's Publishers and Test Scenarios refer to.</param>
    /// <param name="resource">The resource to connect to.</param>
    /// <param name="type">The connection type.</param>
    /// <returns>A reference to the <see cref="IResourceBuilder{T}"/>.</returns>
    public static IResourceBuilder<VroksNetResource> WithConnection(
        this IResourceBuilder<VroksNetResource> builder,
        string name,
        IResourceBuilder<IResourceWithConnectionString> resource,
        VroksNetConnectionType type)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(resource);

        // Not WithReference(resource): its variable name can be overridden per resource type
        // (ConnectionStringEnvironmentVariable), and ValueFrom has to name the key exactly.
        var resourceName = resource.Resource.Name;
        var prefix = builder.AddConnection(name, type);

        return builder
            .WithEnvironment($"ConnectionStrings__{resourceName}", resource.Resource.ConnectionStringExpression)
            .WithEnvironment(prefix + "ValueFrom", $"ConnectionStrings:{resourceName}")
            .WithReferenceRelationship(resource)
            .WaitFor(resource);
    }

    /// <summary>
    /// Declares an HTTP connection to another resource's endpoint — the real service a Test
    /// Scenario checks against its spec. VroksNet doesn't wait for it, so the service may itself
    /// wait for VroksNet.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="name">The connection name the manifest's Test Scenarios refer to.</param>
    /// <param name="endpoint">The endpoint, e.g. <c>orders.GetEndpoint("http")</c>.</param>
    /// <returns>A reference to the <see cref="IResourceBuilder{T}"/>.</returns>
    public static IResourceBuilder<VroksNetResource> WithConnection(
        this IResourceBuilder<VroksNetResource> builder,
        string name,
        EndpointReference endpoint)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(endpoint);

        var prefix = builder.AddConnection(name, VroksNetConnectionType.Http);

        // Resolved in VroksNet's own (container network) context, so the address works from inside it.
        return builder
            .WithEnvironment(prefix + "Value", endpoint)
            .WithReferenceRelationship(endpoint.Resource);
    }

    /// <summary>Declares a VroksNet connection with a fixed value, e.g. an external service's URL.</summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="name">The connection name the manifest's Publishers and Test Scenarios refer to.</param>
    /// <param name="type">The connection type.</param>
    /// <param name="value">The connection value as VroksNet expects it for <paramref name="type"/>.</param>
    /// <returns>A reference to the <see cref="IResourceBuilder{T}"/>.</returns>
    public static IResourceBuilder<VroksNetResource> WithConnection(
        this IResourceBuilder<VroksNetResource> builder,
        string name,
        VroksNetConnectionType type,
        string value)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var prefix = builder.AddConnection(name, type);

        return builder.WithEnvironment(prefix + "Value", value);
    }

    /// <summary>
    /// Declares a VroksNet connection whose value is a parameter — for a value that holds
    /// credentials, e.g. an external broker's connection string kept in user secrets.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="name">The connection name the manifest's Publishers and Test Scenarios refer to.</param>
    /// <param name="type">The connection type.</param>
    /// <param name="value">The parameter holding the value, e.g. <c>builder.AddParameter("payments-sb", secret: true)</c>.</param>
    /// <returns>A reference to the <see cref="IResourceBuilder{T}"/>.</returns>
    public static IResourceBuilder<VroksNetResource> WithConnection(
        this IResourceBuilder<VroksNetResource> builder,
        string name,
        VroksNetConnectionType type,
        IResourceBuilder<ParameterResource> value)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(value);

        var prefix = builder.AddConnection(name, type);

        return builder.WithEnvironment(prefix + "Value", value);
    }

    /// <summary>
    /// Supplies <c>ConnectionStrings__&lt;name&gt;</c> from a resource, for a connection the
    /// <em>manifest</em> declares with <c>valueFrom: ConnectionStrings:&lt;name&gt;</c> — which is how
    /// an exported manifest declares every connection. Unlike <c>WithConnection</c>, it declares
    /// no connection of its own. VroksNet waits for the resource.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="name">The key after <c>ConnectionStrings:</c> in the manifest's <c>valueFrom</c>.</param>
    /// <param name="resource">The resource whose connection string to pass.</param>
    /// <returns>A reference to the <see cref="IResourceBuilder{T}"/>.</returns>
    public static IResourceBuilder<VroksNetResource> WithConnectionString(
        this IResourceBuilder<VroksNetResource> builder,
        string name,
        IResourceBuilder<IResourceWithConnectionString> resource)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(resource);

        return builder
            .WithEnvironment(ConnectionStringVariable(name), resource.Resource.ConnectionStringExpression)
            .WithReferenceRelationship(resource)
            .WaitFor(resource);
    }

    /// <summary>
    /// Supplies <c>ConnectionStrings__&lt;name&gt;</c> from a parameter, for a connection the
    /// manifest declares with <c>valueFrom: ConnectionStrings:&lt;name&gt;</c>.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="name">The key after <c>ConnectionStrings:</c> in the manifest's <c>valueFrom</c>.</param>
    /// <param name="value">The parameter holding the value.</param>
    /// <returns>A reference to the <see cref="IResourceBuilder{T}"/>.</returns>
    public static IResourceBuilder<VroksNetResource> WithConnectionString(
        this IResourceBuilder<VroksNetResource> builder,
        string name,
        IResourceBuilder<ParameterResource> value)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(value);

        return builder.WithEnvironment(ConnectionStringVariable(name), value);
    }

    /// <summary>
    /// Supplies <c>ConnectionStrings__&lt;name&gt;</c> with a fixed value, for a connection the
    /// manifest declares with <c>valueFrom: ConnectionStrings:&lt;name&gt;</c> — an export turns
    /// even a plain URL into one.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="name">The key after <c>ConnectionStrings:</c> in the manifest's <c>valueFrom</c>.</param>
    /// <param name="value">The value.</param>
    /// <returns>A reference to the <see cref="IResourceBuilder{T}"/>.</returns>
    public static IResourceBuilder<VroksNetResource> WithConnectionString(
        this IResourceBuilder<VroksNetResource> builder,
        string name,
        string value)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        return builder.WithEnvironment(ConnectionStringVariable(name), value);
    }

    /// <summary>
    /// Allows browser frontends on the given origins to call the provider mode mock
    /// (<c>Provider__CorsOrigins</c>). CORS is off by default.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="origins">Origins such as <c>http://localhost:5173</c>, or <c>*</c> for any.</param>
    /// <returns>A reference to the <see cref="IResourceBuilder{T}"/>.</returns>
    public static IResourceBuilder<VroksNetResource> WithProviderCors(this IResourceBuilder<VroksNetResource> builder, params string[] origins)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(origins);
        if (origins.Length == 0 || origins.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Pass at least one origin, none blank.", nameof(origins));
        }

        return builder.WithEnvironment(VroksNetResource.ProviderCorsOriginsVariable, string.Join(",", origins));
    }

    /// <summary>
    /// Sets whether a provisioning error stops VroksNet (the default, exit code 3) or is logged
    /// while VroksNet keeps running with whatever applied and reports itself Degraded.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="failOnError"><see langword="false"/> to log and continue.</param>
    /// <returns>A reference to the <see cref="IResourceBuilder{T}"/>.</returns>
    public static IResourceBuilder<VroksNetResource> WithProvisioningFailOnError(this IResourceBuilder<VroksNetResource> builder, bool failOnError)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithEnvironment(VroksNetResource.FailOnErrorVariable, failOnError ? "true" : "false");
    }

    private static IResourceBuilder<VroksNetResource> WithFileDatabase(this IResourceBuilder<VroksNetResource> builder) =>
        // Same path the image's own Dockerfile uses; overrides the in-memory default set in AddVroksNet.
        builder.WithEnvironment(VroksNetResource.DatabaseConnectionStringVariable, $"Data Source={VroksNetResource.DataDirectory}/vroksnet.db");

    private static string ResolveHostPath(this IResourceBuilder<VroksNetResource> builder, string path) =>
        Path.GetFullPath(path, builder.ApplicationBuilder.AppHostDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    /// <summary>Registers a connection's name and type; returns the variable prefix for its value.</summary>
    private static string AddConnection(this IResourceBuilder<VroksNetResource> builder, string name, VroksNetConnectionType type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown VroksNet connection type.");
        }

        var existing = builder.Resource.Annotations.OfType<VroksNetConnectionAnnotation>().ToList();
        if (existing.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"'{builder.Resource.Name}' already has a connection named '{name}'.");
        }

        var prefix = $"{VroksNetResource.ConnectionVariablePrefix}{existing.Count}__";
        builder
            .WithAnnotation(new VroksNetConnectionAnnotation(name, type))
            .WithEnvironment(prefix + "Name", name)
            .WithEnvironment(prefix + "Type", type.ToString());

        return prefix;
    }

    /// <summary>
    /// <c>ConnectionStrings__&lt;name&gt;</c>. The name is limited to what VroksNet's export writes
    /// (<c>[A-Za-z0-9_-]</c>), so the variable is valid in every shell.
    /// </summary>
    private static string ConnectionStringVariable(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
        {
            throw new ArgumentException($"Connection string name '{name}' may contain only letters, digits, '_' and '-'.", nameof(name));
        }

        return $"ConnectionStrings__{name}";
    }

    // Matched by type name: this package depends on Aspire.Hosting alone, not on each broker's
    // hosting package.
    private static VroksNetConnectionType InferConnectionType(IResourceWithConnectionString resource) =>
        resource.GetType().Name switch
        {
            "RabbitMQServerResource" => VroksNetConnectionType.RabbitMq,
            "NatsServerResource" => VroksNetConnectionType.Nats,
            "KafkaServerResource" => VroksNetConnectionType.Kafka,
            // Garnet and Valkey speak the Redis protocol and use its connection string format.
            "RedisResource" or "GarnetResource" or "ValkeyResource" => VroksNetConnectionType.Redis,
            "AzureServiceBusResource" => VroksNetConnectionType.ServiceBus,
            var typeName => throw new ArgumentException(
                $"Can't tell the VroksNet connection type of '{resource.Name}' ({typeName}); pass a {nameof(VroksNetConnectionType)}.",
                nameof(resource)),
        };
}
