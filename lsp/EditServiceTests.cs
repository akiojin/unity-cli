using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using UnityCli.Lsp.Core;
using Xunit;

public sealed class EditServiceTests
{
    [Theory]
    [InlineData("create")]
    [InlineData("write")]
    [InlineData("apply")]
    public async Task RetryingFullFileWrites_DoesNotDuplicateContent(string operation)
    {
        var root = Path.Combine(Path.GetTempPath(), "unity-cli-retry-" + Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(root, "Assets"));
        const string relative = "Assets/Retry.cs";
        const string text = "public class Retry { public int Value; }";
        try
        {
            if (operation != "create")
            {
                await File.WriteAllTextAsync(Path.Combine(root, relative), "public class Retry { }");
            }
            var service = new LspEditService(root, new LspFileLockProvider());
            using var files = JsonDocument.Parse(JsonSerializer.Serialize(new[] { new { relative, newText = text } }));
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var result = operation switch
                {
                    "create" => await service.CreateCSharpFileAsync(relative, text, false, true, true, false),
                    "write" => await service.WriteCSharpFileAsync(relative, text, true, true, false),
                    _ => await service.ApplyCSharpEditsAsync(files.RootElement, true, true, false)
                };
                using var response = JsonDocument.Parse(JsonSerializer.Serialize(result));
                Assert.Equal(attempt == 0, response.RootElement.GetProperty("applied").GetBoolean());
                Assert.Equal(text, await File.ReadAllTextAsync(Path.Combine(root, relative)));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task WriteCSharpFileAsync_PreviewsWithoutApplying()
    {
        var root = Path.Combine(Path.GetTempPath(), "unity-cli-lsp-edit-" + Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(root, "Assets", "Scripts"));
        var scriptPath = Path.Combine(root, "Assets", "Scripts", "Player.cs");
        await File.WriteAllTextAsync(scriptPath, "public class Player { }");

        try
        {
            var service = new LspEditService(root, new LspFileLockProvider());
            var result = await service.WriteCSharpFileAsync(
                "Assets/Scripts/Player.cs",
                "public class Player { public int Health; }",
                validate: true,
                apply: false,
                format: false);

            var json = JsonSerializer.Serialize(result);

            Assert.Contains("\"success\":true", json);
            Assert.Contains("\"applied\":false", json);
            Assert.Equal("public class Player { }", await File.ReadAllTextAsync(scriptPath));
            Assert.Contains("Assets/Scripts/Player.cs", json);
            Assert.Contains("diffPreview", json);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ValidateTextEditsAsync_ReturnsSyntaxDiagnostics()
    {
        var service = new LspEditService(Path.GetTempPath(), new LspFileLockProvider());

        var result = await service.ValidateTextEditsAsync("Assets/Scripts/Broken.cs", "public class {");

        var json = JsonSerializer.Serialize(result);

        Assert.Contains("diagnostics", json);
        Assert.Contains("error", json);
    }

    [Fact]
    public async Task RenameByNamePathAsync_PreviewsTypeRenameWithoutReenteringFileLock()
    {
        var root = Path.Combine(Path.GetTempPath(), "unity-cli-lsp-rename-" + Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(root, "Assets", "Scripts"));
        var scriptPath = Path.Combine(root, "Assets", "Scripts", "Player.cs");
        const string original = "public class Player { public Player Clone() { return new Player(); } }";
        await File.WriteAllTextAsync(scriptPath, original);

        try
        {
            var service = new LspEditService(root, new LspFileLockProvider());
            var renameTask = service.RenameByNamePathAsync(
                "Assets/Scripts/Player.cs",
                "Player",
                "Hero",
                apply: false);

            var completed = await Task.WhenAny(renameTask, Task.Delay(TimeSpan.FromSeconds(2)));

            Assert.Same(renameTask, completed);
            var json = JsonSerializer.Serialize(await renameTask);
            Assert.Contains("\"success\":true", json);
            Assert.Contains("\"applied\":false", json);
            Assert.Contains("Hero", json);
            Assert.Equal(original, await File.ReadAllTextAsync(scriptPath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RemoveSymbolAsync_PreviewsWithReferenceCheckWithoutReenteringFileLock()
    {
        var root = Path.Combine(Path.GetTempPath(), "unity-cli-lsp-remove-" + Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(root, "Assets", "Scripts"));
        var scriptPath = Path.Combine(root, "Assets", "Scripts", "Player.cs");
        const string original = "public class Player { public void Jump() { } }";
        await File.WriteAllTextAsync(scriptPath, original);

        try
        {
            var service = new LspEditService(root, new LspFileLockProvider());
            var removeTask = service.RemoveSymbolAsync(
                "Assets/Scripts/Player.cs",
                "Player/Jump",
                apply: false,
                failOnRefs: true,
                removeEmptyFile: false);

            var completed = await Task.WhenAny(removeTask, Task.Delay(TimeSpan.FromSeconds(2)));

            Assert.Same(removeTask, completed);
            var json = JsonSerializer.Serialize(await removeTask);
            Assert.Contains("\"success\":true", json);
            Assert.Contains("\"applied\":false", json);
            Assert.Equal(original, await File.ReadAllTextAsync(scriptPath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(
        "public class Sample { public int Run(){return 1;} // 日本語の説明を保持\npublic int Next(){return 3;} }\n",
        "public class Sample { public int Run(){return 2;} // 日本語の説明を保持\npublic int Next(){return 3;} }\n")]
    [InlineData(
        "public class Sample {\r\n    // leading\r\n    public int Run()\r\n    {\r\n        return 1;\r\n    } /* tail */\r\n    public int Next() => 3;\r\n}\r\n",
        "public class Sample {\r\n    // leading\r\n    public int Run()\r\n    {return 2;} /* tail */\r\n    public int Next() => 3;\r\n}\r\n")]
    [InlineData(
        "public class Sample { public int Run() => 1; // expr\npublic int Next(){return 3;} }\n",
        "public class Sample { public int Run() {return 2;} // expr\npublic int Next(){return 3;} }\n")]
    public async Task ReplaceSymbolBodyAsync_PreservesTrailingTriviaOfReplacedBody(string original, string expected)
    {
        var root = Path.Combine(Path.GetTempPath(), "unity-cli-replace-body-" + Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(root, "Assets"));
        var scriptPath = Path.Combine(root, "Assets", "Sample.cs");
        await File.WriteAllBytesAsync(scriptPath, Encoding.UTF8.GetBytes(original));
        try
        {
            var service = new LspEditService(root, new LspFileLockProvider());

            var result = await service.ReplaceSymbolBodyAsync("Assets/Sample.cs", "Sample/Run", "{return 2;}", apply: true);

            using var response = JsonDocument.Parse(JsonSerializer.Serialize(result));
            Assert.True(response.RootElement.GetProperty("applied").GetBoolean());
            // The UTF-8 BOM written on save is tracked separately (#498); compare the text bytes after it.
            var bytes = await File.ReadAllBytesAsync(scriptPath);
            var preamble = Encoding.UTF8.GetPreamble();
            var body = bytes.AsSpan().StartsWith(preamble) ? bytes[preamble.Length..] : bytes;
            Assert.Equal(Encoding.UTF8.GetBytes(expected), body);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
