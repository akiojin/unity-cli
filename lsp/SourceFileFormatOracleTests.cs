using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using UnityCli.Lsp.Core;
using Xunit;

// #520: compares ApplyNewlines with an oracle that parses every branch-selection configuration.
public sealed class SourceFileFormatOracleTests
{
    private static readonly LspSourceFileFormat LfFile = new(new UTF8Encoding(false), "\n");

    // Enumerates independent choices for every candidate #if/#elif, letting Roslyn decide which lines
    // are directives in each configuration. A lone CR is kept iff some configuration reads it inside a
    // closed multi-line string literal.
    private static string Oracle(string text)
    {
        var candidates = Regex.Matches(text, @"(?<=^|[\r\n])[ \t]*#[ \t]*(if|elif)[ \t]+[^\r\n]*");
        Assert.InRange(candidates.Count, 0, 7); // Exhaustive enumeration is deliberately test-only and bounded.
        var keep = new HashSet<int>();
        for (var mask = 0; mask < 1 << candidates.Count; mask++)
        {
            var defined = Enumerable.Range(0, candidates.Count).Where(i => (mask & (1 << i)) != 0).Select(i => ((char)('a' + i)).ToString());
            var options = CSharpParseOptions.Default.WithPreprocessorSymbols(defined);
            var builder = new StringBuilder(text);
            var root = CSharpSyntaxTree.ParseText(text, options).GetRoot();
            for (var i = 0; i < candidates.Count; i++)
            {
                var line = candidates[i];
                var hash = text.IndexOf('#', line.Index);
                var directive = root.DescendantTrivia(descendIntoTrivia: true)
                    .FirstOrDefault(trivia => trivia.IsDirective && trivia.SpanStart == hash).GetStructure();
                var condition = directive switch
                {
                    IfDirectiveTriviaSyntax ifDirective => ifDirective.Condition,
                    ElifDirectiveTriviaSyntax elifDirective => elifDirective.Condition,
                    _ => null,
                };
                if (condition is null || condition.Span.IsEmpty) continue;
                // Apply choices in source order, re-parsing before the next candidate. An earlier choice
                // can expose a string containing a later '#if'; never overwrite that string's contents.
                var replacement = ((char)('a' + i)).ToString().PadRight(condition.Span.Length);
                for (var k = 0; k < replacement.Length; k++) builder[condition.SpanStart + k] = replacement[k];
                root = CSharpSyntaxTree.ParseText(builder.ToString(), options).GetRoot();
            }
            foreach (var token in root.DescendantTokens(descendIntoTrivia: true)) Keep(token, 0);
        }

        // Error recovery can leave a closed literal in skipped-token trivia, where an interpolated string
        // stays one lexer token; the lexer still reads it as a closed literal, so it counts as well.
        void Keep(SyntaxToken token, int offset)
        {
            if (token.IsKind(SyntaxKind.InterpolatedStringToken))
            {
                foreach (var part in SyntaxFactory.ParseExpression(token.Text).DescendantTokens(descendIntoTrivia: true)) Keep(part, offset + token.SpanStart);
                return;
            }
            if (!token.IsKind(SyntaxKind.StringLiteralToken) && !token.IsKind(SyntaxKind.Utf8StringLiteralToken) &&
                !token.IsKind(SyntaxKind.MultiLineRawStringLiteralToken) && !token.IsKind(SyntaxKind.Utf8MultiLineRawStringLiteralToken) &&
                !token.IsKind(SyntaxKind.InterpolatedStringTextToken)) return;
            var literal = token.IsKind(SyntaxKind.InterpolatedStringTextToken) ? token.Parent?.Parent : null;
            // Only the token's own diagnostics: its trailing trivia can hold a later skipped literal's.
            if (token.WithoutTrivia().GetDiagnostics().Any(d => d.Id is "CS1039" or "CS8997") ||
                literal is InterpolatedStringExpressionSyntax interpolated && interpolated.StringEndToken.IsMissing) return;
            for (var i = offset + token.Span.Start; i < offset + token.Span.End; i++)
            {
                if (text[i] == '\r' && (i + 1 >= text.Length || text[i + 1] != '\n')) keep.Add(i);
            }
        }

        var result = new StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\n') result.Append('\n');
            else if (c != '\r') result.Append(c);
            else if (i + 1 < text.Length && text[i + 1] == '\n') { result.Append('\n'); i++; }
            else result.Append(keep.Contains(i) ? "\r" : "\n");
        }
        return result.ToString();
    }

    private static readonly string[] Fragments =
    {
        "var s = @\"x\ry\";\n", "var t = @\"\n", "var x = \"\";\n", "var u = \"\"\"\n", "\"\"\";\n",
        "int a = 1;\rint b = 2;\n", "var v = @\"p\r#else\rq\";\n", "var w = @\"p\r#endif\rq\";\n",
        "class C { string f = @\"a\r#else\rb\"; }\n", "/*\n", "*/\n", "var z = $@\"k\r{1}\";\n",
    };

    private static readonly string[] Conditions = { "A", "B", "false", "true", "C" };

    private static readonly string[] RegressionFragments = Fragments.Concat(new[]
    {
        "var s = @\"x\r#if A\";\n", "var s = $@\"x\r#elif A\";\n",
        "var s = $@\"outer\n{ @\"inner\rtext\" }\n", "var s = $@\"outer\n{ $@\"inner\r{1}\" }\n",
    }).ToArray();

    private static string Block(Random random, int depth, bool includeRegressions)
    {
        var builder = new StringBuilder();
        for (var n = random.Next(1, 4); n > 0; n--)
        {
            if (depth < 3 && random.Next(3) == 0)
            {
                builder.Append("#if " + Conditions[random.Next(Conditions.Length)] + "\n").Append(Block(random, depth + 1, includeRegressions));
                if (random.Next(2) == 0) builder.Append("#elif " + Conditions[random.Next(Conditions.Length)] + "\n").Append(Block(random, depth + 1, includeRegressions));
                if (random.Next(2) == 0) builder.Append("#else\n").Append(Block(random, depth + 1, includeRegressions));
                builder.Append("#endif\n");
            }
            else
            {
                var fragments = includeRegressions ? RegressionFragments : Fragments;
                builder.Append(fragments[random.Next(fragments.Length)]);
            }
        }
        return builder.ToString();
    }

    private static string Show(string text) => text.Replace("\r", "\\r").Replace("\n", "\\n");

    [Theory]
    [InlineData("var s = @\"x\r#if A\";\n")]
    [InlineData("var s = @\"x\r#elif A\";\n")]
    [InlineData("#if A\nvar s = @\"x\r#if B\";\n#endif\n")]
    [InlineData("#if A\nvar s = $@\"x\r#if B\";\n#endif\n")]
    [InlineData("#if A\nvar s = \"\"\"\nx\r#if B\r\"\"\";\n#endif\n")]
    [InlineData("var s = $@\"outer\n{ @\"inner\rtext\" }\n")]
    [InlineData("var s = $@\"outer\n{ $@\"inner\r{1}\" }\n")]
    [InlineData("var s = $$\"\"\"\nouter\n{{ @\"inner\rtext\" }}\n")]
    public void Oracle_PreservesClosedLiteralContent(string input)
    {
        Assert.Equal(input, Oracle(input));
        Assert.Equal(input, LfFile.ApplyNewlines(input));
    }

    [Theory]
    [InlineData(516, false)]
    [InlineData(520, true)]
    public void ApplyNewlines_MatchesTheAllConfigurationsOracleOnGeneratedSources(int seed, bool includeRegressions)
    {
        var random = new Random(seed);
        var compared = 0;
        var mismatches = new List<string>();
        for (var iteration = 0; iteration < 50000 && compared < 1000; iteration++)
        {
            var text = Block(random, 0, includeRegressions);
            if (!text.Contains('\r') || Regex.Matches(text, "#if|#elif").Count > 7) continue;
            compared++;
            var expected = Oracle(text);
            var actual = LfFile.ApplyNewlines(text);
            if (expected != actual) mismatches.Add(Show(text) + "\n  oracle: " + Show(expected) + "\n  actual: " + Show(actual));
        }

        Assert.True(compared >= 400, "compared " + compared);
        Assert.True(mismatches.Count == 0, mismatches.Count + " of " + compared + " mismatched:\n" + string.Join("\n", mismatches.Take(10)));
    }

    // Representative minimal mismatches the generator reported against develop 74806ad1.
    [Theory]
    [InlineData("#if true\nvar t = @\"\n#else\nvar s = @\"x\ry\";\n#endif\n")]
    [InlineData("#if true\n/*\n#else\nvar v = @\"p\r#else\rq\";\n#endif\n")]
    [InlineData("#if true\n/*\n#elif C\nvar w = @\"p\r#endif\rq\";\n#endif\n")]
    [InlineData("#if true\n/*\n#endif\nvar w = @\"p\r#endif\rq\";\n")]
    [InlineData("#if B\n#elif true\nvar t = @\"\n#endif\nvar z = $@\"k\r{1}\";\n")]
    [InlineData("#if A\nvar v = @\"p\r#else\rq\";\n/*\n#endif\n")]
    [InlineData("#if C\n/*\n#if C\n*/\n#endif\nvar v = @\"p\r#else\rq\";\n#endif\n")]
    [InlineData("#if B\nvar v = @\"p\r#else\rq\";\n#elif true\nvar z = $@\"k\r{1}\";\n#endif\n")]
    [InlineData("#if false\nvar a = \"\"\"\n  x\r  #if B\r  \"\"\";\n#else\nvar a = $@\"x\r#else\r{1}\";\n#endif\n")]
    public void ApplyNewlines_MatchesTheOracleOnMinimalMismatches(string input)
    {
        Assert.Equal(Oracle(input), LfFile.ApplyNewlines(input));
    }
}
