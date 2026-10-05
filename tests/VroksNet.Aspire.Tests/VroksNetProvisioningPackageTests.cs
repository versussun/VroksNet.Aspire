using System.IO.Compression;
using System.Text;
using Aspire.Hosting;

namespace VroksNet.Aspire.Tests;

public sealed class VroksNetProvisioningPackageTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("vroksnet-export-tests-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task ExtractAsync_WritesSpecsAndManifest()
    {
        using var zip = Zip(("specs/Bookstore.yaml", "openapi: 3.0.0"), ("specs/nested/Orders.json", "{}"), ("vroksnet.yaml", "version: 1"));

        var count = await VroksNetProvisioningPackage.ExtractAsync(zip, _directory, CancellationToken.None);

        Assert.Equal(2, count);
        Assert.Equal("openapi: 3.0.0", File.ReadAllText(Path.Combine(_directory, "specs", "Bookstore.yaml")));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(_directory, "specs", "nested", "Orders.json")));
        Assert.Equal("version: 1", File.ReadAllText(Path.Combine(_directory, "vroksnet.yaml")));
    }

    [Fact]
    public async Task ExtractAsync_ReplacesSpecsButLeavesOtherFilesAlone()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "specs"));
        File.WriteAllText(Path.Combine(_directory, "specs", "deleted-in-ui.yaml"), "old");
        File.WriteAllText(Path.Combine(_directory, "README.md"), "mine");
        using var zip = Zip(("specs/Bookstore.yaml", "new"), ("vroksnet.yaml", "version: 1"));

        await VroksNetProvisioningPackage.ExtractAsync(zip, _directory, CancellationToken.None);

        Assert.Equal(["Bookstore.yaml"], Directory.GetFiles(Path.Combine(_directory, "specs")).Select(Path.GetFileName));
        Assert.Equal("mine", File.ReadAllText(Path.Combine(_directory, "README.md")));
    }

    [Theory]
    [InlineData("../escape.yaml")]
    [InlineData("specs/../../escape.yaml")]
    [InlineData("other/file.yaml")]
    [InlineData("specs/")]
    public async Task ExtractAsync_RejectsUnexpectedPathsBeforeTouchingTheDirectory(string path)
    {
        Directory.CreateDirectory(Path.Combine(_directory, "specs"));
        File.WriteAllText(Path.Combine(_directory, "specs", "keep.yaml"), "keep");
        using var zip = Zip((path + (path.EndsWith('/') ? "x/../y" : string.Empty), "x"), ("vroksnet.yaml", "version: 1"));

        await Assert.ThrowsAsync<InvalidDataException>(() => VroksNetProvisioningPackage.ExtractAsync(zip, _directory, CancellationToken.None));

        Assert.True(File.Exists(Path.Combine(_directory, "specs", "keep.yaml")));
    }

    [Fact]
    public async Task ExtractAsync_RejectsPackageWithoutManifest()
    {
        using var zip = Zip(("specs/Bookstore.yaml", "openapi: 3.0.0"));

        await Assert.ThrowsAsync<InvalidDataException>(() => VroksNetProvisioningPackage.ExtractAsync(zip, _directory, CancellationToken.None));
    }

    private static MemoryStream Zip(params (string Path, string Content)[] entries)
    {
        var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                using var stream = archive.CreateEntry(path).Open();
                stream.Write(Encoding.UTF8.GetBytes(content));
            }
        }

        buffer.Position = 0;
        return buffer;
    }
}
