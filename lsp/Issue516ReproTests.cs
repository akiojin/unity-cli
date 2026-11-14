// LOCAL ONLY (not committed): #516 regression reproduction harness.
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using UnityCli.Lsp.Core;
using Xunit;
using Xunit.Abstractions;

namespace UnityCli.Lsp.Tests;

public sealed class Issue516ReproTests
{
    private readonly ITestOutputHelper _out;
    public Issue516ReproTests(ITestOutputHelper output) => _out = output;

    // Oracle: rewrite every #if/#elif line condition to a unique symbol and enumerate all symbol
    // subsets; a lone CR is kept iff some configuration reads it inside a well-formed multi-line string.
    public static string Oracle(string text, string newline)
    {
        var lines = Regex.Matches(text, @"(?<=^|[\r\n])[ \t]*#[ \t]*(if|elif)[ \t]+[^\r\n]*");
        var sb = new StringBuilder(text);
        var n = 0;
        foreach (Match m in lines)
        {
            var head = Regex.Match(m.Value, @"^[ \t]*#[ \t]*(if|elif)[ \t]+");
            var sym = ((char)('a' + n)).ToString();
            var avail = m.Length - head.Length;
            if (avail < sym.Length) continue;
            n++;
            var repl = sym + new string(' ', avail - sym.Length);
            for (var k = 0; k < repl.Length; k++) sb[m.Index + head.Length + k] = repl[k];
        }
        var rewritten = sb.ToString();
        var keep = new HashSet<int>();
        for (var mask = 0; mask < (1 << n); mask++)
        {
            var syms = Enumerable.Range(0, n).Where(i => (mask & (1 << i)) != 0).Select(i => ((char)('a' + i)).ToString());
            var root = CSharpSyntaxTree.ParseText(rewritten, CSharpParseOptions.Default.WithPreprocessorSymbols(syms)).GetRoot();
            foreach (var t in root.DescendantTokens())
            {
                if (!(t.Kind() is SyntaxKind.StringLiteralToken or SyntaxKind.Utf8StringLiteralToken or SyntaxKind.MultiLineRawStringLiteralToken or SyntaxKind.Utf8MultiLineRawStringLiteralToken or SyntaxKind.InterpolatedStringTextToken)) continue;
                var lit = t.IsKind(SyntaxKind.InterpolatedStringTextToken) ? t.Parent?.Parent : null;
                if (t.GetDiagnostics().Any(d => d.Id is "CS1039" or "CS8997") || lit is InterpolatedStringExpressionSyntax ise && ise.StringEndToken.IsMissing) continue;
                for (var i = t.Span.Start; i < t.Span.End; i++)
                    if (text[i] == '\r' && (i + 1 >= text.Length || text[i + 1] != '\n')) keep.Add(i);
            }
        }
        var r = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\n') r.Append(newline);
            else if (c != '\r') r.Append(c);
            else if (i + 1 < text.Length && text[i + 1] == '\n') { r.Append(newline); i++; }
            else r.Append(keep.Contains(i) ? "\r" : newline);
        }
        return r.ToString();
    }

    private static string Show(string s) => s.Replace("\r", "\\r").Replace("\n", "\\n\n");

    private static readonly string[] Fragments =
    {
                "var s = @\"x\ry\";\n", "var t = @\"\n", "var x = \"\";\n", "var u = \"\"\"\n", "\"\"\";\n",
        "int a = 1;\rint b = 2;\n", "var v = @\"p\r#else\rq\";\n", "var w = @\"p\r#endif\rq\";\n",
        "class C { string f = @\"a\r#else\rb\"; }\n", "/*\n", "*/\n", "var z = $@\"k\r{1}\";\n",
    };

    private static readonly string[] Conds = { "A", "B", "false", "true", "C" };
    private static string Block(Random rng, int depth)
    {
        var sb = new StringBuilder();
        var n = rng.Next(1, 4);
        for (var i = 0; i < n; i++)
        {
            if (depth < 3 && rng.Next(3) == 0)
            {
                sb.Append("#if " + Conds[rng.Next(Conds.Length)] + "\n").Append(Block(rng, depth + 1));
                if (rng.Next(2) == 0) sb.Append("#elif " + Conds[rng.Next(Conds.Length)] + "\n").Append(Block(rng, depth + 1));
                if (rng.Next(2) == 0) sb.Append("#else\n").Append(Block(rng, depth + 1));
                sb.Append("#endif\n");
            }
            else sb.Append(Fragments[rng.Next(Fragments.Length)]);
        }
        return sb.ToString();
    }

    private static bool Balanced(string t)
    {
        var depth = 0;
        foreach (var line in t.Split('\n'))
        {
            var l = line.TrimStart();
            if (l.StartsWith("#if")) depth++;
            else if (l.StartsWith("#endif")) { if (--depth < 0) return false; }
            else if ((l.StartsWith("#else") || l.StartsWith("#elif")) && depth == 0) return false;
        }
        return depth == 0;
    }
    private static bool Differs(string t) => t.Contains('\r') && Balanced(t) && Oracle(t, "\n") != new LspSourceFileFormat(new UTF8Encoding(false), "\n").ApplyNewlines(t);
    private static string Minimize(string text)
    {
        var lines = Regex.Split(text, "(?<=\n)").Where(l => l.Length > 0).ToList();
        for (var changed = true; changed;)
        {
            changed = false;
            for (var i = 0; i < lines.Count; i++)
            {
                var cand = lines.Where((_, j) => j != i).ToList();
                if (Differs(string.Concat(cand))) { lines = cand; changed = true; i--; }
            }
        }
        return string.Concat(lines);
    }

    [Fact]
    public void Fuzz()
    {
        var rng = new Random(516);
        var fails = 0;
        var sb = new StringBuilder();
        var seen = new HashSet<string>();
        for (var iter = 0; iter < 50000 && fails < 400; iter++)
        {
            var text = Block(rng, 0);
            if (!text.Contains('\r')) continue;
            if (Regex.Matches(text, "#if|#elif").Count > 7) continue;
            var expected = Oracle(text, "\n");
            var actual = new LspSourceFileFormat(new UTF8Encoding(false), "\n").ApplyNewlines(text);
            if (expected == actual) continue;
            text = Minimize(text);
            if (!seen.Add(text)) continue;
            expected = Oracle(text, "\n");
            actual = new LspSourceFileFormat(new UTF8Encoding(false), "\n").ApplyNewlines(text);
            fails++;
            sb.Append("=== INPUT " + System.Text.Json.JsonSerializer.Serialize(text) + "\n" + Show(text) + "--- ORACLE\n" + Show(expected) + "--- ACTUAL\n" + Show(actual));
        }
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("REPRO_OUT") ?? ".", "fuzz.txt"), sb + "fails=" + fails + "\n");
    }
}
public sealed class Issue516Debug
{
    [Fact]
    public void Dump()
    {
        var text = "x\";\n#if B\nvar v = @\"p\r#else\rq\";\n#elif true\nvar z = $@\"k\r{1}\";\n#endif\n";
        var sb = new StringBuilder();
        var root = CSharpSyntaxTree.ParseText(text.Replace("#if B", "#if a").Replace("#elif true", "#elif b   "), CSharpParseOptions.Default.WithPreprocessorSymbols("a")).GetRoot();
        foreach (var t in root.DescendantTokens(descendIntoTrivia: true)) sb.AppendLine(t.Kind() + " " + System.Text.Json.JsonSerializer.Serialize(t.ToFullString()) + " " + string.Join(",", t.GetDiagnostics().Select(d => d.Id)));
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("REPRO_OUT") ?? ".", "dump.txt"), sb.ToString());
    }
}
public sealed class Issue516Gen
{
    private static string Lit(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
    [Fact]
    public void Gen()
    {
        var inputs = new[]
        {
            "#if true\n#if C\nvar w = @\"p\r#endif\rq\";\n#endif\n#endif\nvar s = @\"x\ry\";\n",
            "#if B\n#if A\nvar v = @\"p\r#else\rq\";\n#endif\nvar w = @\"p\r#endif\rq\";\n#endif\n",
            "#if C\n#if A\nvar v = @\"p\r#else\rq\";\n#else\nvar z = $@\"k\r{1}\";\n#endif\n#endif\n",
            "#if true\n#if false\nvar w = @\"p\r#endif\rq\";\n#endif\nvar s = @\"x\ry\";\n#endif\n",
            "#if B\n#if A\n#if true\nvar t = @\"\n#endif\nvar z = $@\"k\r{1}\";\n#endif\n#endif\n",
            "#if true\nvar t = @\"\n#else\nvar s = @\"x\ry\";\n#endif\n",
            "#if true\nvar t = @\"\n#elif false\nvar w = @\"p\r#endif\rq\";\n#endif\n",
            "#if true\nvar u = \"\"\"\n#endif\nclass C { string f = @\"a\r#else\rb\"; }\n",
            "#if A\nclass C { string f = @\"a\r#else\rb\"; }\n#endif\nvar z = $@\"k\r{1}\";\n",
            "#if B\nclass C { string f = @\"a\r#else\rb\"; }\nvar u = \"\"\"\n#endif\n",
        };
        var sb = new StringBuilder();
        foreach (var input in inputs)
        {
            var exp = Issue516ReproTests.Oracle(input, "\n");
            var act = new LspSourceFileFormat(new UTF8Encoding(false), "\n").ApplyNewlines(input);
            sb.AppendLine((exp == act ? "// PASSES " : "// FAILS ") + "\n    [InlineData(" + Lit(input) + ", " + Lit(exp) + ")]");
        }
        File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("REPRO_OUT") ?? ".", "gen.txt"), sb.ToString());
    }
}
