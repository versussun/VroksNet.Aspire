namespace Aspire.Hosting;

/// <summary>Default container image coordinates for VroksNet.</summary>
internal static class VroksNetContainerImageTags
{
    /// <remarks>Built from the root <c>Dockerfile</c> of https://github.com/versussun/VroksNet.</remarks>
    public const string Registry = "ghcr.io";

    public const string Image = "versussun/vroksnet";

    /// <remarks>
    /// Pinned, and moved with this package's releases. The last image release (0.1.0) predates
    /// provisioning, so until 0.2.0 is out this is the <c>master</c> build that implements
    /// image contract v1 in full.
    /// </remarks>
    public const string Tag = "sha-ebd503e";
}
