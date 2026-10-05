using System.Security.Cryptography;
using System.Text;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Aspire.Hosting;

// Provisioning: what VroksNet loads at startup from /app/provisioning (image contract §4), and
// the export that writes the same layout back to the host.
public static partial class VroksNetBuilderExtensions
{
    private static readonly string[] SpecExtensions = [".yaml", ".yml", ".json"];

    /// <summary>
    /// Imports every spec in a host directory at startup (<c>*.yaml</c>, <c>*.yml</c>, <c>*.json</c>,
    /// nested directories included; OpenAPI, Swagger and AsyncAPI are told apart by content).
    /// Can be called several times.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="directory">The host directory. Relative paths resolve against the AppHost directory.</param>
    /// <returns>A reference to the <see cref="IResourceBuilder{T}"/>.</returns>
    /// <exception cref="DirectoryNotFoundException">The directory doesn't exist.</exception>
    public static IResourceBuilder<VroksNetResource> WithSpecifications(this IResourceBuilder<VroksNetResource> builder, string directory)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var source = builder.ResolveHostPath(directory);
        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException($"VroksNet specifications directory '{source}' doesn't exist.");
        }

        return builder.WithBindMount(source, builder.NextSpecificationTarget(Path.GetFileName(source)), isReadOnly: true);
    }

    /// <summary>Imports one spec file at startup. Can be called several times.</summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="file">The spec file. Relative paths resolve against the AppHost directory.</param>
    /// <returns>A reference to the <see cref="IResourceBuilder{T}"/>.</returns>
    /// <exception cref="FileNotFoundException">The file doesn't exist.</exception>
    public static IResourceBuilder<VroksNetResource> WithSpecification(this IResourceBuilder<VroksNetResource> builder, string file)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(file);

        var source = builder.ResolveHostPath(file);
        if (!File.Exists(source))
        {
            throw new FileNotFoundException($"VroksNet specification file '{source}' doesn't exist.", source);
        }

        return builder.WithBindMount(source, builder.NextSpecificationTarget(Path.GetFileName(source)), isReadOnly: true);
    }

    /// <summary>
    /// Imports a spec downloaded from a URL — e.g. a third-party API's published OpenAPI document.
    /// It's downloaded by the AppHost each time VroksNet starts; if the download fails, VroksNet
    /// doesn't start. Can be called several times.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="url">An absolute <c>http</c> or <c>https</c> URL.</param>
    /// <param name="fileName">
    /// The file name inside <c>specs/</c>. Defaults to the URL's last segment, or <c>specification.yaml</c>
    /// when that has no <c>.yaml</c>/<c>.yml</c>/<c>.json</c> extension.
    /// </param>
    /// <returns>A reference to the <see cref="IResourceBuilder{T}"/>.</returns>
    public static IResourceBuilder<VroksNetResource> WithSpecificationFromUrl(
        this IResourceBuilder<VroksNetResource> builder,
        string url,
        string? fileName = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException($"'{url}' isn't an absolute http(s) URL.", nameof(url));
        }

        var name = SpecFileName(fileName ?? Path.GetFileName(uri.AbsolutePath), "specification");

        return builder.WithDownloadedSpecification(name, (_, _) => Task.FromResult(uri));
    }

    /// <summary>
    /// Imports the spec a resource serves — typically an ASP.NET Core project's OpenAPI document —
    /// so VroksNet can mock it or contract-test the service against it. VroksNet waits for the
    /// resource to be healthy, then the AppHost downloads the document; so the resource must not
    /// itself wait for VroksNet.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="endpoint">The endpoint serving the document, e.g. <c>orders.GetEndpoint("http")</c>.</param>
    /// <param name="path">The document's path on that endpoint.</param>
    /// <param name="fileName">The file name inside <c>specs/</c>. Defaults to <c>&lt;resource name&gt;.json</c>.</param>
    /// <returns>A reference to the <see cref="IResourceBuilder{T}"/>.</returns>
    public static IResourceBuilder<VroksNetResource> WithSpecificationFrom(
        this IResourceBuilder<VroksNetResource> builder,
        EndpointReference endpoint,
        string path = "/openapi/v1.json",
        string? fileName = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var source = endpoint.Resource;
        var name = SpecFileName(fileName ?? source.Name + (Path.GetExtension(path) is { Length: > 0 } extension ? extension : ".json"), source.Name);
        // The AppHost downloads it, so resolve the endpoint as the host sees it.
        var hostEndpoint = new EndpointReference(source, endpoint.EndpointName, KnownNetworkIdentifiers.LocalhostNetwork);

        return builder
            .WaitFor(builder.ApplicationBuilder.CreateResourceBuilder(source))
            .WithDownloadedSpecification(name, async (services, cancellationToken) =>
            {
                // Explicit as well as WaitFor: nothing guarantees which BeforeResourceStarted handler runs first.
                await services.GetRequiredService<ResourceNotificationService>().WaitForResourceHealthyAsync(source.Name, cancellationToken);
                var baseUrl = await hostEndpoint.GetValueAsync(cancellationToken);
                return new Uri(new Uri(baseUrl!.TrimEnd('/') + "/"), path.TrimStart('/'));
            });
    }

    /// <summary>
    /// Applies a provisioning manifest (<c>vroksnet.yaml</c>) at startup: connections, spec
    /// settings, Publishers, Test Scenarios and Test Suites, all referenced by name. Provisioned
    /// objects are brought back in line with the file on every start.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="manifestFile">The manifest. Relative paths resolve against the AppHost directory.</param>
    /// <returns>A reference to the <see cref="IResourceBuilder{T}"/>.</returns>
    /// <exception cref="FileNotFoundException">The file doesn't exist.</exception>
    /// <exception cref="InvalidOperationException">A manifest has already been set.</exception>
    /// <remarks>Schema: https://github.com/versussun/VroksNet/blob/master/docs/schemas/provisioning-manifest.v1.schema.json</remarks>
    public static IResourceBuilder<VroksNetResource> WithProvisioning(this IResourceBuilder<VroksNetResource> builder, string manifestFile)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestFile);

        var source = builder.ResolveHostPath(manifestFile);
        if (!File.Exists(source))
        {
            throw new FileNotFoundException($"VroksNet provisioning manifest '{source}' doesn't exist.", source);
        }

        builder.EnsureNoProvisioningDirectory();
        if (builder.Resource.Annotations.OfType<ContainerMountAnnotation>().Any(m => m.Target == VroksNetResource.ManifestPath))
        {
            throw new InvalidOperationException($"'{builder.Resource.Name}' already has a provisioning manifest; VroksNet reads only one.");
        }

        return builder.WithBindMount(source, VroksNetResource.ManifestPath, isReadOnly: true);
    }

    /// <summary>
    /// Provisions VroksNet from a whole directory laid out as <c>specs/</c> plus an optional
    /// <c>vroksnet.yaml</c> — exactly what VroksNet's export (and <see cref="WithExportCommand"/>)
    /// produces. The directory is created when missing, so a first run can start empty and be
    /// filled by an export. Can't be combined with the other specification and manifest methods.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="directory">The host directory. Relative paths resolve against the AppHost directory.</param>
    /// <returns>A reference to the <see cref="IResourceBuilder{T}"/>.</returns>
    /// <remarks>
    /// An exported manifest declares each connection with <c>valueFrom: ConnectionStrings:&lt;name&gt;</c>;
    /// supply those with <see cref="WithConnectionString(IResourceBuilder{VroksNetResource}, string, IResourceBuilder{IResourceWithConnectionString})"/>.
    /// </remarks>
    public static IResourceBuilder<VroksNetResource> WithProvisioningDirectory(this IResourceBuilder<VroksNetResource> builder, string directory)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var source = builder.ResolveHostPath(directory);
        if (builder.Resource.Annotations.OfType<ContainerMountAnnotation>().Any(m => IsUnderProvisioning(m.Target)))
        {
            throw new InvalidOperationException(
                $"'{builder.Resource.Name}' already mounts specifications or a manifest; a provisioning directory replaces them all. Put them in '{source}' instead.");
        }

        Directory.CreateDirectory(source);

        return builder.WithBindMount(source, VroksNetResource.ProvisioningDirectory, isReadOnly: true);
    }

    /// <summary>
    /// Adds an <b>Export configuration</b> command to the dashboard: it downloads VroksNet's current
    /// configuration (<c>GET /api/provisioning/export</c>) and writes it into a host directory as
    /// <c>specs/</c> and <c>vroksnet.yaml</c>, replacing what was there. Configure in the Admin UI,
    /// export, commit, and provision the next run from it with <see cref="WithProvisioningDirectory"/>.
    /// </summary>
    /// <param name="builder">The resource builder.</param>
    /// <param name="directory">The host directory. Relative paths resolve against the AppHost directory.</param>
    /// <param name="includeConnectionValues">
    /// Write connection values into the manifest. By default each one becomes
    /// <c>valueFrom: ConnectionStrings:&lt;name&gt;</c> instead, because values usually hold credentials.
    /// </param>
    /// <returns>A reference to the <see cref="IResourceBuilder{T}"/>.</returns>
    public static IResourceBuilder<VroksNetResource> WithExportCommand(
        this IResourceBuilder<VroksNetResource> builder,
        string directory,
        bool includeConnectionValues = false)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var target = builder.ResolveHostPath(directory);
        var endpoint = builder.Resource.PrimaryEndpoint;

        return builder.WithCommand(
            "vroksnet-export",
            "Export configuration",
            async context =>
            {
                try
                {
                    using var http = new HttpClient { BaseAddress = new Uri(endpoint.Url), Timeout = TimeSpan.FromMinutes(1) };
                    using var response = await http.GetAsync($"/api/provisioning/export?inlineValues={(includeConnectionValues ? "true" : "false")}", context.CancellationToken);
                    if (!response.IsSuccessStatusCode)
                    {
                        return CommandResults.Failure($"VroksNet answered {(int)response.StatusCode} to the export.");
                    }

                    await using var zip = await response.Content.ReadAsStreamAsync(context.CancellationToken);
                    var specs = await VroksNetProvisioningPackage.ExtractAsync(zip, target, context.CancellationToken);
                    return CommandResults.Success($"Exported {specs} specification(s) and vroksnet.yaml to {target}.");
                }
                catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or IOException or UnauthorizedAccessException)
                {
                    return CommandResults.Failure(ex.Message);
                }
            },
            new CommandOptions
            {
                Description = $"Writes the current configuration to {target}",
                ConfirmationMessage = $"Replace specs/ and vroksnet.yaml in {target} with VroksNet's current configuration?",
                IconName = "ArrowDownload",
                UpdateState = context => context.ResourceSnapshot.HealthStatus == HealthStatus.Healthy
                    ? ResourceCommandState.Enabled
                    : ResourceCommandState.Disabled,
            });
    }

    /// <summary>
    /// Mounts a spec downloaded before each start. Run mode only: the file lives in the AppHost's
    /// temp directory, which means nothing to a published deployment.
    /// </summary>
    private static IResourceBuilder<VroksNetResource> WithDownloadedSpecification(
        this IResourceBuilder<VroksNetResource> builder,
        string fileName,
        Func<IServiceProvider, CancellationToken, Task<Uri>> resolveUrl)
    {
        if (!builder.ApplicationBuilder.ExecutionContext.IsRunMode)
        {
            return builder;
        }

        var target = builder.NextSpecificationTarget(fileName);
        var local = Path.Combine(builder.DownloadDirectory(), Path.GetFileName(target));

        return builder
            .WithBindMount(local, target, isReadOnly: true)
            .OnBeforeResourceStarted(async (_, @event, cancellationToken) =>
            {
                var url = await resolveUrl(@event.Services, cancellationToken);
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                byte[] content;
                try
                {
                    content = await http.GetByteArrayAsync(url, cancellationToken);
                }
                catch (HttpRequestException ex)
                {
                    throw new InvalidOperationException($"Couldn't download the VroksNet specification from {url}: {ex.Message}", ex);
                }

                Directory.CreateDirectory(Path.GetDirectoryName(local)!);
                await File.WriteAllBytesAsync(local, content, cancellationToken);
            });
    }

    /// <summary>
    /// A per-AppHost, per-resource directory under the system temp directory — stable across runs,
    /// so downloads overwrite the previous run's instead of piling up.
    /// </summary>
    private static string DownloadDirectory(this IResourceBuilder<VroksNetResource> builder)
    {
        var key = $"{builder.ApplicationBuilder.AppHostDirectory}|{builder.Resource.Name}";
        var id = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16];
        return Path.Combine(Path.GetTempPath(), "vroksnet-aspire", id, "specs");
    }

    /// <summary>
    /// The mount target for a spec file or directory under <c>specs/</c>, by its own name — each in
    /// its own place, so several calls don't shadow each other — with a numeric suffix on a clash.
    /// </summary>
    private static string NextSpecificationTarget(this IResourceBuilder<VroksNetResource> builder, string fileName)
    {
        builder.EnsureNoProvisioningDirectory();

        var taken = builder.Resource.Annotations.OfType<ContainerMountAnnotation>()
            .Select(m => m.Target)
            .ToHashSet(StringComparer.Ordinal);

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var target = $"{VroksNetResource.SpecificationsDirectory}/{fileName}";
        for (var i = 2; taken.Contains(target); i++)
        {
            target = $"{VroksNetResource.SpecificationsDirectory}/{stem}-{i}{extension}";
        }

        return target;
    }

    /// <summary>A safe file name with a spec extension VroksNet picks up (YAML reads JSON too).</summary>
    private static string SpecFileName(string candidate, string fallbackStem)
    {
        var name = string.Concat(candidate.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? c : '_')).Trim('.');
        if (name.Length == 0)
        {
            name = fallbackStem;
        }

        return SpecExtensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase) ? name : name + ".yaml";
    }

    private static void EnsureNoProvisioningDirectory(this IResourceBuilder<VroksNetResource> builder)
    {
        if (builder.Resource.Annotations.OfType<ContainerMountAnnotation>().Any(m => m.Target == VroksNetResource.ProvisioningDirectory))
        {
            throw new InvalidOperationException(
                $"'{builder.Resource.Name}' is provisioned from a directory (WithProvisioningDirectory); put specifications and the manifest there instead.");
        }
    }

    private static bool IsUnderProvisioning(string target) =>
        target == VroksNetResource.ProvisioningDirectory || target.StartsWith(VroksNetResource.ProvisioningDirectory + "/", StringComparison.Ordinal);
}
