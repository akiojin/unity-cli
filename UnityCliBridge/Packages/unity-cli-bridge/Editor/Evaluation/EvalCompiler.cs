using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Newtonsoft.Json.Linq;

namespace UnityCliBridge.Evaluation
{
    internal static class EvalCompiler
    {
        internal static void Run(string code, string mode, JObject result, Action emitted)
        {
            var body = mode == "expression" ? "return (object)(\n#line 1 \"eval.cs\"\n" + code + "\n#line default\n);" : "#line 1 \"eval.cs\"\n" + code + "\n#line default\nreturn null;";
            var tree = CSharpSyntaxTree.ParseText("using System; using UnityEngine; using UnityEditor;\npublic static class EvalSnippet { public static object Run() {\n" + body + "\n} }", new CSharpParseOptions(LanguageVersion.CSharp9));
            if (tree.GetRoot().DescendantNodes().Any(n => n is AwaitExpressionSyntax
                || n is AnonymousFunctionExpressionSyntax function && function.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword)
                || n is LocalFunctionStatementSyntax local && local.Modifiers.Any(SyntaxKind.AsyncKeyword)))
            {
                result["state"] = "unsupported";
                result["exception"] = new JObject { ["message"] = "Async/await evaluation is not supported." };
                return;
            }
            var references = new Dictionary<string, MetadataReference>(StringComparer.OrdinalIgnoreCase);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic) continue;
                string location;
                try { location = assembly.Location; }
                catch (NotSupportedException) { continue; }
                if (string.IsNullOrEmpty(location) || !File.Exists(location)) continue;
                if (!references.ContainsKey(assembly.GetName().Name)) references.Add(assembly.GetName().Name, MetadataReference.CreateFromFile(location));
            }
            var compilation = CSharpCompilation.Create("UnityCliEval_" + Guid.NewGuid().ToString("N"), new[] { tree }, references.Values,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
            using (var stream = new MemoryStream())
            {
                var emit = compilation.Emit(stream);
                var diagnostics = (JArray)result["diagnostics"];
                foreach (var diagnostic in emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error || d.Severity == DiagnosticSeverity.Warning).Take(128))
                {
                    var span = diagnostic.Location.GetMappedLineSpan();
                    diagnostics.Add(new JObject { ["id"] = diagnostic.Id, ["severity"] = diagnostic.Severity.ToString().ToLowerInvariant(), ["message"] = EvalResult.Limit(diagnostic.GetMessage()), ["line"] = span.StartLinePosition.Line + 1, ["column"] = span.StartLinePosition.Character + 1 });
                }
                if (!emit.Success) { result["state"] = "compile_error"; return; }
                emitted();
                object value;
                try { value = Assembly.Load(stream.ToArray()).GetType("EvalSnippet").GetMethod("Run").Invoke(null, null); }
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
}
