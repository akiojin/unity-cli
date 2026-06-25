using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UnityCli.Lsp.Core;

public sealed class LspRouterResult
{
    public object? Payload { get; init; }
    public bool HasResponse { get; init; }
    public bool ShouldExit { get; init; }

    public static LspRouterResult NoResponse(bool shouldExit = false) =>
        new() { ShouldExit = shouldExit };

    public static LspRouterResult Response(object payload) =>
        new() { Payload = payload, HasResponse = true };
}

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed class LspRequestRouter
{
    private string _rootDir = string.Empty;
    private readonly LspFileLockProvider _fileLocks = new();

    public bool ShutdownRequested { get; private set; }

    public async Task<LspRouterResult> HandleAsync(JsonElement root)
    {
        var method = root.TryGetProperty("method", out var m) ? m.GetString() : null;
        var id = root.TryGetProperty("id", out var idEl) ? idEl : default;
        if (method is null)
        {
            return LspRouterResult.NoResponse();
        }

        if (method == "initialize")
        {
            try
            {
                var rootUri = root.GetProperty("params").GetProperty("rootUri").GetString();
                if (!string.IsNullOrEmpty(rootUri))
                {
                    _rootDir = Uri2Path(rootUri);
                    LspLogger.Info($"rootDir={_rootDir}");
                }
            }
            catch
            {
            }

            return LspRouterResult.Response(new
            {
                jsonrpc = "2.0",
                id = IdValue(id),
                result = new
                {
                    capabilities = new { documentSymbolProvider = true }
                }
            });
        }

        if (method == "shutdown")
        {
            LspLogger.Info("shutdown");
            ShutdownRequested = true;
            return LspRouterResult.Response(new { jsonrpc = "2.0", id = IdValue(id), result = (object?)null });
        }

        if (method == "exit")
        {
            LspLogger.Info("exit");
            return LspRouterResult.NoResponse(shouldExit: true);
        }

        if (method == "textDocument/documentSymbol")
        {
            var uri = root.GetProperty("params").GetProperty("textDocument").GetProperty("uri").GetString() ?? "";
            var path = Uri2Path(uri);
            var result = await WorkspaceSymbols.DocumentSymbolsAsync(path);
            return Response(id, result);
        }

        if (method == "textDocument/definition")
        {
            var p = root.GetProperty("params");
            var uri = p.GetProperty("textDocument").GetProperty("uri").GetString() ?? "";
            var pos = p.GetProperty("position");
            var result = await DefinitionAsync(Uri2Path(uri), pos.GetProperty("line").GetInt32(), pos.GetProperty("character").GetInt32());
            return Response(id, result);
        }

        if (method == "textDocument/implementation")
        {
            var p = root.GetProperty("params");
            var uri = p.GetProperty("textDocument").GetProperty("uri").GetString() ?? "";
            var pos = p.GetProperty("position");
            var result = await DefinitionAsync(Uri2Path(uri), pos.GetProperty("line").GetInt32(), pos.GetProperty("character").GetInt32());
            return Response(id, result);
        }

        if (method == "textDocument/formatting")
        {
            var p = root.GetProperty("params");
            var uri = p.GetProperty("textDocument").GetProperty("uri").GetString() ?? "";
            var result = await FormattingAsync(Uri2Path(uri));
            return Response(id, result);
        }

        if (method == "workspace/symbol")
        {
            var query = root.GetProperty("params").GetProperty("query").GetString() ?? "";
            if (string.IsNullOrWhiteSpace(query))
            {
                return Response(id, Array.Empty<object>());
            }

            var result = await WorkspaceSymbols.WorkspaceSymbolAsync(query);
            return Response(id, result);
        }

        if (method == "unitycli/ping")
        {
            return Response(id, new { ok = true });
        }

        if (method == "unitycli/referencesByName")
        {
            var symName = root.GetProperty("params").GetProperty("name").GetString() ?? "";
            var result = await WorkspaceSymbols.ReferencesByNameAsync(symName);
            return Response(id, result);
        }

        if (method == "unitycli/renameByNamePath")
        {
            var p = root.GetProperty("params");
            var result = await Edit.RenameByNamePathAsync(
                p.GetProperty("relative").GetString() ?? "",
                p.GetProperty("namePath").GetString() ?? "",
                p.GetProperty("newName").GetString() ?? "",
                p.TryGetProperty("apply", out var apply) && apply.GetBoolean());
            return Response(id, result);
        }

        if (method == "unitycli/replaceSymbolBody")
        {
            var p = root.GetProperty("params");
            var result = await Edit.ReplaceSymbolBodyAsync(
                p.GetProperty("relative").GetString() ?? "",
                p.GetProperty("namePath").GetString() ?? "",
                p.GetProperty("body").GetString() ?? "",
                p.TryGetProperty("apply", out var apply) && apply.GetBoolean());
            return Response(id, result);
        }

        if (method == "unitycli/insertBeforeSymbol" || method == "unitycli/insertAfterSymbol")
        {
            var p = root.GetProperty("params");
            var result = await Edit.InsertAroundSymbolAsync(
                p.GetProperty("relative").GetString() ?? "",
                p.GetProperty("namePath").GetString() ?? "",
                p.GetProperty("text").GetString() ?? "",
                method.EndsWith("AfterSymbol", StringComparison.Ordinal),
                p.TryGetProperty("apply", out var apply) && apply.GetBoolean());
            return Response(id, result);
        }

        if (method == "unitycli/validateTextEdits")
        {
            var p = root.GetProperty("params");
            var result = await Edit.ValidateTextEditsAsync(
                p.GetProperty("relative").GetString() ?? "",
                p.GetProperty("newText").GetString() ?? "");
            return Response(id, result);
        }

        if (method == "unitycli/writeCSharpFile")
        {
            var p = root.GetProperty("params");
            var result = await Edit.WriteCSharpFileAsync(
                p.GetProperty("relative").GetString() ?? "",
                p.GetProperty("newText").GetString() ?? "",
                !p.TryGetProperty("validate", out var validate) || validate.GetBoolean(),
                !p.TryGetProperty("apply", out var apply) || apply.GetBoolean(),
                p.TryGetProperty("format", out var format) && format.GetBoolean());
            return Response(id, result);
        }

        if (method == "unitycli/createCSharpFile")
        {
            var p = root.GetProperty("params");
            var result = await Edit.CreateCSharpFileAsync(
                p.GetProperty("relative").GetString() ?? "",
                p.GetProperty("text").GetString() ?? "",
                p.TryGetProperty("overwrite", out var overwrite) && overwrite.GetBoolean(),
                !p.TryGetProperty("validate", out var validate) || validate.GetBoolean(),
                !p.TryGetProperty("apply", out var apply) || apply.GetBoolean(),
                p.TryGetProperty("format", out var format) && format.GetBoolean());
            return Response(id, result);
        }

        if (method == "unitycli/applyCSharpEdits")
        {
            var p = root.GetProperty("params");
            var result = await Edit.ApplyCSharpEditsAsync(
                p.GetProperty("files"),
                !p.TryGetProperty("validate", out var validate) || validate.GetBoolean(),
                !p.TryGetProperty("apply", out var apply) || apply.GetBoolean(),
                p.TryGetProperty("format", out var format) && format.GetBoolean());
            return Response(id, result);
        }

        if (method == "unitycli/removeSymbol")
        {
            var p = root.GetProperty("params");
            var relative = p.TryGetProperty("relative", out var rel)
                ? rel.GetString() ?? ""
                : p.GetProperty("path").GetString() ?? "";
            var result = await Edit.RemoveSymbolAsync(
                relative,
                p.GetProperty("namePath").GetString() ?? "",
                p.TryGetProperty("apply", out var apply) && apply.GetBoolean(),
                !p.TryGetProperty("failOnReferences", out var failOnRefs) || failOnRefs.GetBoolean(),
                p.TryGetProperty("removeEmptyFile", out var removeEmptyFile) && removeEmptyFile.GetBoolean());
            return Response(id, result);
        }

        if (method == "unitycli/buildCodeIndex")
        {
            string? outputPath = null;
            if (root.TryGetProperty("params", out var param) && param.ValueKind == JsonValueKind.Object)
            {
                if (param.TryGetProperty("outputPath", out var op) && op.ValueKind == JsonValueKind.String)
                {
                    outputPath = op.GetString();
                }
            }

            var result = await CodeIndex.BuildAsync(outputPath);
            return Response(id, result);
        }

        return id.ValueKind == JsonValueKind.Undefined
            ? LspRouterResult.NoResponse()
            : Response(id, (object?)null);
    }

    private LspWorkspaceSymbolService WorkspaceSymbols => new(_rootDir, _fileLocks);
    private LspEditService Edit => new(_rootDir, _fileLocks);
    private LspCodeIndexService CodeIndex => new(_rootDir);

    private static LspRouterResult Response(JsonElement id, object? result) =>
        LspRouterResult.Response(new { jsonrpc = "2.0", id = IdValue(id), result });

    private static object? IdValue(JsonElement id) => id.ValueKind switch
    {
        JsonValueKind.Number => id.GetInt32(),
        JsonValueKind.String => id.GetString(),
        _ => null
    };

    private static string Uri2Path(string uri)
    {
        if (uri.StartsWith("file://", StringComparison.Ordinal))
        {
            uri = uri.Substring("file://".Length);
        }

        return uri.Replace('/', Path.DirectorySeparatorChar);
    }

    private static string Path2Uri(string path) =>
        LspPathUtilities.Path2Uri(path);

    private async Task<object> DefinitionAsync(string path, int line, int character)
    {
        try
        {
            using var handle = await _fileLocks.AcquireAsync(path);
            var text = await File.ReadAllTextAsync(path);
            var tree = CSharpSyntaxTree.ParseText(text);
            var root = await tree.GetRootAsync();
            var offset = GetOffset(text, line, character);
            var token = root.FindToken(offset);
            var idName = token.Parent?.AncestorsAndSelf().OfType<IdentifierNameSyntax>().FirstOrDefault();
            if (idName == null)
            {
                return Array.Empty<object>();
            }

            var name = idName.Identifier.ValueText;
            SyntaxNode? decl = root.DescendantNodes().FirstOrDefault(node =>
                node is ClassDeclarationSyntax c && c.Identifier.ValueText == name
                || node is StructDeclarationSyntax s && s.Identifier.ValueText == name
                || node is InterfaceDeclarationSyntax i && i.Identifier.ValueText == name
                || node is EnumDeclarationSyntax e && e.Identifier.ValueText == name
                || node is MethodDeclarationSyntax m && m.Identifier.ValueText == name
                || node is PropertyDeclarationSyntax p && p.Identifier.ValueText == name
                || node is FieldDeclarationSyntax f && f.Declaration.Variables.Any(v => v.Identifier.ValueText == name));

            if (decl == null)
            {
                return Array.Empty<object>();
            }

            var span = decl.GetLocation().GetLineSpan();
            var start = new { line = span.StartLinePosition.Line, character = span.StartLinePosition.Character };
            var end = new { line = span.EndLinePosition.Line, character = span.EndLinePosition.Character };
            return new[] { new { uri = Path2Uri(path), range = new { start, end } } };
        }
        catch
        {
            return Array.Empty<object>();
        }
    }

    private async Task<object> FormattingAsync(string path)
    {
        try
        {
            using var handle = await _fileLocks.AcquireAsync(path);
            var text = await File.ReadAllTextAsync(path);
            var tree = CSharpSyntaxTree.ParseText(text);
            var root = await tree.GetRootAsync();
            var newText = root.ToFullString();
            if (newText == text)
            {
                return Array.Empty<object>();
            }

            var start = new { line = 0, character = 0 };
            var end = new { line = text.Split('\n').Length, character = 0 };
            return new[] { new { range = new { start, end }, newText } };
        }
        catch
        {
            return Array.Empty<object>();
        }
    }

    private static int GetOffset(string text, int line, int character)
    {
        var curLine = 0;
        var idx = 0;
        while (idx < text.Length && curLine < line)
        {
            if (text[idx++] == '\n')
            {
                curLine++;
            }
        }

        return Math.Min(text.Length, idx + Math.Max(0, character));
    }
}
