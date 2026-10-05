namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// A VroksNet container: one process serving the mock API, the management REST API and the
/// Blazor WebAssembly Admin UI on a single HTTP endpoint.
/// </summary>
/// <param name="name">The name of the resource.</param>
public sealed class VroksNetResource(string name) : ContainerResource(name), IResourceWithServiceDiscovery
{
    internal const string HttpEndpointName = "http";

    /// <summary>The port ASP.NET Core listens on inside the image (<c>ASPNETCORE_HTTP_PORTS</c> default of the aspnet base image).</summary>
    internal const int ContainerHttpPort = 8080;

    /// <summary>Directory declared as <c>VOLUME</c> in the image; holds the SQLite database file.</summary>
    internal const string DataDirectory = "/app/data";

    /// <summary>Configuration key VroksNet reads its SQLite connection string from.</summary>
    internal const string DatabaseConnectionStringVariable = "ConnectionStrings__VroksNetDb";

    /// <summary>
    /// Connection names VroksNet's ApiService hardcodes for its Aspire broker clients
    /// (<c>AddRabbitMQClient("rabbitmq")</c>, <c>AddNatsClient("nats")</c>) — independent of what
    /// the broker resource is called in the consuming AppHost.
    /// </summary>
    internal const string RabbitMQConnectionStringVariable = "ConnectionStrings__rabbitmq";

    internal const string NatsConnectionStringVariable = "ConnectionStrings__nats";

    private EndpointReference? _primaryEndpoint;

    /// <summary>The HTTP endpoint serving the mock API, the management API and the Admin UI.</summary>
    public EndpointReference PrimaryEndpoint => _primaryEndpoint ??= new(this, HttpEndpointName);
}
