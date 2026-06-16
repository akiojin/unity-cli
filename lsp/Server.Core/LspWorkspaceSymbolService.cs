using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis;

namespace UnityCli.Lsp.Core;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed class LspWorkspaceSymbolService
{
    private readonly string _rootDir;
    private readonly LspFileLockProvider _fileLocks;
    private readonly LspSymbolCollector _symbolCollector = new();

    public LspWorkspaceSymbolService(string rootDir, LspFileLockProvider fileLocks)
    {
        _rootDir = rootDir;
        _fileLocks = fileLocks;
    }

    public async Task<object> DocumentSymbolsAsync(string path)
    {
        try
        {
            using var handle = await _fileLocks.AcquireAsync(path);
            var text = await File.ReadAllTextAsync(path);
            var tree = CSharpSyntaxTree.ParseText(text);
            var root = await tree.GetRootAsync();
            var relative = LspPathUtilities.ToRel(path, _rootDir);
            return _symbolCollector.CollectEntries(root, relative).Select(LspCodeIndexModels.MakeSym).ToArray();
        }
        catch
        {
            return Array.Empty<object>();
        }
    }

    public async Task<object> WorkspaceSymbolAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<object>();
        }

        var results = new List<object>();
        foreach (var file in LspWorkspaceUtilities.EnumerateUnityCsFiles(_rootDir))
        {
            try
            {
                using var handle = await _fileLocks.AcquireAsync(file);
                var text = await File.ReadAllTextAsync(file);
                var tree = CSharpSyntaxTree.ParseText(text);
                var root = await tree.GetRootAsync();
                foreach (var entry in _symbolCollector.CollectEntries(root, LspPathUtilities.ToRel(file, _rootDir)))
                {
                    if (LspCodeIndexModels.SymbolKindCode(entry.Kind) == 0 || string.IsNullOrEmpty(entry.Name))
                    {
                        continue;
                    }

                    if (entry.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    var start = new { line = Math.Max(entry.Line - 1, 0), character = Math.Max(entry.Column - 1, 0) };
                    var end = start;
                    results.Add(new
                    {
                        name = entry.Name,
                        kind = LspCodeIndexModels.SymbolKindCode(entry.Kind),
                        kindName = entry.Kind,
                        namePath = LspPathUtilities.NormalizeNamePath(entry.NamePath),
                        containerName = LspPathUtilities.ContainerName(entry.NamePath),
                        location = new { uri = LspPathUtilities.Path2Uri(file), range = new { start, end } }
                    });
                }
            }
            catch
            {
            }
        }

        return results;
    }

    public async Task<object> ReferencesByNameAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Array.Empty<object>();
        }

        var list = new List<object>();
        foreach (var file in LspWorkspaceUtilities.EnumerateUnityCsFiles(_rootDir))
        {
            try
            {
                using var handle = await _fileLocks.AcquireAsync(file);
                var text = await File.ReadAllTextAsync(file);
                var tree = CSharpSyntaxTree.ParseText(text);
                var root = await tree.GetRootAsync();
                foreach (var id in root.DescendantTokens().Where(token => token.IsKind(SyntaxKind.IdentifierToken) && token.ValueText == name))
                {
                    var span = id.GetLocation().GetLineSpan();
                    var lineIdx = span.StartLinePosition.Line;
                    var col = span.StartLinePosition.Character + 1;
                    var snippet = GetLine(text, lineIdx).Trim();
                    list.Add(new { path = LspPathUtilities.ToRel(file, _rootDir), line = lineIdx + 1, column = col, snippet });
                }
            }
            catch
            {
            }
        }

        return list;
    }

    private static string GetLine(string text, int zeroBasedLine)
    {
        var sr = new StringReader(text);
        string? line;
        var i = 0;
        while ((line = sr.ReadLine()) != null)
        {
            if (i++ == zeroBasedLine)
            {
                return line;
            }
        }

        return string.Empty;
    }
}
