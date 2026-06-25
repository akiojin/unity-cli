using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using UnityCli.Lsp.Core;
using Xunit;

public sealed class WorkspaceSymbolServiceTests
{
    [Fact]
    public async Task WorkspaceSymbolAsync_FindsMatchingSymbolsInUnityFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "unity-cli-lsp-symbols-" + Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(root, "Assets", "Scripts"));
        var scriptPath = Path.Combine(root, "Assets", "Scripts", "Player.cs");
        await File.WriteAllTextAsync(scriptPath, "public class Player { public void Jump() {} }");

        try
        {
            var service = new LspWorkspaceSymbolService(root, new LspFileLockProvider());
            var result = await service.WorkspaceSymbolAsync("Player");
            var json = JsonSerializer.Serialize(result);

            Assert.Contains("\"name\":\"Player\"", json);
            Assert.Contains("Assets/Scripts/Player.cs", json);
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
