// LOCAL ONLY (not committed): #516 reopen reproduction (653d81a, 3 regression categories).
// Expected values come from an oracle that parses every branch-selection configuration and keeps a
// lone CR only when some configuration reads it inside a closed multi-line string literal.
using System.Text;
using UnityCli.Lsp.Core;
using Xunit;

namespace UnityCli.Lsp.Tests;

public sealed class Issue516RegressionReproTests
{
    private static string Apply(string text) => new LspSourceFileFormat(new UTF8Encoding(false), "\n").ApplyNewlines(text);

    // 1) Nested inactive conditionals: a directive inside a nested branch's string closes the wrong group.
    [Theory]
    [InlineData("#if true\n#if C\nvar w = @\"p\r#endif\rq\";\n#endif\n#endif\nvar s = @\"x\ry\";\n", "#if true\n#if C\nvar w = @\"p\r#endif\rq\";\n#endif\n#endif\nvar s = @\"x\ry\";\n")]
    [InlineData("#if B\n#if A\nvar v = @\"p\r#else\rq\";\n#endif\nvar w = @\"p\r#endif\rq\";\n#endif\n", "#if B\n#if A\nvar v = @\"p\r#else\rq\";\n#endif\nvar w = @\"p\r#endif\rq\";\n#endif\n")]
    [InlineData("#if true\n#if false\nvar w = @\"p\r#endif\rq\";\n#endif\nvar s = @\"x\ry\";\n#endif\n", "#if true\n#if false\nvar w = @\"p\r#endif\rq\";\n#endif\nvar s = @\"x\ry\";\n#endif\n")]
    [InlineData("#if B\n#if A\n#if true\nvar t = @\"\n#endif\nvar z = $@\"k\r{1}\";\n#endif\n#endif\n", "#if B\n#if A\n#if true\nvar t = @\"\n#endif\nvar z = $@\"k\r{1}\";\n#endif\n#endif\n")]
    public void NestedInactiveBranches(string input, string expected) => Assert.Equal(expected, Apply(input));

    // 2) An unterminated string in the taken branch runs into its sibling and hides that sibling's directive.
    [Theory]
    [InlineData("#if true\nvar t = @\"\n#else\nvar s = @\"x\ry\";\n#endif\n", "#if true\nvar t = @\"\n#else\nvar s = @\"x\ry\";\n#endif\n")]
    [InlineData("#if true\nvar t = @\"\n#elif false\nvar w = @\"p\r#endif\rq\";\n#endif\n", "#if true\nvar t = @\"\n#elif false\nvar w = @\"p\r#endif\rq\";\n#endif\n")]
    [InlineData("#if true\nvar u = \"\"\"\n#endif\nclass C { string f = @\"a\r#else\rb\"; }\n", "#if true\nvar u = \"\"\"\n#endif\nclass C { string f = @\"a\r#else\rb\"; }\n")]
    public void UnterminatedStringReachingSiblingBranch(string input, string expected) => Assert.Equal(expected, Apply(input));

    // 3) A class field's verbatim string holding "#else" is misread as a directive inside a skipped branch.
    [Theory]
    [InlineData("#if A\nclass C { string f = @\"a\r#else\rb\"; }\n#endif\nvar z = $@\"k\r{1}\";\n", "#if A\nclass C { string f = @\"a\r#else\rb\"; }\n#endif\nvar z = $@\"k\r{1}\";\n")]
    [InlineData("#if B\nclass C { string f = @\"a\r#else\rb\"; }\nvar u = \"\"\"\n#endif\n", "#if B\nclass C { string f = @\"a\r#else\rb\"; }\nvar u = \"\"\"\n#endif\n")]
    public void FieldStringElseMisread(string input, string expected) => Assert.Equal(expected, Apply(input));
}
