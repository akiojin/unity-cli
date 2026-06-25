using System.Text.Json;
using System.Text;
using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityCli.Lsp.Core;

// Minimal LSP over stdio: initialize / initialized / shutdown / exit / documentSymbol / workspace/symbol / unitycli/referencesByName / unitycli/renameByNamePath / unitycli/replaceSymbolBody / unitycli/insertBeforeSymbol / unitycli/insertAfterSymbol / unitycli/removeSymbol
// This is a lightweight PoC that parses each file independently using Roslyn SyntaxTree.

LspLogger.Info("Starting...");
var server = new LspServer();
await server.RunAsync();

sealed class LspServer
{
    private readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };
    private readonly SemaphoreSlim _requestLimiter = new(Math.Max(1, Environment.ProcessorCount), Math.Max(1, Environment.ProcessorCount));
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly LspRequestRouter _router = new();
    private readonly List<Task> _inFlight = new();
    private readonly object _inFlightLock = new();

    public async Task RunAsync()
    {
        while (true)
        {
            var msg = await ReadMessageAsync();
            if (msg is null) break;
            try
            {
                var json = JsonDocument.Parse(msg);
                var root = json.RootElement;
                var method = root.TryGetProperty("method", out var m) ? m.GetString() : null;
                if (method is not null)
                {
                    if (method == "exit" || method == "shutdown")
                    {
                        await DrainInFlightAsync();
                        if (await DispatchAsync(root)) break;
                    }
                    else
                    {
                        var task = Task.Run(async () =>
                        {
                            await _requestLimiter.WaitAsync();
                            try
                            {
                                await DispatchAsync(root);
                            }
                            finally
                            {
                                _requestLimiter.Release();
                            }
                        });
                        lock (_inFlightLock)
                        {
                            _inFlight.Add(task);
                            _inFlight.RemoveAll(t => t.IsCompleted);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LspLogger.Error($"Failed to process message: {ex.Message}");
            }
        }
        await DrainInFlightAsync();
    }

    private async Task DrainInFlightAsync()
    {
        Task[] snapshot;
        lock (_inFlightLock)
        {
            snapshot = _inFlight.ToArray();
            _inFlight.Clear();
        }
        if (snapshot.Length > 0)
        {
            try { await Task.WhenAll(snapshot); } catch { }
        }
    }

    private async Task<bool> DispatchAsync(JsonElement root)
    {
        var result = await _router.HandleAsync(root);
        if (result.HasResponse)
        {
            await WriteMessageAsync(result.Payload!);
        }

        return result.ShouldExit;
    }

    private async Task<string?> ReadMessageAsync()
    {
        var stdin = Console.OpenStandardInput();
        int contentLength = 0;

        // Read headers as raw bytes until \r\n\r\n
        var headerBytes = new List<byte>(256);
        var one = new byte[1];
        while (true)
        {
            int n = await stdin.ReadAsync(one, 0, 1);
            if (n <= 0) return null;
            headerBytes.Add(one[0]);
            int count = headerBytes.Count;
            if (count >= 4 &&
                headerBytes[count - 4] == (byte)'\r' &&
                headerBytes[count - 3] == (byte)'\n' &&
                headerBytes[count - 2] == (byte)'\r' &&
                headerBytes[count - 1] == (byte)'\n')
            {
                break;
            }
        }

        var headerText = Encoding.ASCII.GetString(headerBytes.ToArray());
        var headerLines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in headerLines)
        {
            var idx = line.IndexOf(":", StringComparison.Ordinal);
            if (idx <= 0) continue;
            var key = line.Substring(0, idx).Trim();
            var val = line.Substring(idx + 1).Trim();
            if (key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                int.TryParse(val, out contentLength);
            }
        }

        if (contentLength <= 0) return null;
        var body = new byte[contentLength];
        int read = 0;
        while (read < contentLength)
        {
            int n = await stdin.ReadAsync(body, read, contentLength - read);
            if (n <= 0) break;
            read += n;
        }
        return Encoding.UTF8.GetString(body, 0, read);
    }

    private async Task WriteMessageAsync(object payload)
    {
        var json = JsonSerializer.Serialize(payload, _json);
        var header = $"Content-Length: {Encoding.UTF8.GetByteCount(json)}\r\n\r\n";
        await _writeLock.WaitAsync();
        try
        {
            await Console.Out.WriteAsync(header);
            await Console.Out.WriteAsync(json);
            await Console.Out.FlushAsync();
        }
        finally
        {
            _writeLock.Release();
        }
    }

}
