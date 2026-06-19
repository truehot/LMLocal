using System;
using System.Threading;
using System.Threading.Tasks;

namespace LMLocal.Core.Common
{
    /// <summary>
    /// Non-reentrant asynchronous mutex.
    /// Use inside using (await lock.LockAsync()).
    /// Do not attempt to reacquire the lock within Task.Run or other child contexts, as this will throw InvalidOperationException. It's better to move Task.Run outside of the lock section.
    /// </summary>

    public sealed class AsyncLock : IDisposable
    {
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private readonly AsyncLocal<bool> _isHeldInCurrentContext = new AsyncLocal<bool>();
        private int _disposed; // 0 – active, 1 – disposed

        public async Task<IDisposable> LockAsync(CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _disposed) == 1)
                ThrowDisposed();

            if (_isHeldInCurrentContext.Value)
                ThrowReentrancy();

            await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            if (Volatile.Read(ref _disposed) == 1)
            {
                try
                {
                    _semaphore.Release();
                }
                catch (ObjectDisposedException)
                {
                    InternalLogger.Info($"AsyncLock: semaphore was disposed while waiting for lock. This may indicate a race condition or improper usage.");
                }
                ThrowDisposed();
            }

            _isHeldInCurrentContext.Value = true;

            return new Releaser(this);
        }

        public IDisposable Lock(CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _disposed) == 1)
                ThrowDisposed();

            if (_isHeldInCurrentContext.Value)
                ThrowReentrancy();

            _semaphore.Wait(cancellationToken);

            if (Volatile.Read(ref _disposed) == 1)
            {
                try
                {
                    _semaphore.Release();
                }
                catch (ObjectDisposedException) { }
                ThrowDisposed();
            }

            _isHeldInCurrentContext.Value = true;
            return new Releaser(this);
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
            {
                _semaphore.Dispose();
            }
        }

        private static void ThrowDisposed()
        {
            throw new ObjectDisposedException(nameof(AsyncLock));
        }

        private static void ThrowReentrancy()
        {
            throw new InvalidOperationException("AsyncLock does not support reentrancy.");
        }

        private sealed class Releaser : IDisposable
        {
            private AsyncLock _parent;

            public Releaser(AsyncLock parent)
            {
                _parent = parent;
            }

            public void Dispose()
            {
                var parent = Interlocked.Exchange(ref _parent, null);
                if (parent == null)
                    return;

                parent._isHeldInCurrentContext.Value = false;

                if (Volatile.Read(ref parent._disposed) == 0)
                {
                    parent._semaphore.Release();
                }
            }
        }
    }
}
