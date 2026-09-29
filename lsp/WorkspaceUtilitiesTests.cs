using System.IO;
using System.Linq;
using UnityCli.Lsp.Core;
using Xunit;

public sealed class WorkspaceUtilitiesTests
{
    [Theory]
    [InlineData("assets", "Assets/A.cs")]
    [InlineData("embedded", "Packages/B.cs")]
    [InlineData("library", "Library/PackageCache/C.cs")]
    [InlineData("packages", "Library/PackageCache/C.cs,Packages/B.cs")]
    [InlineData("all", "Assets/A.cs,Library/PackageCache/C.cs,Packages/B.cs")]
    [InlineData("unknown", "Assets/A.cs,Library/PackageCache/C.cs,Packages/B.cs")]
    public void EnumerateUnityCsFiles_SelectsRequestedRoots(string scope, string expected)
    {
        var root = Path.Combine(Path.GetTempPath(), "lsp-roots-" + Path.GetRandomFileName());
        try
        {
            foreach (var file in new[] { "Assets/A.cs", "Packages/B.cs", "Library/PackageCache/C.cs" })
            {
                var path = Path.Combine(root, file);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "class Probe {}");
            }
            var files = LspWorkspaceUtilities.EnumerateUnityCsFiles(root, scope)
                .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/')).OrderBy(path => path);
            Assert.Equal(expected, string.Join(",", files));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void EnumerateUnityCsFiles_ScansUnityFoldersAndSkipsBinObj()
    {
        var root = Path.Combine(Path.GetTempPath(), "unity-cli-lsp-workspace-" + Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(root, "Assets", "Scripts"));
        Directory.CreateDirectory(Path.Combine(root, "Packages", "Pkg"));
        Directory.CreateDirectory(Path.Combine(root, "Library", "PackageCache", "Cached"));
        Directory.CreateDirectory(Path.Combine(root, "Assets", "obj"));
        Directory.CreateDirectory(Path.Combine(root, "Assets", "bin"));

        File.WriteAllText(Path.Combine(root, "Assets", "Scripts", "A.cs"), "class A {}");
        File.WriteAllText(Path.Combine(root, "Packages", "Pkg", "B.cs"), "class B {}");
        File.WriteAllText(Path.Combine(root, "Library", "PackageCache", "Cached", "C.cs"), "class C {}");
        File.WriteAllText(Path.Combine(root, "Assets", "obj", "Ignored.cs"), "class Ignored {}");
        File.WriteAllText(Path.Combine(root, "Assets", "bin", "Ignored2.cs"), "class Ignored2 {}");

        try
        {
            var files = LspWorkspaceUtilities.EnumerateUnityCsFiles(root)
                .Select(path => path.Replace('\\', '/'))
                .OrderBy(path => path)
                .ToArray();

            Assert.Equal(3, files.Length);
            Assert.Contains(files, path => path.EndsWith("/Assets/Scripts/A.cs"));
            Assert.Contains(files, path => path.EndsWith("/Packages/Pkg/B.cs"));
            Assert.Contains(files, path => path.EndsWith("/Library/PackageCache/Cached/C.cs"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void EnumerateUnityCsFiles_ReturnsEmptyWhenUnityFoldersAreMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "unity-cli-lsp-empty-" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);

        try
        {
            Assert.Empty(LspWorkspaceUtilities.EnumerateUnityCsFiles(root));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
