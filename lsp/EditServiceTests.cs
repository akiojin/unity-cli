using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using UnityCli.Lsp.Core;
using Xunit;

public sealed class EditServiceTests
{
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
}
