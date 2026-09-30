using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace UnityCliBridge.Evaluation
{
    internal static class EvalCompiler
    {
        private sealed class Compiled
        {
            public MethodInfo Run;
            public JArray Diagnostics;
        }

        private static readonly object Gate = new object();
        // One MetadataReference per file for the whole domain: Roslyn keeps the parsed metadata on the reference object.
        private static readonly Dictionary<string, MetadataReference> ReferencesByPath = new Dictionary<string, MetadataReference>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Compiled> CompiledBySource = new Dictionary<string, Compiled>(StringComparer.Ordinal);
        private static MetadataReference[] references;
        private static int referencesVersion = -1;
        private static int loadVersion;
        private static int referenceBuilds;
        private static int cacheHits;
        private static volatile bool warmedUp;

        static EvalCompiler()
        {
            // A file-backed assembly loaded later in this domain must become resolvable. Eval's own assemblies are loaded from bytes.
            AppDomain.CurrentDomain.AssemblyLoad += (sender, args) =>
            {
                if (!args.LoadedAssembly.IsDynamic && !string.IsNullOrEmpty(LocationOf(args.LoadedAssembly))) Interlocked.Increment(ref loadVersion);
            };
        }

        internal static bool WarmedUp => warmedUp;
        internal static int ReferenceBuilds => referenceBuilds;
        internal static int CacheHits => cacheHits;
        internal static int ReferenceCount { get { lock (Gate) return references?.Length ?? 0; } }
        internal static int CachedCompilations { get { lock (Gate) return CompiledBySource.Count; } }

        internal static bool IsCached(string code, string mode)
        {
            lock (Gate) return Volatile.Read(ref loadVersion) == referencesVersion && CompiledBySource.ContainsKey(mode + "\n" + code);
        }

        [InitializeOnLoadMethod]
        private static void ScheduleWarmUp()
        {
            EditorApplication.update += WarmUpWhenIdle;
        }

        // update keeps ticking while the Editor is in the background; delayCall can wait for focus.
        private static void WarmUpWhenIdle()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            EditorApplication.update -= WarmUpWhenIdle;
            // Pay Roslyn's first-use cost (JIT and reference metadata) off the main thread.
            Task.Run(() =>
            {
                try
                {
                    using (var stream = new MemoryStream()) Compile(Parse("1+2", "expression"), CurrentReferences(out _)).Emit(stream);
                    warmedUp = true;
                }
                catch (Exception) { /* Warm-up is an optimization; evaluation reports real failures. */ }
            });
        }

        private static string LocationOf(Assembly assembly)
        {
            try { return assembly.Location; }
            catch (NotSupportedException) { return null; }
        }

        private static MetadataReference[] CurrentReferences(out int version)
        {
            lock (Gate)
            {
                version = Volatile.Read(ref loadVersion);
                if (references != null && version == referencesVersion) return references;
                var byName = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly.IsDynamic) continue;
                    var location = LocationOf(assembly);
                    if (string.IsNullOrEmpty(location) || !File.Exists(location)) continue;
                    var name = assembly.GetName().Name;
                    if (byName.ContainsKey(name)) continue;
                    if (!ReferencesByPath.TryGetValue(location, out var reference)) ReferencesByPath[location] = reference = MetadataReference.CreateFromFile(location);
                    byName.Add(name, reference);
                }
                references = byName.Values.ToArray();
                referencesVersion = version;
                // A snippet compiled against the previous set may bind differently now.
                CompiledBySource.Clear();
                referenceBuilds++;
                return references;
            }
        }

        private static SyntaxTree Parse(string code, string mode)
        {
            var body = mode == "expression" ? "return (object)(\n#line 1 \"eval.cs\"\n" + code + "\n#line default\n);" : "#line 1 \"eval.cs\"\n" + code + "\n#line default\nreturn null;";
            return CSharpSyntaxTree.ParseText("using System; using UnityEngine; using UnityEditor;\npublic static class EvalSnippet { public static object Run() {\n" + body + "\n} }", new CSharpParseOptions(LanguageVersion.CSharp9));
        }

        private static CSharpCompilation Compile(SyntaxTree tree, MetadataReference[] compilationReferences)
        {
            return CSharpCompilation.Create("UnityCliEval_" + Guid.NewGuid().ToString("N"), new[] { tree }, compilationReferences,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        }

        internal static void Run(string code, string mode, JObject result, Action emitted)
        {
            var key = mode + "\n" + code;
            var compilationReferences = CurrentReferences(out var version);
            Compiled compiled;
            lock (Gate) CompiledBySource.TryGetValue(key, out compiled);
            if (compiled != null) Interlocked.Increment(ref cacheHits);
            else
            {
                var tree = Parse(code, mode);
                if (tree.GetRoot().DescendantNodes().Any(n => n is AwaitExpressionSyntax
                    || n is AnonymousFunctionExpressionSyntax function && function.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword)
                    || n is LocalFunctionStatementSyntax local && local.Modifiers.Any(SyntaxKind.AsyncKeyword)))
                {
                    result["state"] = "unsupported";
                    result["exception"] = new JObject { ["message"] = "Async/await evaluation is not supported." };
                    return;
                }
                using (var stream = new MemoryStream())
                {
                    var emit = Compile(tree, compilationReferences).Emit(stream);
                    var diagnostics = new JArray();
                    foreach (var diagnostic in emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error || d.Severity == DiagnosticSeverity.Warning).Take(128))
                    {
                        var span = diagnostic.Location.GetMappedLineSpan();
                        diagnostics.Add(new JObject { ["id"] = diagnostic.Id, ["severity"] = diagnostic.Severity.ToString().ToLowerInvariant(), ["message"] = EvalResult.Limit(diagnostic.GetMessage()), ["line"] = span.StartLinePosition.Line + 1, ["column"] = span.StartLinePosition.Character + 1 });
                    }
                    if (!emit.Success) { result["diagnostics"] = diagnostics; result["state"] = "compile_error"; return; }
                    emitted();
                    compiled = new Compiled { Run = Assembly.Load(stream.ToArray()).GetType("EvalSnippet").GetMethod("Run"), Diagnostics = diagnostics };
                }
                // Loaded assemblies cannot be unloaded, so identical source reuses this one for the rest of the domain.
                lock (Gate) if (version == referencesVersion && version == Volatile.Read(ref loadVersion)) CompiledBySource[key] = compiled;
            }
            result["diagnostics"] = compiled.Diagnostics.DeepClone();
            object value;
            try { value = compiled.Run.Invoke(null, null); }
            catch (TargetInvocationException ex) { EvalResult.Fail(result, "runtime_error", ex.InnerException ?? ex); return; }
            if (value is Task || (value != null && value.GetType().FullName.StartsWith("System.Threading.Tasks.ValueTask", StringComparison.Ordinal)))
            {
                result["state"] = "unsupported";
                result["exception"] = new JObject { ["message"] = "Task values are not supported; evaluation is synchronous." };
                return;
            }
            try
            {
                result["value"] = EvalResultSerializer.Serialize(value);
                result["state"] = "completed";
            }
            catch (Exception ex) { EvalResult.Fail(result, "serialization_error", ex); }
        }
    }
}
