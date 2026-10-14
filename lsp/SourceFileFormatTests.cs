using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using UnityCli.Lsp.Core;
using Xunit;

public sealed class SourceFileFormatTests
{
    private const string Original = "public class Sample\n{\n    public int Run() { return 1; }\n}\n";

    // Every BOM the reader recognises, plus UTF-8 without one.
    private static readonly string[] Encodings = { "utf-8", "utf-8-bom", "utf-16le", "utf-16be", "utf-32le", "utf-32be" };

    public static TheoryData<string, string, string> ExistingFileEdits()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var operation in new[] { "write", "apply", "replace_body", "insert_before", "insert_after", "remove", "rename" })
        foreach (var encoding in Encodings)
        foreach (var newline in new[] { "\n", "\r\n", "\r" })
        {
            data.Add(operation, encoding, newline);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(ExistingFileEdits))]
    public async Task ExistingFileEdits_KeepTheOriginalBomAndNewlines(string operation, string encoding, string newline)
    {
        var root = Path.Combine(Path.GetTempPath(), "unity-cli-format-" + Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(root, "Assets"));
        var scriptPath = Path.Combine(root, "Assets", "Sample.cs");
        await File.WriteAllBytesAsync(scriptPath, Encode(Original, encoding, newline));
        try
        {
            var service = new LspEditService(root, new LspFileLockProvider());
            // Inputs always use LF so a CRLF file also proves newline preservation.
            const string written = "public class Sample\n{\n    public int Run() { return 2; }\n}\n";
            using var files = JsonDocument.Parse(JsonSerializer.Serialize(new[] { new { relative = "Assets/Sample.cs", newText = written } }));
            var (result, expected) = operation switch
            {
                "write" => (await service.WriteCSharpFileAsync("Assets/Sample.cs", written, true, true, false), written),
                "apply" => (await service.ApplyCSharpEditsAsync(files.RootElement, true, true, false), written),
                "replace_body" => (await service.ReplaceSymbolBodyAsync("Assets/Sample.cs", "Sample/Run", "{ return 2; }", true), written),
                "insert_before" => (await service.InsertAroundSymbolAsync("Assets/Sample.cs", "Sample/Run", "public int Added;\n", false, true),
                    "public class Sample\n{\npublic int Added;\n    public int Run() { return 1; }\n}\n"),
                "insert_after" => (await service.InsertAroundSymbolAsync("Assets/Sample.cs", "Sample/Run", "public int Added;\n", true, true),
                    "public class Sample\n{\n    public int Run() { return 1; }\npublic int Added;\n}\n"),
                "remove" => (await service.RemoveSymbolAsync("Assets/Sample.cs", "Sample/Run", true, false, false),
                    // KeepExteriorTrivia leaves the member's indentation and line break behind.
                    "public class Sample\n{\n    \n}\n"),
                _ => (await service.RenameByNamePathAsync("Assets/Sample.cs", "Sample/Run", "Execute", true),
                    Original.Replace("Run", "Execute")),
            };

            using var response = JsonDocument.Parse(JsonSerializer.Serialize(result));
            Assert.True(response.RootElement.GetProperty("applied").GetBoolean(), response.RootElement.ToString());
            Assert.Equal(Encode(expected, encoding, newline), await File.ReadAllBytesAsync(scriptPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task RenameAcrossFiles_KeepsEachFileFormat()
    {
        var root = Path.Combine(Path.GetTempPath(), "unity-cli-format-" + Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(root, "Assets"));
        const string declaration = "namespace Demo\n{\n    public partial class Sample {}\n}\n";
        const string use = "namespace Demo\n{\n    public class Consumer { public Sample Value; }\n}\n";
        await File.WriteAllBytesAsync(Path.Combine(root, "Assets", "Sample.cs"), Encode(declaration, "utf-8-bom", "\r\n"));
        await File.WriteAllBytesAsync(Path.Combine(root, "Assets", "Use.cs"), Encode(use, "utf-8", "\n"));
        try
        {
            var service = new LspEditService(root, new LspFileLockProvider());

            var result = await service.RenameByNamePathAsync("Assets/Sample.cs", "Sample", "Renamed", true);

            using var response = JsonDocument.Parse(JsonSerializer.Serialize(result));
            Assert.True(response.RootElement.GetProperty("applied").GetBoolean(), response.RootElement.ToString());
            Assert.Equal(Encode(declaration.Replace("Sample", "Renamed"), "utf-8-bom", "\r\n"),
                await File.ReadAllBytesAsync(Path.Combine(root, "Assets", "Sample.cs")));
            Assert.Equal(Encode(use.Replace("Sample", "Renamed"), "utf-8", "\n"),
                await File.ReadAllBytesAsync(Path.Combine(root, "Assets", "Use.cs")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("public class Sample {}\n")]
    [InlineData("using UnityEngine;\r\npublic class Sample : MonoBehaviour {}\r\n")]
    public async Task CreateCSharpFile_WritesUtf8WithoutBomAndKeepsGivenNewlines(string text)
    {
        var root = Path.Combine(Path.GetTempPath(), "unity-cli-format-" + Path.GetRandomFileName());
        Directory.CreateDirectory(root);
        try
        {
            var service = new LspEditService(root, new LspFileLockProvider());

            var result = await service.CreateCSharpFileAsync("Assets/Sample.cs", text, false, true, true, false);

            using var response = JsonDocument.Parse(JsonSerializer.Serialize(result));
            Assert.True(response.RootElement.GetProperty("applied").GetBoolean(), response.RootElement.ToString());
            Assert.Equal(Encoding.UTF8.GetBytes(text), await File.ReadAllBytesAsync(Path.Combine(root, "Assets", "Sample.cs")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task WriteCSharpFile_TreatsANewlineOnlyDifferenceAsNoChange()
    {
        var root = Path.Combine(Path.GetTempPath(), "unity-cli-format-" + Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(root, "Assets"));
        var scriptPath = Path.Combine(root, "Assets", "Sample.cs");
        await File.WriteAllBytesAsync(scriptPath, Encode(Original, "utf-8-bom", "\r\n"));
        try
        {
            var service = new LspEditService(root, new LspFileLockProvider());

            var result = await service.WriteCSharpFileAsync("Assets/Sample.cs", Original, true, true, false);

            using var response = JsonDocument.Parse(JsonSerializer.Serialize(result));
            Assert.Equal("no_change", response.RootElement.GetProperty("reason").GetString());
            Assert.Equal(Encode(Original, "utf-8-bom", "\r\n"), await File.ReadAllBytesAsync(scriptPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("a\r\nb\nc\n", "x\ny\n", "x\ny\n")]
    [InlineData("single line", "x\r\ny\n", "x\r\ny\n")]
    [InlineData("a\r\nb\r\n", "x\ny\r\nz\rw", "x\r\ny\r\nz\r\nw")]
    [InlineData("a\nb\n", "x\ry\r\nz\rw", "x\ny\nz\nw")]
    [InlineData("a\nb\n", "x\r\ny\n", "x\ny\n")]
    [InlineData("a\rb\r", "x\ny\r\nz\rw", "x\ry\rz\rw")]
    [InlineData("a\rb\nc\r", "x\ny\n", "x\ny\n")]
    public void ApplyNewlines_OnlyRewritesWhenTheOriginalUsesOneConvention(string original, string text, string expected)
    {
        var format = LspSourceFileFormat.Detect(Encoding.UTF8.GetBytes(original));

        Assert.Equal(expected, format.ApplyNewlines(text));
    }

    public static TheoryData<string, string> NewlineMatrix()
    {
        var data = new TheoryData<string, string>();
        foreach (var original in new[] { "\n", "\r\n", "\r" })
        foreach (var input in new[] { "\n", "\r\n", "\r" })
        {
            data.Add(original, input);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(NewlineMatrix))]
    public async Task EditInputNewlines_AreSavedInTheOriginalConvention(string originalNewline, string inputNewline)
    {
        const string written = "public class Sample\n{\n    public int Run() { return 2; }\n}\n";
        foreach (var operation in new[] { "write", "apply", "create_overwrite" })
        {
            var root = Path.Combine(Path.GetTempPath(), "unity-cli-format-" + Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(root, "Assets"));
            var scriptPath = Path.Combine(root, "Assets", "Sample.cs");
            await File.WriteAllBytesAsync(scriptPath, Encode(Original, "utf-8", originalNewline));
            try
            {
                var service = new LspEditService(root, new LspFileLockProvider());
                var input = written.Replace("\n", inputNewline);
                using var files = JsonDocument.Parse(JsonSerializer.Serialize(new[] { new { relative = "Assets/Sample.cs", newText = input } }));
                var result = operation switch
                {
                    "write" => await service.WriteCSharpFileAsync("Assets/Sample.cs", input, true, true, false),
                    "apply" => await service.ApplyCSharpEditsAsync(files.RootElement, true, true, false),
                    _ => await service.CreateCSharpFileAsync("Assets/Sample.cs", input, true, true, true, false),
                };

                using var response = JsonDocument.Parse(JsonSerializer.Serialize(result));
                Assert.True(response.RootElement.GetProperty("applied").GetBoolean(), operation + ": " + response.RootElement);
                Assert.Equal(Encode(written, "utf-8", originalNewline), await File.ReadAllBytesAsync(scriptPath));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    // A lone CR inside a string literal is part of the string's value, so it is never rewritten;
    // lone CRs anywhere else (code, comments) are line terminators and follow the file's convention.
    [Theory]
    [InlineData("\n", "var a = @\"x\ry\";\rvar b = 1;\n", "var a = @\"x\ry\";\nvar b = 1;\n")]
    [InlineData("\r\n", "var a = @\"x\ry\";\rvar b = 1;\n", "var a = @\"x\ry\";\r\nvar b = 1;\r\n")]
    [InlineData("\n", "var a = $@\"x\r{b}\ry\";\r", "var a = $@\"x\r{b}\ry\";\n")]
    [InlineData("\r\n", "var a = \"\"\"\n  x\ry\n  \"\"\";\r", "var a = \"\"\"\r\n  x\ry\r\n  \"\"\";\r\n")]
    [InlineData("\n", "var a = $\"\"\"\n  x\r{b}\n  \"\"\";\r", "var a = $\"\"\"\n  x\r{b}\n  \"\"\";\n")]
    [InlineData("\n", "var a = \"\"\"\n  x\ry\n  \"\"\"u8;\r", "var a = \"\"\"\n  x\ry\n  \"\"\"u8;\n")]
    [InlineData("\n", "// comment\rvar a = 1; /* x\ry */\r", "// comment\nvar a = 1; /* x\ny */\n")]
    [InlineData("\n", "var a = \"x\ry\";\r", "var a = \"x\ny\";\n")]
    [InlineData("\n", "var a = @\"x\r#\";\r", "var a = @\"x\r#\";\n")]
    [InlineData("\n", "#\fif UNITY_EDITOR\nvar a = @\"x\ry\";\r#endif\n", "#\fif UNITY_EDITOR\nvar a = @\"x\ry\";\n#endif\n")]
    [InlineData("\n", "#\u00a0if UNITY_EDITOR\nvar a = @\"x\ry\";\r#endif\n", "#\u00a0if UNITY_EDITOR\nvar a = @\"x\ry\";\n#endif\n")]
    // A directive-looking line inside a string of an inactive branch is string content once that branch is active.
    [InlineData("\n", "#if A\nvar a = @\"x\r#elif false\r\";\n#endif\n", "#if A\nvar a = @\"x\r#elif false\r\";\n#endif\n")]
    [InlineData("\n", "#if A\nvar a = @\"x\r#else\ry\";\n#endif\n", "#if A\nvar a = @\"x\r#else\ry\";\n#endif\n")]
    [InlineData("\n", "#if A\nvar a = @\"x\r#endif\ry\";\n#endif\n", "#if A\nvar a = @\"x\r#endif\ry\";\n#endif\n")]
    [InlineData("\r\n", "#if false\nvar a = \"\"\"\n  x\r  #if B\r  \"\"\";\n#else\nvar a = $@\"x\r#else\r{1}\";\n#endif\n", "#if false\r\nvar a = \"\"\"\r\n  x\r  #if B\r  \"\"\";\r\n#else\r\nvar a = $@\"x\r#else\r{1}\";\r\n#endif\r\n")]
    [InlineData("\n", "#if A\n#if B\nvar a = @\"x\r#elif C\r#endif\ry\";\n#endif\n#endif\n", "#if A\n#if B\nvar a = @\"x\r#elif C\r#endif\ry\";\n#endif\n#endif\n")]
    // A fake #endif inside a skipped branch's string: what follows is code in the configuration where the branch is skipped.
    [InlineData("\n", "#if false\nvar s = @\"\n#endif\nx\"\"@\"\";\nvar a = 1;\rvar b = 2;\n#endif\n", "#if false\nvar s = @\"\n#endif\nx\"\"@\"\";\nvar a = 1;\nvar b = 2;\n#endif\n")]
    [InlineData("\n", "#if A\nvar s = @\"\n#endif\nx\"\"@\"\";\nvar a = 1;\rvar b = 2;\n#endif\n", "#if A\nvar s = @\"\n#endif\nx\"\"@\"\";\nvar a = 1;\nvar b = 2;\n#endif\n")]
    [InlineData("\r\n", "#if A\nvar s = @\"\n#elif B\nx\"\"@\"\";\nvar a = 1;\rvar b = 2;\n#endif\n", "#if A\r\nvar s = @\"\r\n#elif B\r\nx\"\"@\"\";\r\nvar a = 1;\r\nvar b = 2;\r\n#endif\r\n")]
    [InlineData("\n", "#if A\nvar s = @\"\n#endif\nx\"\"@\"\"\";\nvar a = 1;\rvar b = 2;\n#endif\n", "#if A\nvar s = @\"\n#endif\nx\"\"@\"\"\";\nvar a = 1;\nvar b = 2;\n#endif\n")]
    [InlineData("\n", "#if A\nvar s = @\"\n#endif\nvar t = \"\"+@\"\"\";\nvar a = 1;\rvar b = 2;\n#endif\n", "#if A\nvar s = @\"\n#endif\nvar t = \"\"+@\"\"\";\nvar a = 1;\nvar b = 2;\n#endif\n")]
    // An unterminated comment or string in one branch must not change how another branch is lexed.
    [InlineData("\n", "#if false\n/*\n#else\nvar a = @\"x\ry\";\n#endif\n", "#if false\n/*\n#else\nvar a = @\"x\ry\";\n#endif\n")]
    [InlineData("\n", "#if UNITY_EDITOR\n/*\n#elif UNITY_STANDALONE\nvar a = @\"x\ry\";\n#endif\n", "#if UNITY_EDITOR\n/*\n#elif UNITY_STANDALONE\nvar a = @\"x\ry\";\n#endif\n")]
    [InlineData("\n", "#if UNITY_EDITOR\nvar b = @\"\n#else\nvar a = 1;\n#endif\nvar c = @\"x\ry\";\r", "#if UNITY_EDITOR\nvar b = @\"\n#else\nvar a = 1;\n#endif\nvar c = @\"x\ry\";\n")]
    [InlineData("\r\n", "#if UNITY_EDITOR\nvar b = \"\"\"\n#endif\nvar a = 1;\rvar c = @\"x\ry\";\n", "#if UNITY_EDITOR\r\nvar b = \"\"\"\r\n#endif\r\nvar a = 1;\r\nvar c = @\"x\ry\";\r\n")]
    public void ApplyNewlines_KeepsLoneCrInsideStringLiterals(string originalNewline, string text, string expected)
    {
        var format = LspSourceFileFormat.Detect(Encoding.UTF8.GetBytes("a" + originalNewline + "b" + originalNewline));

        Assert.Equal(expected, format.ApplyNewlines(text));
    }

    [Fact]
    public void ApplyNewlines_HandlesMalformedDirectivesAndManyBranches()
    {
        var format = LspSourceFileFormat.Detect(Encoding.UTF8.GetBytes("a\nb\n"));
        Assert.Equal("#if\nvar a = 1;\n#endif\n", format.ApplyNewlines("#if\rvar a = 1;\r#endif\r"));

        var block = "#if A\nvar a = @\"x\r#else\ry\";\n#else\nvar b = 1;\r#endif\n";
        var input = string.Concat(Enumerable.Repeat(block, 400));
        Assert.Equal(string.Concat(Enumerable.Repeat(block.Replace(";\r#endif", ";\n#endif"), 400)), format.ApplyNewlines(input));
    }

    [Fact]
    public void ApplyNewlines_ActivatesBranchesWithDirectivesLongerThanTheFirstWindow()
    {
        var format = LspSourceFileFormat.Detect(Encoding.UTF8.GetBytes("a\nb\n"));
        var condition = string.Join(" || ", Enumerable.Range(0, 60).Select(i => "SYMBOL_" + i));
        foreach (var directive in new[] { "#elif " + condition, "#else // " + new string('x', 300) })
        {
            var input = "#if false\nvar a = 1;\r" + directive + "\nvar b = @\"x\ry\";\r#endif\n";
            Assert.True(directive.Length > 256);
            Assert.Equal("#if false\nvar a = 1;\n" + directive + "\nvar b = @\"x\ry\";\n#endif\n", format.ApplyNewlines(input));
        }
    }

    public static TheoryData<string, string, string> InactiveBranchStrings()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var newline in new[] { "\n", "\r\n", "\r" })
        foreach (var literal in new[]
        {
            "@\"x\ry\"",
            "\"\"\"\n    x\ry\n    \"\"\"",
            "\"\"\"\n    x\ry\n    \"\"\"u8",
            "$@\"x\r{1}\ry\"",
            "$\"\"\"\n    x\r{1}\ry\n    \"\"\"",
            "@\"x\r#if OTHER\r#endif\ry\"",
        })
        foreach (var branch in new[]
        {
            "#if UNITY_EDITOR\n{0}\n#endif\n",
            "\t# if UNITY_EDITOR\n{0}\n\t# endif\n",
            "#if false\n{0}\n#endif\n",
            "#if true\nint enabled = 1;\n#elif UNITY_STANDALONE\n{0}\n#endif\n",
            "#if true\nint enabled = 1;\n#else\n{0}\n#endif\n",
            "#if UNITY_EDITOR\n#if UNITY_STANDALONE\n{0}\n#endif\n#endif\n",
            "#define UNITY_EDITOR\n#if !UNITY_EDITOR\n{0}\n#endif\n",
        })
        {
            data.Add(newline, literal, branch);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(InactiveBranchStrings))]
    public async Task EditInputNewlines_KeepStringCrsInInactiveBranches(string newline, string literal, string branch)
    {
        // Construct expected bytes separately: source line breaks follow the original convention,
        // while the lone CRs in the literal remain part of its value.
        var declaration = "var text = " + literal + ";";
        var input = branch.Replace("{0}", declaration);
        var expected = branch.Replace("\n", newline).Replace("{0}", declaration.Replace("\n", newline));
        foreach (var operation in new[] { "write", "apply", "create_overwrite" })
        {
            var root = Path.Combine(Path.GetTempPath(), "unity-cli-format-" + Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(root, "Assets"));
            var scriptPath = Path.Combine(root, "Assets", "Sample.cs");
            await File.WriteAllBytesAsync(scriptPath, Encode(Original, "utf-8-bom", newline));
            try
            {
                var service = new LspEditService(root, new LspFileLockProvider());
                using var files = JsonDocument.Parse(JsonSerializer.Serialize(new[] { new { relative = "Assets/Sample.cs", newText = input } }));
                var result = operation switch
                {
                    "write" => await service.WriteCSharpFileAsync("Assets/Sample.cs", input, true, true, false),
                    "apply" => await service.ApplyCSharpEditsAsync(files.RootElement, true, true, false),
                    _ => await service.CreateCSharpFileAsync("Assets/Sample.cs", input, true, true, true, false),
                };

                using var response = JsonDocument.Parse(JsonSerializer.Serialize(result));
                Assert.True(response.RootElement.GetProperty("applied").GetBoolean(), operation + ": " + response.RootElement);
                var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
                Assert.Equal(encoding.GetPreamble().Concat(encoding.GetBytes(expected)).ToArray(), await File.ReadAllBytesAsync(scriptPath));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ApplyNewlines_NormalizesCrsInInactiveCommentsAndDirectiveMessages()
    {
        var format = LspSourceFileFormat.Detect(Encoding.UTF8.GetBytes("a\nb\n"));
        const string input = "#if UNITY_EDITOR\r// @\"x\ry\"\r/* @\"x\ry\" */\r#error @\"x\ry\"\r#endif\r";

        Assert.Equal(input.Replace("\r", "\n"), format.ApplyNewlines(input));
    }

    private static byte[] Encode(string lfText, string encoding, string newline)
    {
        Encoding target = encoding switch
        {
            "utf-8" => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            "utf-8-bom" => new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
            "utf-16le" => new UnicodeEncoding(bigEndian: false, byteOrderMark: true),
            "utf-16be" => new UnicodeEncoding(bigEndian: true, byteOrderMark: true),
            "utf-32le" => new UTF32Encoding(bigEndian: false, byteOrderMark: true),
            "utf-32be" => new UTF32Encoding(bigEndian: true, byteOrderMark: true),
            _ => throw new ArgumentOutOfRangeException(nameof(encoding), encoding, null),
        };
        return target.GetPreamble().Concat(target.GetBytes(lfText.Replace("\n", newline))).ToArray();
    }
}
