using System.Collections.Concurrent;

namespace UnityCli.Lsp.Core;

public sealed class LspFileLockProvider
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks = new(StringComparer.OrdinalIgnoreCase);

    public async Task<IDisposable> AcquireAsync(string path)
    {
        var key = path ?? string.Empty;
        var sem = _fileLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync();
        return new Releaser(() => sem.Release());
    }

    private sealed class Releaser : IDisposable
    {
        private readonly Action _release;

        public Releaser(Action release) => _release = release;

        public void Dispose() => _release();
    }
}
