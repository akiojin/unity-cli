using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis.CSharp;

namespace UnityCli.Lsp.Core;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed class LspCodeIndexService
{
    private readonly string _rootDir;
    private readonly LspSymbolCollector _symbolCollector = new();

    public LspCodeIndexService(string rootDir)
    {
        _rootDir = rootDir;
    }

    public async Task<object> BuildAsync(string? outputPath)
    {
        try
        {
            if (string.IsNullOrEmpty(_rootDir) || !Directory.Exists(_rootDir))
            {
                return new { success = false, error = "root_directory_not_initialized" };
            }

            var entries = new List<CodeIndexEntry>();
            foreach (var file in LspWorkspaceUtilities.EnumerateUnityCsFiles(_rootDir))
            {
                try
                {
                    var text = await File.ReadAllTextAsync(file);
                    var tree = CSharpSyntaxTree.ParseText(text);
                    var root = await tree.GetRootAsync();
                    var relative = LspPathUtilities.ToRel(file, _rootDir);
                    _symbolCollector.CollectSymbols(root, new Stack<string>(), entries, relative);
                }
                catch (Exception ex)
                {
                    entries.Add(new CodeIndexEntry
                    {
                        Name = Path.GetFileName(file),
                        Kind = "file_error",
                        NamePath = Path.GetFileName(file),
                        File = LspPathUtilities.ToRel(file, _rootDir),
                        Line = 0,
                        Column = 0,
                        Summary = ex.Message
                    });
                }
            }

            var target = ResolveIndexOutputPath(outputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            var payload = new CodeIndexDocument
            {
                GeneratedAt = DateTime.UtcNow.ToString("o"),
                Root = _rootDir.Replace('\\', '/'),
                Entries = entries.OrderBy(entry => entry.NamePath, StringComparer.OrdinalIgnoreCase).ToArray()
            };

            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };

            await File.WriteAllTextAsync(target, JsonSerializer.Serialize(payload, options), Encoding.UTF8);

            return new
            {
                success = true,
                count = entries.Count,
                outputPath = target.Replace('\\', '/')
            };
        }
        catch (Exception ex)
        {
            return new { success = false, error = ex.Message };
        }
    }

    private string ResolveIndexOutputPath(string? outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return Path.Combine(_rootDir, ".unity", "code-index.json");
        }

        if (Path.IsPathRooted(outputPath))
        {
            return outputPath;
        }

        return Path.Combine(_rootDir, outputPath);
    }
}
