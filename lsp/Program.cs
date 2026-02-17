using System.Text.Json;
using System.Text;
using System;
using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

// Minimal LSP over stdio: initialize / initialized / shutdown / exit / documentSymbol / workspace/symbol / mcp/referencesByName / mcp/renameByNamePath / mcp/replaceSymbolBody / mcp/insertBeforeSymbol / mcp/insertAfterSymbol / mcp/removeSymbol
// This is a lightweight PoC that parses each file independently using Roslyn SyntaxTree.

LspLogger.Info("Starting...");
var server = new LspServer();
await server.RunAsync();

sealed class LspServer
{
    private readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
    private bool _shutdownRequested;
    private string _rootDir = "";
    private readonly SemaphoreSlim _requestLimiter = new(Math.Max(1, Environment.ProcessorCount), Math.Max(1, Environment.ProcessorCount));
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Task> _inFlight = new();
    private readonly object _inFlightLock = new();

    public async Task RunAsync()
    {
        while (true)
        {
            var msg = await ReadMessageAsync();
            if (msg is null) break;
            try
            {
                var json = JsonDocument.Parse(msg);
                var root = json.RootElement;
                var method = root.TryGetProperty("method", out var m) ? m.GetString() : null;
                var id = root.TryGetProperty("id", out var idEl) ? idEl : default;
                if (method is not null)
                {
                    if (method == "exit" || method == "shutdown")
                    {
                        await DrainInFlightAsync();
                        await HandleMessageAsync(root, method, id);
                        if (method == "exit") break;
                    }
                    else
                    {
                        var task = Task.Run(async () =>
                        {
                            await _requestLimiter.WaitAsync();
                            try
                            {
                                await HandleMessageAsync(root, method, id);
                            }
                            finally
                            {
                                _requestLimiter.Release();
                            }
                        });
                        lock (_inFlightLock)
                        {
                            _inFlight.Add(task);
                            _inFlight.RemoveAll(t => t.IsCompleted);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LspLogger.Error($"Failed to process message: {ex.Message}");
            }
        }
        await DrainInFlightAsync();
    }

    private async Task DrainInFlightAsync()
    {
        Task[] snapshot;
        lock (_inFlightLock)
        {
            snapshot = _inFlight.ToArray();
            _inFlight.Clear();
        }
        if (snapshot.Length > 0)
        {
            try { await Task.WhenAll(snapshot); } catch { }
        }
    }

    private async Task HandleMessageAsync(JsonElement root, string method, JsonElement id)
    {
        if (method == "initialize")
        {
            LspLogger.Debug("initialize");
            try
            {
                var rootUri = root.GetProperty("params").GetProperty("rootUri").GetString();
                if (!string.IsNullOrEmpty(rootUri))
                {
                    _rootDir = Uri2Path(rootUri);
                    LspLogger.Info($"rootDir={_rootDir}");
                }
            }
            catch { }
            var resp = new
            {
                jsonrpc = "2.0",
                id = id.ValueKind == JsonValueKind.Number ? id.GetInt32() : (int?)null,
                result = new
                {
                    capabilities = new { documentSymbolProvider = true }
                }
            };
            await WriteMessageAsync(resp);
            return;
        }
        if (method == "shutdown")
        {
            LspLogger.Info("shutdown");
            _shutdownRequested = true;
            var resp = new { jsonrpc = "2.0", id = id.GetInt32(), result = (object?)null };
            await WriteMessageAsync(resp);
            return;
        }
        if (method == "exit")
        {
            LspLogger.Info("exit");
            return;
        }
        if (method == "textDocument/documentSymbol")
        {
            var uri = root.GetProperty("params").GetProperty("textDocument").GetProperty("uri").GetString() ?? "";
            var path = Uri2Path(uri);
            var result = await DocumentSymbolsAsync(path);
            await WriteMessageAsync(new { jsonrpc = "2.0", id = id.GetInt32(), result });
            return;
        }
        if (method == "textDocument/definition")
        {
            var p = root.GetProperty("params");
            var uri = p.GetProperty("textDocument").GetProperty("uri").GetString() ?? "";
            var pos = p.GetProperty("position");
            var def = await DefinitionAsync(Uri2Path(uri), pos.GetProperty("line").GetInt32(), pos.GetProperty("character").GetInt32());
            await WriteMessageAsync(new { jsonrpc = "2.0", id = id.GetInt32(), result = def });
            return;
        }
        if (method == "textDocument/implementation")
        {
            var p = root.GetProperty("params");
            var uri = p.GetProperty("textDocument").GetProperty("uri").GetString() ?? "";
            var pos = p.GetProperty("position");
            var impl = await DefinitionAsync(Uri2Path(uri), pos.GetProperty("line").GetInt32(), pos.GetProperty("character").GetInt32());
            await WriteMessageAsync(new { jsonrpc = "2.0", id = id.GetInt32(), result = impl });
            return;
        }
        if (method == "textDocument/formatting")
        {
            var p = root.GetProperty("params");
            var uri = p.GetProperty("textDocument").GetProperty("uri").GetString() ?? "";
            var edits = await FormattingAsync(Uri2Path(uri));
            await WriteMessageAsync(new { jsonrpc = "2.0", id = id.GetInt32(), result = edits });
            return;
        }
        if (method == "workspace/symbol")
        {
            var query = root.GetProperty("params").GetProperty("query").GetString() ?? "";
            if (string.IsNullOrWhiteSpace(query))
            {
                await WriteMessageAsync(new { jsonrpc = "2.0", id = id.GetInt32(), result = Array.Empty<object>() });
                return;
            }
            var result = await WorkspaceSymbolAsync(query);
            await WriteMessageAsync(new { jsonrpc = "2.0", id = id.GetInt32(), result });
            return;
        }
        if (method == "mcp/ping")
        {
            await WriteMessageAsync(new { jsonrpc = "2.0", id = id.GetInt32(), result = new { ok = true } });
            return;
        }
        if (method == "mcp/referencesByName")
        {
            var symName = root.GetProperty("params").GetProperty("name").GetString() ?? "";
            var list = await ReferencesByNameAsync(symName);
            await WriteMessageAsync(new { jsonrpc = "2.0", id = id.GetInt32(), result = list });
            return;
        }
        if (method == "mcp/renameByNamePath")
        {
            var p = root.GetProperty("params");
            var relative = p.GetProperty("relative").GetString() ?? "";
            var namePath = p.GetProperty("namePath").GetString() ?? "";
            var newName = p.GetProperty("newName").GetString() ?? "";
            var apply = p.TryGetProperty("apply", out var a) && a.GetBoolean();
            var resp = await RenameByNamePathAsync(relative, namePath, newName, apply);
            await WriteMessageAsync(new { jsonrpc = "2.0", id = id.GetInt32(), result = resp });
            return;
        }
        if (method == "mcp/replaceSymbolBody")
        {
            var p = root.GetProperty("params");
            var relative = p.GetProperty("relative").GetString() ?? "";
            var namePath = p.GetProperty("namePath").GetString() ?? "";
            var body = p.GetProperty("body").GetString() ?? "";
            var apply = p.TryGetProperty("apply", out var a2) && a2.GetBoolean();
            var resp = await ReplaceSymbolBodyAsync(relative, namePath, body, apply);
            await WriteMessageAsync(new { jsonrpc = "2.0", id = id.GetInt32(), result = resp });
            return;
        }
        if (method == "mcp/insertBeforeSymbol" || method == "mcp/insertAfterSymbol")
        {
            var p = root.GetProperty("params");
            var relative = p.GetProperty("relative").GetString() ?? "";
            var namePath = p.GetProperty("namePath").GetString() ?? "";
            var text = p.GetProperty("text").GetString() ?? "";
            var apply = p.TryGetProperty("apply", out var a3) && a3.GetBoolean();
            bool after = method.EndsWith("AfterSymbol", StringComparison.Ordinal);
            var resp = await InsertAroundSymbolAsync(relative, namePath, text, after, apply);
            await WriteMessageAsync(new { jsonrpc = "2.0", id = id.GetInt32(), result = resp });
            return;
        }
        if (method == "mcp/validateTextEdits")
        {
            var p = root.GetProperty("params");
            var relative = p.GetProperty("relative").GetString() ?? "";
            var newText = p.GetProperty("newText").GetString() ?? "";
            var result = await ValidateTextEditsAsync(relative, newText);
            await WriteMessageAsync(new { jsonrpc = "2.0", id = id.GetInt32(), result });
            return;
        }
        if (method == "mcp/removeSymbol")
        {
            var p = root.GetProperty("params");
            var relative = p.GetProperty("relative").GetString() ?? p.GetProperty("path").GetString() ?? "";
            var namePath = p.GetProperty("namePath").GetString() ?? "";
            var apply = p.TryGetProperty("apply", out var a4) && a4.GetBoolean();
            var failOnRefs = !p.TryGetProperty("failOnReferences", out var fr) || fr.GetBoolean();
            var removeEmpty = p.TryGetProperty("removeEmptyFile", out var rf) && rf.GetBoolean();
            var resp = await RemoveSymbolAsync(relative, namePath, apply, failOnRefs, removeEmpty);
            await WriteMessageAsync(new { jsonrpc = "2.0", id = id.GetInt32(), result = resp });
            return;
        }
        if (method == "mcp/buildCodeIndex")
        {
            string? outputPath = null;
            if (root.TryGetProperty("params", out var param) && param.ValueKind == JsonValueKind.Object)
            {
                if (param.TryGetProperty("outputPath", out var op) && op.ValueKind == JsonValueKind.String)
                {
                    outputPath = op.GetString();
                }
            }

            var resp = await BuildCodeIndexAsync(outputPath);
            await WriteMessageAsync(new { jsonrpc = "2.0", id = id.GetInt32(), result = resp });
            return;
        }
        if (id.ValueKind != JsonValueKind.Undefined)
        {
            await WriteMessageAsync(new { jsonrpc = "2.0", id = id.GetInt32(), result = (object?)null });
        }
    }

    private async Task<IDisposable> AcquireFileLockAsync(string path)
    {
        var key = path ?? string.Empty;
        var sem = _fileLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync();
        return new Releaser(() => sem.Release());
    }

    private sealed class Releaser : IDisposable
    {
        private readonly Action _release;
        public Releaser(Action release) => _release = release;
        public void Dispose() => _release();
    }

    private static string Uri2Path(string uri)
    {
        if (uri.StartsWith("file://")) uri = uri.Substring("file://".Length);
        return uri.Replace('/', System.IO.Path.DirectorySeparatorChar);
    }

    private async Task<object> DocumentSymbolsAsync(string path)
    {
        try
        {
            var handle = await AcquireFileLockAsync(path);
            try
            {
                var text = await File.ReadAllTextAsync(path);
                var tree = CSharpSyntaxTree.ParseText(text);
                var root = await tree.GetRootAsync();
                var list = new List<object>();
                foreach (var node in root.DescendantNodes())
                {
                    if (node is NamespaceDeclarationSyntax ns)
                    {
                        list.Add(MakeSym(ns.Name.ToString(), 3, node));
                    }
                    else if (node is ClassDeclarationSyntax c)
                    {
                        list.Add(MakeSym(c.Identifier.ValueText, 5, node));
                    }
                    else if (node is StructDeclarationSyntax s)
                    {
                        list.Add(MakeSym(s.Identifier.ValueText, 23, node));
                    }
                    else if (node is InterfaceDeclarationSyntax i)
                    {
                        list.Add(MakeSym(i.Identifier.ValueText, 11, node));
                    }
                    else if (node is EnumDeclarationSyntax e)
                    {
                        list.Add(MakeSym(e.Identifier.ValueText, 10, node));
                    }
                    else if (node is MethodDeclarationSyntax m)
                    {
                        list.Add(MakeSym(m.Identifier.ValueText, 6, node));
                    }
                    else if (node is PropertyDeclarationSyntax p)
                    {
                        list.Add(MakeSym(p.Identifier.ValueText, 7, node));
                    }
                    else if (node is FieldDeclarationSyntax f)
                    {
                        var v = f.Declaration.Variables.FirstOrDefault();
                        if (v != null) list.Add(MakeSym(v.Identifier.ValueText, 8, node));
                    }
                }
                return list;
            }
            finally
            {
                handle.Dispose();
            }
        }
        catch
        {
            return Array.Empty<object>();
        }
    }

    private async Task<object> WorkspaceSymbolAsync(string query)
    {
        var results = new List<object>();
        foreach (var file in EnumerateUnityCsFiles(_rootDir))
        {
            try
            {
                var handle = await AcquireFileLockAsync(file);
                try
                {
                    var text = await File.ReadAllTextAsync(file);
                    var tree = CSharpSyntaxTree.ParseText(text);
                    var root = await tree.GetRootAsync();
                    foreach (var node in root.DescendantNodes())
                    {
                        (int kind, string name) = node switch
                        {
                            ClassDeclarationSyntax c => (5, c.Identifier.ValueText),
                            StructDeclarationSyntax s => (23, s.Identifier.ValueText),
                            InterfaceDeclarationSyntax i => (11, i.Identifier.ValueText),
                            EnumDeclarationSyntax e => (10, e.Identifier.ValueText),
                            MethodDeclarationSyntax m => (6, m.Identifier.ValueText),
                            PropertyDeclarationSyntax p => (7, p.Identifier.ValueText),
                            FieldDeclarationSyntax f => (8, f.Declaration.Variables.FirstOrDefault()?.Identifier.ValueText ?? ""),
                            _ => (0, "")
                        };
                        if (kind == 0 || string.IsNullOrEmpty(name)) continue;
                        if (name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
                        var span = node.GetLocation().GetLineSpan();
                        var start = new { line = span.StartLinePosition.Line, character = span.StartLinePosition.Character };
                        var end = new { line = span.EndLinePosition.Line, character = span.EndLinePosition.Character };
                        results.Add(new
                        {
                            name,
                            kind,
                            location = new { uri = Path2Uri(file), range = new { start, end } }
                        });
                    }
                }
                finally
                {
                    handle.Dispose();
                }
            }
            catch { }
        }
        return results;
    }

    private async Task<object> ReferencesByNameAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return Array.Empty<object>();
        var list = new List<object>();
        foreach (var file in EnumerateUnityCsFiles(_rootDir))
        {
            try
            {
                var handle = await AcquireFileLockAsync(file);
                try
                {
                    var text = await File.ReadAllTextAsync(file);
                    var tree = CSharpSyntaxTree.ParseText(text);
                    var root = await tree.GetRootAsync();
                    foreach (var id in root.DescendantTokens().Where(t => t.IsKind(SyntaxKind.IdentifierToken) && t.ValueText == name))
                    {
                        var span = id.GetLocation().GetLineSpan();
                        var lineIdx = span.StartLinePosition.Line;
                        var col = span.StartLinePosition.Character + 1;
                        var snippet = GetLine(text, lineIdx).Trim();
                        list.Add(new { path = ToRel(file, _rootDir), line = lineIdx + 1, column = col, snippet });
                    }
                }
                finally
                {
                    handle.Dispose();
                }
            }
            catch { }
        }
        return list;
    }

    private static string GetLine(string text, int zeroBasedLine)
    {
        var sr = new System.IO.StringReader(text);
        string? line = null; int i = 0;
        while ((line = sr.ReadLine()) != null)
        {
            if (i++ == zeroBasedLine) return line;
        }
        return string.Empty;
    }

    private async Task<object> DefinitionAsync(string path, int line, int character)
    {
        try
        {
            var handle = await AcquireFileLockAsync(path);
            try
            {
                var text = await File.ReadAllTextAsync(path);
                var tree = CSharpSyntaxTree.ParseText(text);
                var root = await tree.GetRootAsync();
                int offset = GetOffset(text, line, character);
                var token = root.FindToken(offset);
                var idName = token.Parent?.AncestorsAndSelf().OfType<IdentifierNameSyntax>().FirstOrDefault();
                if (idName == null) return Array.Empty<object>();
                // Search same-file declarations
                SyntaxNode? decl = root.DescendantNodes().FirstOrDefault(n =>
                    n is ClassDeclarationSyntax c && c.Identifier.ValueText == idName.Identifier.ValueText
                 || n is StructDeclarationSyntax s && s.Identifier.ValueText == idName.Identifier.ValueText
                 || n is InterfaceDeclarationSyntax i && i.Identifier.ValueText == idName.Identifier.ValueText
                 || n is EnumDeclarationSyntax e && e.Identifier.ValueText == idName.Identifier.ValueText
                 || n is MethodDeclarationSyntax m && m.Identifier.ValueText == idName.Identifier.ValueText
                 || n is PropertyDeclarationSyntax p && p.Identifier.ValueText == idName.Identifier.ValueText
                 || (n is FieldDeclarationSyntax f && f.Declaration.Variables.Any(v => v.Identifier.ValueText == idName.Identifier.ValueText))
                );
                if (decl == null) return Array.Empty<object>();
                var span = decl.GetLocation().GetLineSpan();
                var start = new { line = span.StartLinePosition.Line, character = span.StartLinePosition.Character };
                var end = new { line = span.EndLinePosition.Line, character = span.EndLinePosition.Character };
                return new[] { new { uri = Path2Uri(path), range = new { start, end } } };
            }
            finally
            {
                handle.Dispose();
            }
        }
        catch { return Array.Empty<object>(); }
    }

    private static int GetOffset(string text, int line, int character)
    {
        int curLine = 0, idx = 0;
        while (idx < text.Length && curLine < line)
        {
            if (text[idx++] == '\n') curLine++;
        }
        return Math.Min(text.Length, idx + Math.Max(0, character));
    }

    private async Task<object> FormattingAsync(string path)
    {
        try
        {
            var handle = await AcquireFileLockAsync(path);
            try
            {
                var text = await File.ReadAllTextAsync(path);
                var tree = CSharpSyntaxTree.ParseText(text);
                var root = await tree.GetRootAsync();
                // Without Workspaces dependency, return full-document replace as no-op or minimal normalized
                var newText = root.ToFullString();
                if (newText == text) return Array.Empty<object>();
                var start = new { line = 0, character = 0 };
                var end = new { line = text.Split('\n').Length, character = 0 };
                return new[] { new { range = new { start, end }, newText } };
            }
            finally
            {
                handle.Dispose();
            }
        }
        catch { return Array.Empty<object>(); }
    }

    private async Task<object> RenameByNamePathAsync(string relative, string namePath, string newName, bool apply)
    {
        var full = Path.Combine(_rootDir, relative.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(full)) return new { success = false, applied = false, error = "file_not_found" };
        try
        {
            var handle = await AcquireFileLockAsync(full);
            try
            {
            var text = await File.ReadAllTextAsync(full);
            var tree = CSharpSyntaxTree.ParseText(text);
            var root = await tree.GetRootAsync();
            var segments = (namePath ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length == 0) return new { success = false, applied = false, error = "invalid_namePath" };
            SyntaxNode cursor = root;
            for (int i = 0; i < segments.Length - 1; i++)
            {
                var seg = segments[i];
                var next = cursor.DescendantNodes().FirstOrDefault(n => n is ClassDeclarationSyntax c && c.Identifier.ValueText == seg
                                                                      || n is StructDeclarationSyntax s && s.Identifier.ValueText == seg
                                                                      || n is InterfaceDeclarationSyntax ii && ii.Identifier.ValueText == seg
                                                                      || n is EnumDeclarationSyntax en && en.Identifier.ValueText == seg);
                if (next is null) return new { success = false, applied = false, error = "container_not_found", segment = seg };
                cursor = next;
            }
            var targetName = segments[^1];
            SyntaxNode? decl = cursor.DescendantNodes().FirstOrDefault(n => n is ClassDeclarationSyntax c && c.Identifier.ValueText == targetName
                                                                          || n is StructDeclarationSyntax s && s.Identifier.ValueText == targetName
                                                                          || n is InterfaceDeclarationSyntax ii && ii.Identifier.ValueText == targetName
                                                                          || n is EnumDeclarationSyntax en && en.Identifier.ValueText == targetName)
                             ?? cursor.DescendantNodes().FirstOrDefault(n => n is MethodDeclarationSyntax m && m.Identifier.ValueText == targetName
                                                                          || n is PropertyDeclarationSyntax p && p.Identifier.ValueText == targetName
                                                                          || n is FieldDeclarationSyntax f && f.Declaration.Variables.Any(v => v.Identifier.ValueText == targetName));
            if (decl is null) return new { success = false, applied = false, error = "symbol_not_found" };

            // Replace identifier token text (declaration only)
            SyntaxNode newRoot = root;
            if (decl is ClassDeclarationSyntax dc)
                newRoot = root.ReplaceToken(dc.Identifier, SyntaxFactory.Identifier(newName).WithTriviaFrom(dc.Identifier));
            else if (decl is StructDeclarationSyntax ds)
                newRoot = root.ReplaceToken(ds.Identifier, SyntaxFactory.Identifier(newName).WithTriviaFrom(ds.Identifier));
            else if (decl is InterfaceDeclarationSyntax di)
                newRoot = root.ReplaceToken(di.Identifier, SyntaxFactory.Identifier(newName).WithTriviaFrom(di.Identifier));
            else if (decl is EnumDeclarationSyntax de)
                newRoot = root.ReplaceToken(de.Identifier, SyntaxFactory.Identifier(newName).WithTriviaFrom(de.Identifier));
            else if (decl is MethodDeclarationSyntax dm)
                newRoot = root.ReplaceToken(dm.Identifier, SyntaxFactory.Identifier(newName).WithTriviaFrom(dm.Identifier));
            else if (decl is PropertyDeclarationSyntax dp)
                newRoot = root.ReplaceToken(dp.Identifier, SyntaxFactory.Identifier(newName).WithTriviaFrom(dp.Identifier));
            else if (decl is FieldDeclarationSyntax df)
            {
                var v = df.Declaration.Variables.FirstOrDefault(v => v.Identifier.ValueText == targetName);
                if (v != null) newRoot = root.ReplaceToken(v.Identifier, SyntaxFactory.Identifier(newName).WithTriviaFrom(v.Identifier));
            }

            // 衝突検出: 同一コンテナ内に同名シンボルが既に存在する場合は失敗（安全側）
            bool conflict = false;
            if (decl is ClassDeclarationSyntax dc2)
            {
                var parent = dc2.Parent;
                var exists = parent?.DescendantNodes().FirstOrDefault(n => n is ClassDeclarationSyntax c && c != dc2 && c.Identifier.ValueText == newName
                                                                          || n is StructDeclarationSyntax s && s.Identifier.ValueText == newName
                                                                          || n is InterfaceDeclarationSyntax ii && ii.Identifier.ValueText == newName
                                                                          || n is EnumDeclarationSyntax en && en.Identifier.ValueText == newName);
                conflict = exists != null;
            }
            else if (decl is StructDeclarationSyntax ds2 || decl is InterfaceDeclarationSyntax || decl is EnumDeclarationSyntax)
            {
                var parent = decl.Parent;
                var exists = parent?.DescendantNodes().FirstOrDefault(n => n is ClassDeclarationSyntax c && c != decl && c.Identifier.ValueText == newName
                                                                          || n is StructDeclarationSyntax s && s != decl && s.Identifier.ValueText == newName
                                                                          || n is InterfaceDeclarationSyntax ii && ii != decl && ii.Identifier.ValueText == newName
                                                                          || n is EnumDeclarationSyntax en && en != decl && en.Identifier.ValueText == newName);
                conflict = exists != null;
            }
            else if (decl is MethodDeclarationSyntax dm2)
            {
                if (dm2.Parent is TypeDeclarationSyntax tparent)
                {
                    var sig = dm2.ParameterList?.ToFullString() ?? string.Empty;
                    conflict = tparent.Members.OfType<MethodDeclarationSyntax>()
                        .Any(m => !object.ReferenceEquals(m, dm2) && m.Identifier.ValueText == newName && (m.ParameterList?.ToFullString() ?? "") == sig);
                }
            }
            else if (decl is PropertyDeclarationSyntax dp2)
            {
                if (dp2.Parent is TypeDeclarationSyntax tparent)
                {
                    conflict = tparent.Members.OfType<PropertyDeclarationSyntax>()
                        .Any(p => !object.ReferenceEquals(p, dp2) && p.Identifier.ValueText == newName);
                }
            }
            else if (decl is FieldDeclarationSyntax df2)
            {
                if (df2.Parent is TypeDeclarationSyntax tparent)
                {
                    conflict = tparent.Members.OfType<FieldDeclarationSyntax>()
                        .SelectMany(f => f.Declaration.Variables)
                        .Any(v => v.Identifier.ValueText == newName);
                }
            }
            if (conflict)
            {
                return new { success = false, applied = false, error = "name_conflict" };
            }

            // Extend rename: if type/member, update identifier usages across workspace within matching containers
            bool isTypeDecl = decl is ClassDeclarationSyntax || decl is StructDeclarationSyntax || decl is InterfaceDeclarationSyntax || decl is EnumDeclarationSyntax;
            bool isMemberDecl = decl is MethodDeclarationSyntax || decl is PropertyDeclarationSyntax || decl is FieldDeclarationSyntax;
            if (!(isTypeDecl || isMemberDecl))
            {
                var newText = newRoot.ToFullString();
                if (apply)
                {
                    await File.WriteAllTextAsync(full, newText, Encoding.UTF8);
                    return new { success = true, applied = true };
                }
                return new { success = true, applied = false, preview = DiffPreview(text, newText) };
            }

            var containers = segments.Take(segments.Length - 1).ToArray();
            var nsTarget = GetNamespaceChain(decl);
            var oldName = targetName;
            var updatedFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [full] = newRoot.ToFullString() };
            foreach (var file in EnumerateUnityCsFiles(_rootDir))
            {
                // Member rename is limited to the declaration file; type rename updates across workspace
                if (isMemberDecl && !string.Equals(file, full, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var fileHandle = await AcquireFileLockAsync(file);
                    try
                    {
                        var src = await File.ReadAllTextAsync(file);
                        var t = CSharpSyntaxTree.ParseText(src);
                        var r = await t.GetRootAsync();
                        var tokens = r.DescendantTokens().Where(tk => tk.IsKind(SyntaxKind.IdentifierToken) && tk.ValueText == oldName).ToArray();
                        if (tokens.Length == 0) continue;
                        bool changed = false;
                        SyntaxNode rr = r;
                        foreach (var tk in tokens)
                        {
                            bool inUsing = tk.Parent != null && tk.Parent.AncestorsAndSelf().Any(a => a is UsingDirectiveSyntax);
                            if (isMemberDecl && inUsing) continue;
                            if (!NamespaceEndsWith(GetNamespaceChain(tk.Parent), nsTarget)) continue;
                            if (inUsing && isTypeDecl)
                            {
                                var chain = GetUsingNameChain(tk.Parent);
                                if (!ChainEndsWith(chain, Concat(containers, oldName))) continue;
                            }
                            else
                            {
                                if (!ContainerEndsWith(GetTypeContainerChain(tk.Parent), containers)) continue;
                            }
                            if (isTypeDecl)
                            {
                                rr = rr.ReplaceToken(tk, SyntaxFactory.Identifier(newName).WithTriviaFrom(tk));
                                changed = true;
                            }
                            else if (isMemberDecl)
                            {
                                if (tk.Parent is IdentifierNameSyntax)
                                {
                                    rr = rr.ReplaceToken(tk, SyntaxFactory.Identifier(newName).WithTriviaFrom(tk));
                                    changed = true;
                                }
                            }
                        }
                        if (changed) updatedFiles[file] = rr.ToFullString();
                    }
                    finally
                    {
                        fileHandle.Dispose();
                    }
                }
                catch { }
            }
            if (apply)
            {
                var filesToLock = updatedFiles.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToArray();
                var locks = new List<IDisposable>(filesToLock.Length);
                try
                {
                    foreach (var file in filesToLock)
                    {
                        locks.Add(await AcquireFileLockAsync(file));
                    }
                    foreach (var kv in updatedFiles)
                        await File.WriteAllTextAsync(kv.Key, kv.Value, Encoding.UTF8);
                }
                finally
                {
                    foreach (var l in locks) l.Dispose();
                }
                return new { success = true, applied = true, updated = updatedFiles.Count };
            }
            return new { success = true, applied = false, preview = DiffPreview(text, updatedFiles.Values.FirstOrDefault() ?? newRoot.ToFullString()) };
            }
            finally
            {
                handle.Dispose();
            }
        }
        catch (Exception ex)
        {
            return new { success = false, applied = false, error = ex.Message };
        }
    }

    private static string DiffPreview(string oldText, string newText)
    {
        // Minimal diff: return new text truncated
        if (newText.Length > 1000) return newText.Substring(0, 1000) + "…";
        return newText;
    }

    private async Task<object> ValidateTextEditsAsync(string relative, string newText)
    {
        try
        {
            var text = newText ?? string.Empty;
            if (string.IsNullOrEmpty(text))
            {
                var full = Path.Combine(_rootDir, relative.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(full))
                {
                    var handle = await AcquireFileLockAsync(full);
                    try
                    {
                        text = await File.ReadAllTextAsync(full);
                    }
                    finally
                    {
                        handle.Dispose();
                    }
                }
            }
            var tree = CSharpSyntaxTree.ParseText(text);
            var diagnostics = tree.GetDiagnostics();
            var list = new List<object>();
            foreach (var diag in diagnostics)
            {
                var span = diag.Location.GetLineSpan();
                list.Add(new
                {
                    severity = diag.Severity.ToString().ToLowerInvariant(),
                    id = diag.Id,
                    message = diag.GetMessage(),
                    line = span.StartLinePosition.Line + 1,
                    column = span.StartLinePosition.Character + 1
                });
            }
            return new { diagnostics = list };
        }
        catch (Exception ex)
        {
            var errorList = new List<object>();
            errorList.Add(new
            {
                severity = "error",
                id = "validateTextEdits",
                message = ex.Message,
                line = 0,
                column = 0
            });
            return new
            {
                diagnostics = errorList,
                error = ex.Message
            };
        }
    }

    private async Task<object> ReplaceSymbolBodyAsync(string relative, string namePath, string bodyText, bool apply)
    {
        var full = Path.Combine(_rootDir, relative.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(full)) return new { success = false, applied = false, error = "file_not_found" };
        var handle = await AcquireFileLockAsync(full);
        try
        {
            var text = await File.ReadAllTextAsync(full);
            var tree = CSharpSyntaxTree.ParseText(text);
            var root = await tree.GetRootAsync();
            var (_, last) = FindNodeByNamePath(root, namePath);
            if (last is not MethodDeclarationSyntax method) return new { success = false, applied = false, error = "method_not_found" };
            var block = ParseBlock(bodyText);
            // handle expression-bodied to block conversion
            var m2 = method.WithExpressionBody(null).WithSemicolonToken(default).WithBody(block);
            var newRoot = root.ReplaceNode(method, m2);
            var newText = newRoot.ToFullString();
            if (apply) { await File.WriteAllTextAsync(full, newText, Encoding.UTF8); return new { success = true, applied = true }; }
            return new { success = true, applied = false, preview = DiffPreview(text, newText) };
        }
        finally
        {
            handle.Dispose();
        }
    }

    private async Task<object> InsertAroundSymbolAsync(string relative, string namePath, string textToInsert, bool after, bool apply)
    {
        var full = Path.Combine(_rootDir, relative.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(full)) return new { success = false, applied = false, error = "file_not_found" };
        var handle = await AcquireFileLockAsync(full);
        try
        {
            var original = await File.ReadAllTextAsync(full);
            var tree = CSharpSyntaxTree.ParseText(original);
            var root = await tree.GetRootAsync();
            var (_, last) = FindNodeByNamePath(root, namePath);
            if (last is null) return new { success = false, applied = false, error = "symbol_not_found" };
            // insert members at class/namespace level using Roslyn API
            var member = SyntaxFactory.ParseMemberDeclaration(textToInsert);
            if (member is null)
            {
                // fallback to textual insertion
                var pos = after ? last.FullSpan.End : last.FullSpan.Start;
                var newText0 = original.Substring(0, pos) + textToInsert + original.Substring(pos);
                if (apply) { await File.WriteAllTextAsync(full, newText0, Encoding.UTF8); return new { success = true, applied = true }; }
                return new { success = true, applied = false, preview = DiffPreview(original, newText0) };
            }
            SyntaxNode newRoot;
            if (last.Parent is ClassDeclarationSyntax cls)
            {
                var members = after ? cls.Members.Insert(cls.Members.IndexOf((MemberDeclarationSyntax)last) + 1, member)
                                    : cls.Members.Insert(cls.Members.IndexOf((MemberDeclarationSyntax)last), member);
                var cls2 = cls.WithMembers(members);
                newRoot = root.ReplaceNode(cls, cls2);
            }
            else if (last.Parent is NamespaceDeclarationSyntax ns)
            {
                var members = after ? ns.Members.Insert(ns.Members.IndexOf((MemberDeclarationSyntax)last) + 1, member)
                                    : ns.Members.Insert(ns.Members.IndexOf((MemberDeclarationSyntax)last), member);
                var ns2 = ns.WithMembers(members);
                newRoot = root.ReplaceNode(ns, ns2);
            }
            else
            {
                // fallback textual for unsupported contexts
                var pos = after ? last.FullSpan.End : last.FullSpan.Start;
                var newText1 = original.Substring(0, pos) + textToInsert + original.Substring(pos);
                if (apply) { await File.WriteAllTextAsync(full, newText1, Encoding.UTF8); return new { success = true, applied = true }; }
                return new { success = true, applied = false, preview = DiffPreview(original, newText1) };
            }
            var newText = newRoot.ToFullString();
            if (apply) { await File.WriteAllTextAsync(full, newText, Encoding.UTF8); return new { success = true, applied = true }; }
            return new { success = true, applied = false, preview = DiffPreview(original, newText) };
        }
        finally
        {
            handle.Dispose();
        }
    }

    private static (SyntaxNode cursor, SyntaxNode? last) FindNodeByNamePath(SyntaxNode root, string namePath)
    {
        var segs = (namePath ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        SyntaxNode cursor = root;
        SyntaxNode? last = null;
        int matched = 0;
        for (int i = 0; i < segs.Length; i++)
        {
            var seg = segs[i];
            var next = cursor.DescendantNodes().FirstOrDefault(n => n is ClassDeclarationSyntax c && c.Identifier.ValueText == seg
                                                                  || n is StructDeclarationSyntax s && s.Identifier.ValueText == seg
                                                                  || n is InterfaceDeclarationSyntax ii && ii.Identifier.ValueText == seg
                                                                  || n is EnumDeclarationSyntax en && en.Identifier.ValueText == seg
                                                                  || n is MethodDeclarationSyntax m && m.Identifier.ValueText == seg
                                                                  || n is PropertyDeclarationSyntax p && p.Identifier.ValueText == seg
                                                                  || (n is FieldDeclarationSyntax f && f.Declaration.Variables.Any(v => v.Identifier.ValueText == seg)));
            if (next is null)
            {
                // 途中で未一致 → 完全一致ではないため last=null を返す
                return (cursor, null);
            }
            cursor = next;
            matched++;
        }
        // 全セグメント一致した場合のみ last を返す
        last = (matched == segs.Length && segs.Length > 0) ? cursor : null;
        return (cursor, last);
    }

    private static string[] GetTypeContainerChain(SyntaxNode? node)
    {
        var list = new List<string>();
        for (var cur = node; cur != null; cur = cur.Parent)
        {
            if (cur is ClassDeclarationSyntax c) list.Add(c.Identifier.ValueText);
            else if (cur is StructDeclarationSyntax s) list.Add(s.Identifier.ValueText);
            else if (cur is InterfaceDeclarationSyntax i) list.Add(i.Identifier.ValueText);
            else if (cur is EnumDeclarationSyntax e) list.Add(e.Identifier.ValueText);
        }
        list.Reverse();
        return list.ToArray();
    }

    private static bool ContainerEndsWith(string[] chain, string[] suffix)
    {
        if (suffix.Length == 0) return true;
        if (chain.Length < suffix.Length) return false;
        for (int i = 1; i <= suffix.Length; i++)
        {
            if (!string.Equals(chain[^i], suffix[^i], StringComparison.Ordinal)) return false;
        }
        return true;
    }

    private static string[] GetNamespaceChain(SyntaxNode? node)
    {
        var list = new List<string>();
        for (var cur = node; cur != null; cur = cur.Parent)
        {
            if (cur is NamespaceDeclarationSyntax ns) list.Add(ns.Name.ToString());
            else if (cur is FileScopedNamespaceDeclarationSyntax fns) list.Add(fns.Name.ToString());
        }
        list.Reverse();
        var flat = new List<string>();
        foreach (var n in list)
        {
            if (string.IsNullOrWhiteSpace(n)) continue;
            flat.AddRange(n.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        return flat.ToArray();
    }

    private static bool NamespaceEndsWith(string[] chain, string[] suffix)
    {
        if (suffix.Length == 0) return true;
        if (chain.Length < suffix.Length) return false;
        for (int i = 1; i <= suffix.Length; i++)
        {
            if (!string.Equals(chain[^i], suffix[^i], StringComparison.Ordinal)) return false;
        }
        return true;
    }

    private static string[] GetUsingNameChain(SyntaxNode? node)
    {
        var u = node?.AncestorsAndSelf().OfType<UsingDirectiveSyntax>().FirstOrDefault();
        if (u == null || u.Name == null) return Array.Empty<string>();
        var name = u.Name.ToString();
        return name.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string[] Concat(string[] prefix, string last)
    {
        var list = new List<string>(prefix.Length + 1);
        list.AddRange(prefix);
        list.Add(last);
        return list.ToArray();
    }

    private static bool ChainEndsWith(string[] chain, string[] suffix)
    {
        if (suffix.Length == 0) return true;
        if (chain.Length < suffix.Length) return false;
        for (int i = 1; i <= suffix.Length; i++)
        {
            if (!string.Equals(chain[^i], suffix[^i], StringComparison.Ordinal)) return false;
        }
        return true;
    }
    private static BlockSyntax ParseBlock(string body)
    {
        // Try parse as a block statement first
        var txt = body ?? string.Empty;
        SyntaxNode? stmt = null;
        try { stmt = SyntaxFactory.ParseStatement(txt); } catch { }
        if (stmt is BlockSyntax b) return b;
        // Fallback: wrap into method and extract the body
        var code = $"class C{{ void M() {txt} }}";
        try
        {
            var tree = CSharpSyntaxTree.ParseText(code);
            var root = tree.GetRoot();
            var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            if (method?.Body != null) return method.Body;
        }
        catch { }
        return SyntaxFactory.Block();
    }

    private async Task<object> RemoveSymbolAsync(string relative, string namePath, bool apply, bool failOnRefs, bool removeEmptyFile)
    {
        var full = Path.Combine(_rootDir, relative.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(full)) return new { success = false, applied = false, error = "file_not_found" };
        try
        {
            var handle = await AcquireFileLockAsync(full);
            try
            {
            // Locate target declaration first
            var original = await File.ReadAllTextAsync(full);
            var tree0 = CSharpSyntaxTree.ParseText(original);
            var root0 = await tree0.GetRootAsync();
            var (_, targetNode) = FindNodeByNamePath(root0, namePath);
            if (targetNode is null) return new { success = false, applied = false, error = "symbol_not_found" };

            // Optional preflight: detect references across workspace (naive but syntax-aware)
            if (failOnRefs)
            {
                var lastSeg = (namePath ?? "").Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "";
                if (!string.IsNullOrEmpty(lastSeg))
                {
                    var declContainers = GetTypeContainerChain(targetNode);
                    var refs = new List<object>();
                    foreach (var file in EnumerateUnityCsFiles(_rootDir))
                    {
                        try
                        {
                            var fileHandle = await AcquireFileLockAsync(file);
                            try
                            {
                                var src = await File.ReadAllTextAsync(file);
                                var t = CSharpSyntaxTree.ParseText(src);
                                var r = await t.GetRootAsync();
                                foreach (var id in r.DescendantTokens().Where(tk => tk.IsKind(SyntaxKind.IdentifierToken) && tk.ValueText == lastSeg))
                                {
                                    // ignore identifiers within the target span (same file only)
                                    if (string.Equals(file, full, StringComparison.OrdinalIgnoreCase))
                                    {
                                        var span = targetNode.FullSpan;
                                        var pos = id.SpanStart;
                                        if (pos >= span.Start && pos <= span.End) continue;
                                    }
                                    if (ContainerEndsWith(GetTypeContainerChain(id.Parent), declContainers))
                                    {
                                        var sp = id.GetLocation().GetLineSpan();
                                        refs.Add(new { path = ToRel(file, _rootDir), line = sp.StartLinePosition.Line + 1, column = sp.StartLinePosition.Character + 1 });
                                    }
                                }
                            }
                            finally
                            {
                                fileHandle.Dispose();
                            }
                        }
                        catch { }
                    }
                    if (refs.Count > 0)
                    {
                        return new { success = false, applied = false, references = refs };
                    }
                }
            }

            // Apply removal
            var tree = CSharpSyntaxTree.ParseText(original);
            var root = await tree.GetRootAsync();
            var (_, last) = FindNodeByNamePath(root, namePath);
            if (last is null) return new { success = false, applied = false, error = "symbol_not_found" };
            var newRoot = root.RemoveNode(last, SyntaxRemoveOptions.KeepExteriorTrivia);
            var newText = newRoot?.ToFullString() ?? original;
            if (newText == original)
            {
                return new { success = false, applied = false, error = "no_change" };
            }
            if (apply)
            {
                if (removeEmptyFile && string.IsNullOrWhiteSpace(newText))
                {
                    File.Delete(full);
                    return new { success = true, applied = true, removedFile = true };
                }
                await File.WriteAllTextAsync(full, newText, Encoding.UTF8);
                return new { success = true, applied = true };
            }
            return new { success = true, applied = false, preview = DiffPreview(original, newText) };
            }
            finally
            {
                handle.Dispose();
            }
        }
        catch (Exception ex)
        {
            return new { success = false, applied = false, error = ex.Message };
        }
    }

    private static IEnumerable<string> EnumerateUnityCsFiles(string rootDir)
    {
        IEnumerable<string> EnumDir(string dir)
        {
            if (!Directory.Exists(dir)) yield break;
            foreach (var f in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                var norm = f.Replace('\\','/');
                if (norm.Contains("/obj/") || norm.Contains("/bin/")) continue;
                yield return f;
            }
        }
        foreach (var f in EnumDir(Path.Combine(rootDir, "Assets"))) yield return f;
        foreach (var f in EnumDir(Path.Combine(rootDir, "Packages"))) yield return f;
        foreach (var f in EnumDir(Path.Combine(rootDir, "Library", "PackageCache"))) yield return f;
    }

    private static string Path2Uri(string path)
    {
        return "file://" + path.Replace('\\','/');
    }

    private static string ToRel(string fullPath, string root)
    {
        var normFull = fullPath.Replace('\\', '/');
        var normRoot = root.Replace('\\', '/').TrimEnd('/');
        if (normFull.StartsWith(normRoot, StringComparison.OrdinalIgnoreCase))
            return normFull.Substring(normRoot.Length + 1);
        return normFull;
    }

    private static object MakeSym(string name, int kind, SyntaxNode node)
    {
        var span = node.GetLocation().GetLineSpan();
        var start = new { line = span.StartLinePosition.Line, character = span.StartLinePosition.Character };
        var end = new { line = span.EndLinePosition.Line, character = span.EndLinePosition.Character };
        return new
        {
            name,
            kind,
            range = new { start, end },
            selectionRange = new { start, end }
        };
    }

    private async Task<object> BuildCodeIndexAsync(string? outputPath)
    {
        try
        {
            if (string.IsNullOrEmpty(_rootDir) || !Directory.Exists(_rootDir))
            {
                return new { success = false, error = "root_directory_not_initialized" };
            }

            var entries = new List<CodeIndexEntry>();
            foreach (var file in EnumerateUnityCsFiles(_rootDir))
            {
                try
                {
                    var text = await File.ReadAllTextAsync(file);
                    var tree = CSharpSyntaxTree.ParseText(text);
                    var root = await tree.GetRootAsync();
                    var scope = new Stack<string>();
                    var relative = ToRel(file, _rootDir);
                    CollectSymbols(root, scope, entries, relative);
                }
                catch (Exception ex)
                {
                    entries.Add(new CodeIndexEntry
                    {
                        Name = Path.GetFileName(file),
                        Kind = "file_error",
                        NamePath = Path.GetFileName(file),
                        File = ToRel(file, _rootDir),
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
                Entries = entries.OrderBy(e => e.NamePath, StringComparer.OrdinalIgnoreCase).ToArray()
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

    private void CollectSymbols(SyntaxNode node, Stack<string> scope, List<CodeIndexEntry> output, string relativePath)
    {
        switch (node)
        {
            case FileScopedNamespaceDeclarationSyntax fileNs:
                scope.Push(fileNs.Name.ToString());
                foreach (var member in fileNs.Members)
                {
                    CollectSymbols(member, scope, output, relativePath);
                }
                scope.Pop();
                break;
            case NamespaceDeclarationSyntax ns:
                scope.Push(ns.Name.ToString());
                foreach (var member in ns.Members)
                {
                    CollectSymbols(member, scope, output, relativePath);
                }
                scope.Pop();
                break;
            case ClassDeclarationSyntax cls:
                AddEntry(cls.Identifier.ValueText, "class", cls, scope, output, relativePath);
                scope.Push(cls.Identifier.ValueText);
                foreach (var member in cls.Members)
                {
                    CollectSymbols(member, scope, output, relativePath);
                }
                scope.Pop();
                break;
            case StructDeclarationSyntax st:
                AddEntry(st.Identifier.ValueText, "struct", st, scope, output, relativePath);
                scope.Push(st.Identifier.ValueText);
                foreach (var member in st.Members)
                {
                    CollectSymbols(member, scope, output, relativePath);
                }
                scope.Pop();
                break;
            case InterfaceDeclarationSyntax iface:
                AddEntry(iface.Identifier.ValueText, "interface", iface, scope, output, relativePath);
                scope.Push(iface.Identifier.ValueText);
                foreach (var member in iface.Members)
                {
                    CollectSymbols(member, scope, output, relativePath);
                }
                scope.Pop();
                break;
            case EnumDeclarationSyntax en:
                AddEntry(en.Identifier.ValueText, "enum", en, scope, output, relativePath);
                foreach (var member in en.Members)
                {
                    AddEntry(member.Identifier.ValueText, "enumMember", member, scope, output, relativePath);
                }
                break;
            case MethodDeclarationSyntax method:
                AddEntry(method.Identifier.ValueText, "method", method, scope, output, relativePath);
                break;
            case ConstructorDeclarationSyntax ctor:
                AddEntry(ctor.Identifier.ValueText, "constructor", ctor, scope, output, relativePath);
                break;
            case PropertyDeclarationSyntax prop:
                AddEntry(prop.Identifier.ValueText, "property", prop, scope, output, relativePath);
                break;
            case EventDeclarationSyntax ev:
                AddEntry(ev.Identifier.ValueText, "event", ev, scope, output, relativePath);
                break;
            case EventFieldDeclarationSyntax ef:
                foreach (var variable in ef.Declaration.Variables)
                {
                    AddEntry(variable.Identifier.ValueText, "event", variable, scope, output, relativePath);
                }
                break;
            case FieldDeclarationSyntax field:
                foreach (var variable in field.Declaration.Variables)
                {
                    AddEntry(variable.Identifier.ValueText, "field", variable, scope, output, relativePath);
                }
                break;
            case DelegateDeclarationSyntax del:
                AddEntry(del.Identifier.ValueText, "delegate", del, scope, output, relativePath);
                break;
        }

    }

    private void AddEntry(string name, string kind, SyntaxNode node, Stack<string> scope, List<CodeIndexEntry> output, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(name)) return;

        var span = node.GetLocation().GetLineSpan();
        var line = span.StartLinePosition.Line + 1;
        var column = span.StartLinePosition.Character + 1;
        var namePath = string.Join('.', scope.Reverse().Append(name));

        output.Add(new CodeIndexEntry
        {
            Name = name,
            Kind = kind,
            NamePath = namePath,
            File = relativePath.Replace('\\', '/'),
            Line = line,
            Column = column
        });
    }

    private string ResolveIndexOutputPath(string? outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return Path.Combine(_rootDir, ".mcp", "code-index.json");
        }

        if (Path.IsPathRooted(outputPath))
        {
            return outputPath;
        }

        return Path.Combine(_rootDir, outputPath);
    }

    private async Task<string?> ReadMessageAsync()
    {
        var stdin = Console.OpenStandardInput();
        int contentLength = 0;

        // Read headers as raw bytes until \r\n\r\n
        var headerBytes = new List<byte>(256);
        var one = new byte[1];
        while (true)
        {
            int n = await stdin.ReadAsync(one, 0, 1);
            if (n <= 0) return null;
            headerBytes.Add(one[0]);
            int count = headerBytes.Count;
            if (count >= 4 &&
                headerBytes[count - 4] == (byte)'\r' &&
                headerBytes[count - 3] == (byte)'\n' &&
                headerBytes[count - 2] == (byte)'\r' &&
                headerBytes[count - 1] == (byte)'\n')
            {
                break;
            }
        }

        var headerText = Encoding.ASCII.GetString(headerBytes.ToArray());
        var headerLines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in headerLines)
        {
            var idx = line.IndexOf(":", StringComparison.Ordinal);
            if (idx <= 0) continue;
            var key = line.Substring(0, idx).Trim();
            var val = line.Substring(idx + 1).Trim();
            if (key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(val, out contentLength);
            }
        }

        if (contentLength <= 0) return null;
        var body = new byte[contentLength];
        int read = 0;
        while (read < contentLength)
        {
            int n = await stdin.ReadAsync(body, read, contentLength - read);
            if (n <= 0) break;
            read += n;
        }
        return Encoding.UTF8.GetString(body, 0, read);
    }

    private async Task WriteMessageAsync(object payload)
    {
        var json = JsonSerializer.Serialize(payload, _json);
        var header = $"Content-Length: {Encoding.UTF8.GetByteCount(json)}\r\n\r\n";
        await _writeLock.WaitAsync();
        try
        {
            await Console.Out.WriteAsync(header);
            await Console.Out.WriteAsync(json);
            await Console.Out.FlushAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private sealed class CodeIndexDocument
    {
        public string GeneratedAt { get; set; } = string.Empty;
        public string Root { get; set; } = string.Empty;
        public CodeIndexEntry[] Entries { get; set; } = Array.Empty<CodeIndexEntry>();
    }

    private sealed class CodeIndexEntry
    {
        public string Name { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public string NamePath { get; set; } = string.Empty;
        public string File { get; set; } = string.Empty;
        public int Line { get; set; }
        public int Column { get; set; }
        public string? Summary { get; set; }
    }
}

static class LspLogger
{
    private const string Prefix = "[unity-cli:lsp]";

    public static void Info(string message) =>
        Console.Error.WriteLine($"{Prefix} {message}");

    public static void Error(string message) =>
        Console.Error.WriteLine($"{Prefix} ERROR: {message}");

    public static void Debug(string method) =>
        Console.Error.WriteLine($"{Prefix} {method}");
}
