using System.Collections.Generic;
using System.IO;

namespace UnityCli.Lsp.Core;

public static class LspWorkspaceUtilities
{
    public static IEnumerable<string> EnumerateUnityCsFiles(string rootDir, string scope = "all")
    {
        IEnumerable<string> Enumerate(string dir)
        {
            if (!Directory.Exists(dir))
            {
                yield break;
            }

            foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                var normalized = file.Replace('\\', '/');
                if (normalized.Contains("/obj/") || normalized.Contains("/bin/"))
                {
                    continue;
                }

                yield return file;
            }
        }

        scope = scope.ToLowerInvariant();
        var all = scope is not ("assets" or "packages" or "embedded" or "library");
        foreach (var file in (all || scope == "assets"
            ? Enumerate(Path.Combine(rootDir, "Assets")) : Enumerable.Empty<string>()))
        {
            yield return file;
        }

        foreach (var file in (all || scope is "packages" or "embedded"
            ? Enumerate(Path.Combine(rootDir, "Packages")) : Enumerable.Empty<string>()))
        {
            yield return file;
        }

        foreach (var file in (all || scope is "packages" or "library"
            ? Enumerate(Path.Combine(rootDir, "Library", "PackageCache")) : Enumerable.Empty<string>()))
        {
            yield return file;
        }
    }
}
