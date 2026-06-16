using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using UnityCli.Lsp.Core;
using Xunit;

public sealed class CodeIndexServiceTests
{
    [Fact]
    public async Task BuildAsync_WritesIndexWithCollectedSymbols()
    {
        var root = Path.Combine(Path.GetTempPath(), "unity-cli-lsp-index-" + Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(root, "Assets", "Scripts"));
        var scriptPath = Path.Combine(root, "Assets", "Scripts", "Player.cs");
        await File.WriteAllTextAsync(scriptPath, "public class Player { public int Health; }");

        try
        {
            var service = new LspCodeIndexService(root);
            await service.BuildAsync(null);

            var indexPath = Path.Combine(root, ".unity", "code-index.json");
            Assert.True(File.Exists(indexPath));

            var doc = JsonSerializer.Deserialize<CodeIndexDocument>(
                await File.ReadAllTextAsync(indexPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            Assert.NotNull(doc);
            Assert.Contains(doc!.Entries, entry => entry.Name == "Player" && entry.File == "Assets/Scripts/Player.cs");
            Assert.Contains(doc.Entries, entry => entry.Name == "Health");
            Assert.True(doc.Entries.All(entry => !string.IsNullOrWhiteSpace(entry.NamePath)));
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
