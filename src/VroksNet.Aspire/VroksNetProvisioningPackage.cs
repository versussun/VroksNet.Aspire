using System.IO.Compression;

namespace Aspire.Hosting;

/// <summary>
/// Unpacks VroksNet's export (<c>GET /api/provisioning/export</c>: a zip of <c>specs/…</c> and
/// <c>vroksnet.yaml</c>) into a provisioning directory on the host.
/// </summary>
internal static class VroksNetProvisioningPackage
{
    internal const string ManifestFileName = "vroksnet.yaml";

    internal const string SpecsDirectoryName = "specs";

    /// <summary>
    /// Replaces <c>specs/</c> and <c>vroksnet.yaml</c> in <paramref name="directory"/> with the
    /// package's; anything else there is left alone. The whole package is read and checked before
    /// anything is deleted, so a bad package changes nothing.
    /// </summary>
    /// <returns>The number of spec files written.</returns>
    /// <exception cref="InvalidDataException">Not a VroksNet export: an unexpected path, or no manifest.</exception>
    public static async Task<int> ExtractAsync(Stream zip, string directory, CancellationToken cancellationToken)
    {
        var files = new List<(string RelativePath, byte[] Content)>();
        await using (var archive = new ZipArchive(zip, ZipArchiveMode.Read))
        {
            foreach (var entry in archive.Entries)
            {
                var path = entry.FullName.Replace('\\', '/');
                if (path.EndsWith('/'))
                {
                    continue;
                }

                if (!IsExpected(path))
                {
                    throw new InvalidDataException($"Unexpected entry '{entry.FullName}' in the VroksNet export.");
                }

                await using var stream = await entry.OpenAsync(cancellationToken);
                using var content = new MemoryStream();
                await stream.CopyToAsync(content, cancellationToken);
                files.Add((path, content.ToArray()));
            }
        }

        if (!files.Any(file => file.RelativePath == ManifestFileName))
        {
            throw new InvalidDataException($"The VroksNet export has no {ManifestFileName}.");
        }

        var root = Path.GetFullPath(directory);
        Directory.CreateDirectory(root);
        var specs = Path.Combine(root, SpecsDirectoryName);
        if (Directory.Exists(specs))
        {
            Directory.Delete(specs, recursive: true);
        }

        foreach (var (relativePath, content) in files)
        {
            var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, content, cancellationToken);
        }

        return files.Count(file => file.RelativePath != ManifestFileName);
    }

    // Only the export's own layout, and no segment that could climb out of the directory.
    private static bool IsExpected(string path)
    {
        if (path == ManifestFileName)
        {
            return true;
        }

        var segments = path.Split('/');
        return segments.Length >= 2
            && segments[0] == SpecsDirectoryName
            && segments.Skip(1).All(segment => segment is not ("" or "." or "..") && segment.IndexOfAny(Path.GetInvalidFileNameChars()) < 0);
    }
}
