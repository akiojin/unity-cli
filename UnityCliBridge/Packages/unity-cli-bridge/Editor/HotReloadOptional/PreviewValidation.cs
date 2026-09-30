using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace UnityCliBridge.HotReload
{
    internal sealed class PreviewChange
    {
        public string[] Methods;
        public string InstrumentedSource;
        public string TypeName;
    }

    // Deliberately narrow: unsupported syntax must fail before FSR can touch any method.
    internal static class PreviewValidation
    {
        private static InvalidOperationException Reject(string message, string code = "HOT_RELOAD_UNSUPPORTED_CHANGE")
        {
            var error = new InvalidOperationException(message);
            error.Data["code"] = code;
            return error;
        }
        public static PreviewChange Compare(string baseline, string candidate, string transaction)
        {
            var before = Parse(baseline);
            var after = Parse(candidate);
            if (new RemoveBodies().Visit(before).NormalizeWhitespace().ToFullString() !=
                new RemoveBodies().Visit(after).NormalizeWhitespace().ToFullString())
                throw Reject("Only existing method bodies may change; declarations and fields must remain identical.");
            var oldMethods = before.DescendantNodes().OfType<MethodDeclarationSyntax>().ToDictionary(m => m.Identifier.ValueText);
            var changed = after.DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Where(m => !m.Body.IsEquivalentTo(oldMethods[m.Identifier.ValueText].Body))
                .Select(m => m.Identifier.ValueText).ToArray();
            var instrumented = after.ReplaceNodes(after.DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Where(m => changed.Contains(m.Identifier.ValueText)), (original, rewritten) =>
                rewritten.WithBody(rewritten.Body.WithStatements(rewritten.Body.Statements.Insert(0,
                    SyntaxFactory.ParseStatement("global::UnityCliBridge.HotReload.FastScriptReloadAdapter.Observe(" +
                        SymbolDisplay.FormatLiteral(transaction, true) + ", " + SymbolDisplay.FormatLiteral(original.Identifier.ValueText, true) + ");")))));
            var type = after.DescendantNodes().OfType<ClassDeclarationSyntax>().Single();
            var ns = type.Ancestors().OfType<NamespaceDeclarationSyntax>().Reverse().Select(n => n.Name.ToString());
            return new PreviewChange { Methods = changed, TypeName = string.Join(".", ns.Concat(new[] { type.Identifier.ValueText })),
                InstrumentedSource = instrumented.NormalizeWhitespace().ToFullString() };
        }

        private static CompilationUnitSyntax Parse(string source)
        {
            if (source == null) throw Reject("source is required.");
            var tree = CSharpSyntaxTree.ParseText(source);
            var errors = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
            if (errors.Length != 0) throw Reject(string.Join("\n", errors.Select(d => d.ToString())), "HOT_RELOAD_SYNTAX_ERROR");
            var root = tree.GetCompilationUnitRoot();
            if (root.ContainsDirectives) throw Reject("Preprocessor directives are not supported in preview sessions.");
            var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>().ToArray();
            if (classes.Length != 1 || classes[0].TypeParameterList != null || classes[0].Modifiers.Any(SyntaxKind.PartialKeyword) ||
                root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>().Count() != 1 || root.DescendantNodes().OfType<DelegateDeclarationSyntax>().Any())
                throw Reject("Exactly one non-partial, non-generic class is supported.");
            var type = classes[0];
            if (type.Members.Any(m => !(m is MethodDeclarationSyntax) && !(m is FieldDeclarationSyntax)))
                throw Reject("Only fields and ordinary block-bodied methods are supported (no constructors, properties, events or nested types).");
            var methods = type.Members.OfType<MethodDeclarationSyntax>().ToArray();
            if (methods.GroupBy(m => m.Identifier.ValueText).Any(g => g.Count() != 1))
                throw Reject("Overloaded methods are not supported in preview sessions.");
            if (methods.Any(m => m.Body == null || m.TypeParameterList != null || m.Modifiers.Any(SyntaxKind.AsyncKeyword) ||
                m.Modifiers.Any(SyntaxKind.ExternKeyword) || m.Modifiers.Any(SyntaxKind.AbstractKeyword) ||
                m.Identifier.ValueText.StartsWith("OnScriptHotReload", StringComparison.Ordinal)) ||
                root.DescendantNodes().Any(n => n is YieldStatementSyntax || n is LocalFunctionStatementSyntax || n is AnonymousFunctionExpressionSyntax))
                throw Reject("Async, iterator, local/anonymous functions, generic methods and FSR callbacks are unsupported.");
            return root.ReplaceTokens(root.DescendantTokens(), (original, rewritten) => rewritten.WithoutTrivia()).WithoutTrivia();
        }

        private sealed class RemoveBodies : CSharpSyntaxRewriter
        {
            public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node) => node.WithBody(SyntaxFactory.Block());
        }
    }

    internal sealed class ExecutionObservations
    {
        private readonly string transaction;
        private readonly string[] methods;
        private readonly HashSet<string> observed = new HashSet<string>(StringComparer.Ordinal);
        public ExecutionObservations(string transaction, string[] methods) { this.transaction = transaction; this.methods = methods; }
        public void Observe(string transaction, string method)
        {
            lock (observed) if (this.transaction == transaction && methods.Contains(method)) observed.Add(method);
        }
        public string[] Observed() { lock (observed) return methods.Where(observed.Contains).ToArray(); }
        public string[] Unverified() { lock (observed) return methods.Where(m => !observed.Contains(m)).ToArray(); }
    }
}
namespace UnityCliBridge.HotReload
{
    internal static class CandidateFields
    {
        // Same value as FSR's AssemblyChangesLoader.ClassnamePatchedPostfix; kept here so tests need no FSR reference.
        public const string GeneratedPrefix = "__Patched_";
        public static bool Match(IEnumerable<string> compiled, IEnumerable<string> candidate) =>
            new HashSet<string>(compiled, StringComparer.Ordinal).SetEquals(
                candidate.Where(name => !name.StartsWith(GeneratedPrefix, StringComparison.Ordinal)));
    }

    internal static class CompilationProof
    {
        public static bool CanCommit(bool explicitCleanBuild, ISet<string> successfulAssemblies, string assemblyName) =>
            explicitCleanBuild || successfulAssemblies.Contains(assemblyName);
        public static Dictionary<string, string> Capture(IEnumerable<string> paths) => paths.Select(System.IO.Path.GetFullPath)
            .Distinct(StringComparer.Ordinal).ToDictionary(path => path, HashFile, StringComparer.Ordinal);
        public static string HashFile(string path)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var stream = System.IO.File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        public static bool Matches(Dictionary<string, string> proof, string source, string assembly, string hash) =>
            proof != null && proof.TryGetValue(System.IO.Path.GetFullPath(source), out var baseline) &&
            System.IO.File.Exists(source) && System.IO.File.Exists(assembly) && baseline == HashFile(source) && hash == HashFile(assembly);
    }
}
