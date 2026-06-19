using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LMLocal.Infrastructure.Persistence
{
    /// <summary>
    /// Manages per-file synchronization using SemaphoreSlim.
    /// </summary>
    public interface IFileLockManager
    {
        Task WaitAsync(string absolutePath, CancellationToken cancellationToken = default);
        void Release(string absolutePath);
    }

    internal class FileLockManager : IFileLockManager
    {
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new ConcurrentDictionary<string, SemaphoreSlim>();

        public async Task WaitAsync(string absolutePath, CancellationToken cancellationToken = default)
        {
            string key = NormalizePath(absolutePath);
            var sem = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
            await sem.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        public void Release(string absolutePath)
        {
            string key = NormalizePath(absolutePath);
            if (_locks.TryGetValue(key, out var sem))
                sem.Release();
        }

        private static string NormalizePath(string path)
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .ToUpperInvariant();
        }
    }
}
