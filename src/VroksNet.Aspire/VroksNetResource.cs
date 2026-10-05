namespace Aspire.Hosting.ApplicationModel;

/// <summary>
/// A VroksNet container: the management REST API, the Admin UI and the <c>/mock/…</c> routes on
/// the <c>http</c> endpoint, and provider mode (mocks at the spec's real paths) on the
/// <c>provider</c> endpoint.
/// </summary>
/// <remarks>
/// Everything this integration relies on is the image contract:
/// https://github.com/versussun/VroksNet/blob/master/docs/container-contract.md
/// </remarks>
/// <param name="name">The name of the resource.</param>
public sealed class VroksNetResource(string name) : ContainerResource(name), IResourceWithServiceDiscovery
{
    internal const string HttpEndpointName = "http";

    internal const string ProviderEndpointName = "provider";

    /// <summary>The API and UI port inside the image (contract §3).</summary>
    internal const int ContainerHttpPort = 8080;

    /// <summary>The provider mode port inside the image (contract §3).</summary>
    internal const int ContainerProviderPort = 7353;

    /// <summary>Readiness: Unhealthy until provisioning has been applied (contract §6).</summary>
    internal const string HealthPath = "/health";

    /// <summary>Declared as <c>VOLUME</c> in the image; holds the SQLite database file (contract §4).</summary>
    internal const string DataDirectory = "/app/data";

    /// <summary>The provisioning root (contract §4).</summary>
    internal const string ProvisioningDirectory = "/app/provisioning";

    internal const string SpecificationsDirectory = ProvisioningDirectory + "/specs";

    internal const string ManifestPath = ProvisioningDirectory + "/vroksnet.yaml";

    internal const string DatabaseConnectionStringVariable = "ConnectionStrings__VroksNetDb";

    internal const string ProviderPublicUrlVariable = "Provider__PublicUrl";

    internal const string ProviderCorsOriginsVariable = "Provider__CorsOrigins";

    internal const string FailOnErrorVariable = "Provisioning__FailOnError";

    internal const string ConnectionVariablePrefix = "Provisioning__Connections__";

    private EndpointReference? _primaryEndpoint;
    private EndpointReference? _providerEndpoint;

    /// <summary>The HTTP endpoint serving the management API, the Admin UI and the <c>/mock/…</c> routes.</summary>
    public EndpointReference PrimaryEndpoint => _primaryEndpoint ??= new(this, HttpEndpointName);

    /// <summary>
    /// The provider mode endpoint: mocks served at the spec's real paths. Hand it to a service in
    /// place of the real dependency's base URL.
    /// </summary>
    public EndpointReference ProviderEndpoint => _providerEndpoint ??= new(this, ProviderEndpointName);
}
