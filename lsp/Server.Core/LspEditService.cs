using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UnityCli.Lsp.Core;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed class LspEditService
{
    private readonly string _rootDir;
    private readonly LspFileLockProvider _fileLockProvider;

    public LspEditService(string rootDir, LspFileLockProvider fileLockProvider)
    {
        _rootDir = rootDir;
        _fileLockProvider = fileLockProvider;
    }

    private Task<IDisposable> AcquireFileLockAsync(string path) =>
        _fileLockProvider.AcquireAsync(path);

    public async Task<object> RenameByNamePathAsync(string relative, string namePath, string newName, bool apply)
    {
        var full = Path.Combine(_rootDir, relative.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(full)) return EditResult(false, false, reason: "file_not_found");
        try
        {
            var handle = await AcquireFileLockAsync(full);
            try
            {
            var text = await File.ReadAllTextAsync(full);
            var tree = CSharpSyntaxTree.ParseText(text);
            var root = await tree.GetRootAsync();
            var normalizedNamePath = NormalizeNamePath(namePath);
            var segments = normalizedNamePath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length == 0) return EditResult(false, false, reason: "invalid_namePath");
            SyntaxNode cursor = root;
            for (int i = 0; i < segments.Length - 1; i++)
            {
                var seg = segments[i];
                var next = cursor.DescendantNodes().FirstOrDefault(n => n is ClassDeclarationSyntax c && c.Identifier.ValueText == seg
                                                                      || n is StructDeclarationSyntax s && s.Identifier.ValueText == seg
                                                                      || n is InterfaceDeclarationSyntax ii && ii.Identifier.ValueText == seg
                                                                      || n is EnumDeclarationSyntax en && en.Identifier.ValueText == seg);
                if (next is null) return EditResult(false, false, reason: "container_not_found");
                cursor = next;
            }
            var targetName = segments[^1];
            if (string.Equals(targetName, newName, StringComparison.Ordinal))
            {
                return EditResult(false, false, reason: "no_change");
            }
            SyntaxNode? decl = cursor.DescendantNodes().FirstOrDefault(n => n is ClassDeclarationSyntax c && c.Identifier.ValueText == targetName
                                                                          || n is StructDeclarationSyntax s && s.Identifier.ValueText == targetName
                                                                          || n is InterfaceDeclarationSyntax ii && ii.Identifier.ValueText == targetName
                                                                          || n is EnumDeclarationSyntax en && en.Identifier.ValueText == targetName)
                             ?? cursor.DescendantNodes().FirstOrDefault(n => n is MethodDeclarationSyntax m && m.Identifier.ValueText == targetName
                                                                          || n is PropertyDeclarationSyntax p && p.Identifier.ValueText == targetName
                                                                          || n is FieldDeclarationSyntax f && f.Declaration.Variables.Any(v => v.Identifier.ValueText == targetName));
            if (decl is null) return EditResult(false, false, reason: "symbol_not_found");

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
                return EditResult(false, false, reason: "name_conflict");
            }

            // Extend rename: if type/member, update identifier usages across workspace within matching containers
            bool isTypeDecl = decl is ClassDeclarationSyntax || decl is StructDeclarationSyntax || decl is InterfaceDeclarationSyntax || decl is EnumDeclarationSyntax;
            bool isMemberDecl = decl is MethodDeclarationSyntax || decl is PropertyDeclarationSyntax || decl is FieldDeclarationSyntax;
            var newSymbolPath = string.Join('/', segments.Take(segments.Length - 1).Append(newName));
            if (!(isTypeDecl || isMemberDecl))
            {
                var newText = newRoot.ToFullString();
                if (newText == text) return EditResult(false, false, reason: "no_change");
                if (apply)
                {
                    await File.WriteAllTextAsync(full, newText, Encoding.UTF8);
                    return EditResult(
                        true,
                        true,
                        changedFiles: new[] { NormalizeRelative(relative) },
                        changedSymbols: new[] { NormalizeNamePath(newSymbolPath) });
                }
                return EditResult(
                    true,
                    false,
                    changedFiles: new[] { NormalizeRelative(relative) },
                    changedSymbols: new[] { NormalizeNamePath(newSymbolPath) },
                    diffPreview: BuildDiffPreviewEntries(new[] { (relative, text, newText) }),
                    reason: "preview_only");
            }

            var containers = segments.Take(segments.Length - 1).ToArray();
            var nsTarget = GetNamespaceChain(decl);
            var oldName = targetName;
            var rewrittenFullText = newRoot.ToFullString();
            var updatedFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [full] = rewrittenFullText };
            var originalByFile = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [full] = text };
            void TrackRenameChanges(string file, string originalSrc, SyntaxNode currentRoot)
            {
                var tokens = currentRoot.DescendantTokens().Where(tk => tk.IsKind(SyntaxKind.IdentifierToken) && tk.ValueText == oldName).ToArray();
                if (tokens.Length == 0) return;
                var tokensToReplace = new List<SyntaxToken>();
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
                        tokensToReplace.Add(tk);
                    }
                    else if (isMemberDecl)
                    {
                        if (tk.Parent is IdentifierNameSyntax)
                        {
                            tokensToReplace.Add(tk);
                        }
                    }
                }
                if (tokensToReplace.Count > 0)
                {
                    var rr = currentRoot.ReplaceTokens(
                        tokensToReplace,
                        (token, _) => SyntaxFactory.Identifier(newName).WithTriviaFrom(token));
                    updatedFiles[file] = rr.ToFullString();
                    originalByFile[file] = originalSrc;
                }
            }

            TrackRenameChanges(full, text, await CSharpSyntaxTree.ParseText(rewrittenFullText).GetRootAsync());
            foreach (var file in EnumerateUnityCsFiles(_rootDir))
            {
                // Member rename is limited to the declaration file; type rename updates across workspace
                if (isMemberDecl && !string.Equals(file, full, StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(file, full, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var fileHandle = await AcquireFileLockAsync(file);
                    try
                    {
                        var src = await File.ReadAllTextAsync(file);
                        var t = CSharpSyntaxTree.ParseText(src);
                        var r = await t.GetRootAsync();
                        TrackRenameChanges(file, src, r);
                    }
                    finally
                    {
                        fileHandle.Dispose();
                    }
                }
                catch { }
            }
            var changed = updatedFiles
                .Where(kv => !string.Equals(originalByFile[kv.Key], kv.Value, StringComparison.Ordinal))
                .ToArray();
            if (changed.Length == 0)
            {
                return EditResult(false, false, reason: "no_change");
            }
            var changedFiles = changed.Select(kv => NormalizeRelative(ToRel(kv.Key, _rootDir))).ToArray();
            if (apply)
            {
                var filesToLock = updatedFiles.Keys
                    .Where(file => !string.Equals(file, full, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
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
                return EditResult(
                    true,
                    true,
                    changedFiles: changedFiles,
                    changedSymbols: new[] { NormalizeNamePath(newSymbolPath) });
            }
            return EditResult(
                true,
                false,
                changedFiles: changedFiles,
                changedSymbols: new[] { NormalizeNamePath(newSymbolPath) },
                diffPreview: BuildDiffPreviewEntries(changed.Select(kv => (
                    ToRel(kv.Key, _rootDir),
                    originalByFile[kv.Key],
                    kv.Value
                ))),
                reason: "preview_only");

            }
            finally
            {
                handle.Dispose();
            }
        }
        catch (Exception ex)
        {
            return EditResult(false, false, reason: ex.Message);
        }
    }

    private static string DiffPreview(string oldText, string newText)
        => LspEditResultFactory.DiffPreview(oldText, newText);

    private static string NormalizeRelative(string relative)
        => LspPathUtilities.NormalizeRelative(relative);

    private static string MaybeFormatText(string text, bool format)
        => LspEditResultFactory.MaybeFormatText(text, format);

    private static List<Dictionary<string, object?>> CollectSyntaxDiagnostics(string text)
        => LspEditResultFactory.CollectSyntaxDiagnostics(text);

    private static bool HasErrorDiagnostics(IEnumerable<Dictionary<string, object?>> diagnostics)
        => LspEditResultFactory.HasErrorDiagnostics(diagnostics);

    private static object[] BuildDiffPreviewEntries(IEnumerable<(string path, string originalText, string newText)> changes)
        => LspEditResultFactory.BuildDiffPreviewEntries(changes);

    private static Dictionary<string, object?> EditResult(
        bool success,
        bool applied,
        IEnumerable<string>? changedFiles = null,
        IEnumerable<string>? changedSymbols = null,
        IEnumerable<object>? diagnostics = null,
        IEnumerable<object>? diffPreview = null,
        string? reason = null)
        => LspEditResultFactory.EditResult(success, applied, changedFiles, changedSymbols, diagnostics, diffPreview, reason);

    public async Task<object> ValidateTextEditsAsync(string relative, string newText)
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
            return new { diagnostics = CollectSyntaxDiagnostics(text) };
        }
        catch (Exception ex)
        {
            var errorList = new List<object>
            {
                new Dictionary<string, object?>
                {
                    ["severity"] = "error",
                    ["id"] = "validateTextEdits",
                    ["message"] = ex.Message,
                    ["line"] = 0,
                    ["column"] = 0
                }
            };
            return new
            {
                diagnostics = errorList,
                error = ex.Message
            };
        }
    }

    public async Task<object> WriteCSharpFileAsync(string relative, string newText, bool validate, bool apply, bool format)
    {
        var full = Path.Combine(_rootDir, relative.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(full)) return EditResult(false, false, reason: "file_not_found");

        var handle = await AcquireFileLockAsync(full);
        try
        {
            var original = await File.ReadAllTextAsync(full);
            var updated = MaybeFormatText(newText ?? string.Empty, format);
            var diagnostics = validate ? CollectSyntaxDiagnostics(updated).Cast<object>().ToArray() : Array.Empty<object>();
            if (validate && HasErrorDiagnostics(diagnostics.Cast<Dictionary<string, object?>>()))
            {
                return EditResult(false, false, diagnostics: diagnostics, reason: "validation_failed");
            }
            if (updated == original)
            {
                return EditResult(false, false, diagnostics: diagnostics, reason: "no_change");
            }
            if (!apply)
            {
                return EditResult(
                    true,
                    false,
                    changedFiles: new[] { NormalizeRelative(relative) },
                    diagnostics: diagnostics,
                    diffPreview: BuildDiffPreviewEntries(new[] { (relative, original, updated) }),
                    reason: "preview_only");
            }

            await File.WriteAllTextAsync(full, updated, Encoding.UTF8);
            return EditResult(
                true,
                true,
                changedFiles: new[] { NormalizeRelative(relative) },
                diagnostics: diagnostics);
        }
        finally
        {
            handle.Dispose();
        }
    }

    public async Task<object> CreateCSharpFileAsync(string relative, string text, bool overwrite, bool validate, bool apply, bool format)
    {
        var full = Path.Combine(_rootDir, relative.Replace('/', Path.DirectorySeparatorChar));
        var exists = File.Exists(full);
        if (exists && !overwrite) return EditResult(false, false, reason: "file_exists");

        var original = exists ? await File.ReadAllTextAsync(full) : string.Empty;
        var normalizedText = MaybeFormatText(text ?? string.Empty, format);
        var diagnostics = validate ? CollectSyntaxDiagnostics(normalizedText).Cast<object>().ToArray() : Array.Empty<object>();
        if (validate && HasErrorDiagnostics(diagnostics.Cast<Dictionary<string, object?>>()))
        {
            return EditResult(false, false, diagnostics: diagnostics, reason: "validation_failed");
        }
        if (exists && original == normalizedText)
        {
            return EditResult(false, false, diagnostics: diagnostics, reason: "no_change");
        }
        if (!apply)
        {
            return EditResult(
                    true,
                    false,
                    changedFiles: new[] { NormalizeRelative(relative) },
                    diagnostics: diagnostics,
                    diffPreview: BuildDiffPreviewEntries(new[] { (relative, original, normalizedText) }),
                    reason: "preview_only");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full, normalizedText, Encoding.UTF8);
        return EditResult(
            true,
            true,
            changedFiles: new[] { NormalizeRelative(relative) },
            diagnostics: diagnostics);
    }

    public async Task<object> ApplyCSharpEditsAsync(JsonElement files, bool validate, bool apply, bool format)
    {
        var desired = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var originals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var diagnostics = new List<object>();

        foreach (var item in files.EnumerateArray())
        {
            var relative = item.GetProperty("relative").GetString() ?? "";
            var newText = item.GetProperty("newText").GetString() ?? "";
            var full = Path.Combine(_rootDir, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(full))
            {
                return EditResult(false, false, reason: "file_not_found");
            }
            if (desired.ContainsKey(full))
            {
                return EditResult(false, false, reason: "duplicate_path");
            }

            originals[full] = await File.ReadAllTextAsync(full);
            var formatted = MaybeFormatText(newText, format);
            desired[full] = formatted;
            if (validate)
            {
                diagnostics.AddRange(CollectSyntaxDiagnostics(formatted));
            }
        }

        if (validate && HasErrorDiagnostics(diagnostics.Cast<Dictionary<string, object?>>()))
        {
            return EditResult(false, false, diagnostics: diagnostics, reason: "validation_failed");
        }

        var changed = desired
            .Where(kv => !string.Equals(originals[kv.Key], kv.Value, StringComparison.Ordinal))
            .Select(kv => kv.Key)
            .ToArray();
        if (changed.Length == 0)
        {
            return EditResult(false, false, diagnostics: diagnostics, reason: "no_change");
        }

        var changedFiles = changed.Select(path => NormalizeRelative(ToRel(path, _rootDir))).ToArray();
        var diffPreview = BuildDiffPreviewEntries(changed.Select(path => (
            ToRel(path, _rootDir),
            originals[path],
            desired[path]
        )));

        if (!apply)
        {
            return EditResult(
                true,
                false,
                changedFiles: changedFiles,
                diagnostics: diagnostics,
                diffPreview: diffPreview,
                reason: "preview_only");
        }

        var locks = new List<IDisposable>(changed.Length);
        try
        {
            foreach (var file in changed.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                locks.Add(await AcquireFileLockAsync(file));
            }
            foreach (var file in changed)
            {
                await File.WriteAllTextAsync(file, desired[file], Encoding.UTF8);
            }
        }
        finally
        {
            foreach (var entry in locks) entry.Dispose();
        }

        return EditResult(true, true, changedFiles: changedFiles, diagnostics: diagnostics);
    }

    public async Task<object> ReplaceSymbolBodyAsync(string relative, string namePath, string bodyText, bool apply)
    {
        var full = Path.Combine(_rootDir, relative.Replace('/', Path.DirectorySeparatorChar));
        var normalizedPath = NormalizeNamePath(namePath);
        if (!File.Exists(full)) return EditResult(false, false, reason: "file_not_found");
        var handle = await AcquireFileLockAsync(full);
        try
        {
            var text = await File.ReadAllTextAsync(full);
            var tree = CSharpSyntaxTree.ParseText(text);
            var root = await tree.GetRootAsync();
            var (_, last) = FindNodeByNamePath(root, normalizedPath);
            if (last is not MethodDeclarationSyntax method) return EditResult(false, false, reason: "symbol_not_found");
            var block = ParseBlock(bodyText);
            // handle expression-bodied to block conversion
            var m2 = method.WithExpressionBody(null).WithSemicolonToken(default).WithBody(block);
            var newRoot = root.ReplaceNode(method, m2);
            var newText = newRoot.ToFullString();
            if (newText == text) return EditResult(false, false, reason: "no_change");
            if (apply)
            {
                await File.WriteAllTextAsync(full, newText, Encoding.UTF8);
                return EditResult(
                    true,
                    true,
                    changedFiles: new[] { NormalizeRelative(relative) },
                    changedSymbols: new[] { normalizedPath });
            }
            return EditResult(
                true,
                false,
                changedFiles: new[] { NormalizeRelative(relative) },
                changedSymbols: new[] { normalizedPath },
                diffPreview: BuildDiffPreviewEntries(new[] { (relative, text, newText) }),
                reason: "preview_only");
        }
        finally
        {
            handle.Dispose();
        }
    }

    public async Task<object> InsertAroundSymbolAsync(string relative, string namePath, string textToInsert, bool after, bool apply)
    {
        var full = Path.Combine(_rootDir, relative.Replace('/', Path.DirectorySeparatorChar));
        var normalizedPath = NormalizeNamePath(namePath);
        if (!File.Exists(full)) return EditResult(false, false, reason: "file_not_found");
        var handle = await AcquireFileLockAsync(full);
        try
        {
            var original = await File.ReadAllTextAsync(full);
            var tree = CSharpSyntaxTree.ParseText(original);
            var root = await tree.GetRootAsync();
            var (_, last) = FindNodeByNamePath(root, normalizedPath);
            if (last is null) return EditResult(false, false, reason: "symbol_not_found");
            // insert members at class/namespace level using Roslyn API
            var member = SyntaxFactory.ParseMemberDeclaration(textToInsert);
            if (member is null)
            {
                // fallback to textual insertion
                var pos = after ? last.FullSpan.End : last.FullSpan.Start;
                var newText0 = original.Substring(0, pos) + textToInsert + original.Substring(pos);
                if (newText0 == original) return EditResult(false, false, reason: "no_change");
                if (apply)
                {
                    await File.WriteAllTextAsync(full, newText0, Encoding.UTF8);
                    return EditResult(
                        true,
                        true,
                        changedFiles: new[] { NormalizeRelative(relative) },
                        changedSymbols: new[] { normalizedPath });
                }
                return EditResult(
                    true,
                    false,
                    changedFiles: new[] { NormalizeRelative(relative) },
                    changedSymbols: new[] { normalizedPath },
                    diffPreview: BuildDiffPreviewEntries(new[] { (relative, original, newText0) }),
                    reason: "preview_only");
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
                if (newText1 == original) return EditResult(false, false, reason: "no_change");
                if (apply)
                {
                    await File.WriteAllTextAsync(full, newText1, Encoding.UTF8);
                    return EditResult(
                        true,
                        true,
                        changedFiles: new[] { NormalizeRelative(relative) },
                        changedSymbols: new[] { normalizedPath });
                }
                return EditResult(
                    true,
                    false,
                    changedFiles: new[] { NormalizeRelative(relative) },
                    changedSymbols: new[] { normalizedPath },
                    diffPreview: BuildDiffPreviewEntries(new[] { (relative, original, newText1) }),
                    reason: "preview_only");
            }
            var newText = newRoot.ToFullString();
            if (newText == original) return EditResult(false, false, reason: "no_change");
            if (apply)
            {
                await File.WriteAllTextAsync(full, newText, Encoding.UTF8);
                return EditResult(
                    true,
                    true,
                    changedFiles: new[] { NormalizeRelative(relative) },
                    changedSymbols: new[] { normalizedPath });
            }
            return EditResult(
                true,
                false,
                changedFiles: new[] { NormalizeRelative(relative) },
                changedSymbols: new[] { normalizedPath },
                diffPreview: BuildDiffPreviewEntries(new[] { (relative, original, newText) }),
                reason: "preview_only");
        }
        finally
        {
            handle.Dispose();
        }
    }

    private static (SyntaxNode cursor, SyntaxNode? last) FindNodeByNamePath(SyntaxNode root, string namePath)
    {
        var segs = NormalizeNamePath(namePath)
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segs.Length == 0) return (root, null);

        SyntaxNode cursor = root;
        for (int i = 0; i < segs.Length; i++)
        {
            var seg = segs[i];
            var candidates = FindNamePathCandidates(cursor, seg, i == 0);
            if (candidates.Count != 1)
            {
                return (cursor, null);
            }
            cursor = candidates[0];
        }
        return (cursor, cursor);
    }

    private static List<SyntaxNode> FindNamePathCandidates(SyntaxNode cursor, string segment, bool topLevel)
    {
        if (topLevel)
        {
            return cursor
                .DescendantNodes()
                .Where(node =>
                    IsNamedTypeDeclaration(node, segment)
                    && node.Parent is CompilationUnitSyntax or NamespaceDeclarationSyntax or FileScopedNamespaceDeclarationSyntax)
                .Cast<SyntaxNode>()
                .ToList();
        }

        var matches = new List<SyntaxNode>();
        if (cursor is TypeDeclarationSyntax typeDecl)
        {
            foreach (var member in typeDecl.Members)
            {
                if (IsNamedTypeDeclaration(member, segment))
                {
                    matches.Add(member);
                    continue;
                }
                if (member is MethodDeclarationSyntax method && method.Identifier.ValueText == segment)
                {
                    matches.Add(method);
                    continue;
                }
                if (member is ConstructorDeclarationSyntax ctor && ctor.Identifier.ValueText == segment)
                {
                    matches.Add(ctor);
                    continue;
                }
                if (member is PropertyDeclarationSyntax property && property.Identifier.ValueText == segment)
                {
                    matches.Add(property);
                    continue;
                }
                if (member is FieldDeclarationSyntax field && field.Declaration.Variables.Any(variable => variable.Identifier.ValueText == segment))
                {
                    matches.Add(field);
                }
            }
        }
        return matches;
    }

    private static bool IsNamedTypeDeclaration(SyntaxNode node, string segment)
    {
        return node switch
        {
            ClassDeclarationSyntax cls => cls.Identifier.ValueText == segment,
            StructDeclarationSyntax st => st.Identifier.ValueText == segment,
            InterfaceDeclarationSyntax iface => iface.Identifier.ValueText == segment,
            EnumDeclarationSyntax en => en.Identifier.ValueText == segment,
            _ => false
        };
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

    public async Task<object> RemoveSymbolAsync(string relative, string namePath, bool apply, bool failOnRefs, bool removeEmptyFile)
    {
        var full = Path.Combine(_rootDir, relative.Replace('/', Path.DirectorySeparatorChar));
        var normalizedPath = NormalizeNamePath(namePath);
        if (!File.Exists(full)) return EditResult(false, false, reason: "file_not_found");
        try
        {
            var handle = await AcquireFileLockAsync(full);
            try
            {
            // Locate target declaration first
            var original = await File.ReadAllTextAsync(full);
            var tree0 = CSharpSyntaxTree.ParseText(original);
            var root0 = await tree0.GetRootAsync();
            var (_, targetNode) = FindNodeByNamePath(root0, normalizedPath);
            if (targetNode is null) return EditResult(false, false, reason: "symbol_not_found");

            // Optional preflight: detect references across workspace (naive but syntax-aware)
            if (failOnRefs)
            {
                var lastSeg = NormalizeNamePath(normalizedPath).Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "";
                if (!string.IsNullOrEmpty(lastSeg))
                {
                    var declContainers = GetTypeContainerChain(targetNode);
                    var refs = new List<object>();
                    void CollectReferences(string file, SyntaxNode syntaxRoot)
                    {
                        foreach (var id in syntaxRoot.DescendantTokens().Where(tk => tk.IsKind(SyntaxKind.IdentifierToken) && tk.ValueText == lastSeg))
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
                    foreach (var file in EnumerateUnityCsFiles(_rootDir))
                    {
                        try
                        {
                            if (string.Equals(file, full, StringComparison.OrdinalIgnoreCase))
                            {
                                CollectReferences(file, root0);
                                continue;
                            }
                            var fileHandle = await AcquireFileLockAsync(file);
                            try
                            {
                                var src = await File.ReadAllTextAsync(file);
                                var t = CSharpSyntaxTree.ParseText(src);
                                var r = await t.GetRootAsync();
                                CollectReferences(file, r);
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
                        return EditResult(false, false, diagnostics: refs, reason: "references_found");
                    }
                }
            }

            // Apply removal
            var tree = CSharpSyntaxTree.ParseText(original);
            var root = await tree.GetRootAsync();
            var (_, last) = FindNodeByNamePath(root, normalizedPath);
            if (last is null) return EditResult(false, false, reason: "symbol_not_found");
            var newRoot = root.RemoveNode(last, SyntaxRemoveOptions.KeepExteriorTrivia);
            var newText = newRoot?.ToFullString() ?? original;
            if (newText == original)
            {
                return EditResult(false, false, reason: "no_change");
            }
            if (apply)
            {
                if (removeEmptyFile && string.IsNullOrWhiteSpace(newText))
                {
                    File.Delete(full);
                    return EditResult(
                        true,
                        true,
                        changedFiles: new[] { NormalizeRelative(relative) },
                        changedSymbols: new[] { normalizedPath });
                }
                await File.WriteAllTextAsync(full, newText, Encoding.UTF8);
                return EditResult(
                    true,
                    true,
                    changedFiles: new[] { NormalizeRelative(relative) },
                    changedSymbols: new[] { normalizedPath });
            }
            return EditResult(
                true,
                false,
                changedFiles: new[] { NormalizeRelative(relative) },
                changedSymbols: new[] { normalizedPath },
                diffPreview: BuildDiffPreviewEntries(new[] { (relative, original, newText) }),
                reason: "preview_only");
            }
            finally
            {
                handle.Dispose();
            }
        }
        catch (Exception ex)
        {
            return EditResult(false, false, reason: ex.Message);
        }
    }


    private static IEnumerable<string> EnumerateUnityCsFiles(string rootDir)
        => LspWorkspaceUtilities.EnumerateUnityCsFiles(rootDir);

    private static string ToRel(string fullPath, string root)
        => LspPathUtilities.ToRel(fullPath, root);

    private static string NormalizeNamePath(string raw)
        => LspPathUtilities.NormalizeNamePath(raw);
}
