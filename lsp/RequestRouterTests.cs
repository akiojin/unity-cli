using System.Text.Json;
using System.Threading.Tasks;
using UnityCli.Lsp.Core;
using Xunit;

public sealed class RequestRouterTests
{
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
