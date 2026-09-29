using System.Text.Json;
using System.Threading.Tasks;
using UnityCli.Lsp.Core;
using Xunit;

public sealed class RequestRouterTests
{
    [Theory]
    [InlineData("workspace/symbol", "query")]
    [InlineData("unitycli/referencesByName", "name")]
    public async Task SymbolQueries_RespectAssetsScopeBeforeScanningPackages(string method, string key)
    {
        var root = Path.Combine(Path.GetTempPath(), "lsp-scope-" + Path.GetRandomFileName());
        Directory.CreateDirectory(Path.Combine(root, "Assets"));
        Directory.CreateDirectory(Path.Combine(root, "Packages"));
        File.WriteAllText(Path.Combine(root, "Assets", "Asset.cs"), "class ScopeProbe {}");
        File.WriteAllText(Path.Combine(root, "Packages", "Package.cs"), "class ScopeProbe {}");
        try
        {
            var router = new LspRequestRouter();
            using var init = JsonDocument.Parse(JsonSerializer.Serialize(new {
                id = 1, method = "initialize", @params = new { rootUri = new System.Uri(root + "/").AbsoluteUri }
            }));
            await router.HandleAsync(init.RootElement);
            using var request = JsonDocument.Parse(JsonSerializer.Serialize(new {
                id = 2, method, @params = new System.Collections.Generic.Dictionary<string, string> {
                    [key] = "ScopeProbe", ["scope"] = "assets"
                }
            }));
            var response = await router.HandleAsync(request.RootElement);
            var json = JsonSerializer.Serialize(response.Payload);
            Assert.Contains("Asset.cs", json);
            Assert.DoesNotContain("Package.cs", json);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task HandleAsync_Ping_ReturnsJsonRpcResponsePayload()
    {
        var router = new LspRequestRouter();
        using var doc = JsonDocument.Parse("""{"jsonrpc":"2.0","id":7,"method":"unitycli/ping"}""");

        var result = await router.HandleAsync(doc.RootElement);
        var json = JsonSerializer.Serialize(result.Payload);

        Assert.True(result.HasResponse);
        Assert.False(result.ShouldExit);
        Assert.Contains("\"id\":7", json);
        Assert.Contains("\"ok\":true", json);
    }

    [Fact]
    public async Task HandleAsync_Exit_ReturnsNoResponseAndSignalsExit()
    {
        var router = new LspRequestRouter();
        using var doc = JsonDocument.Parse("""{"jsonrpc":"2.0","method":"exit"}""");

        var result = await router.HandleAsync(doc.RootElement);

        Assert.False(result.HasResponse);
        Assert.True(result.ShouldExit);
    }
}
