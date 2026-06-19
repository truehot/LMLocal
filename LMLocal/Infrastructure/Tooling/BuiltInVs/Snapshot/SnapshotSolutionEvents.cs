using System;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot
{
    internal interface ISnapshotSolutionEvents
    {
        void Dispose();
        void Initialize();
    }

    /// <summary>
    /// Listens to VS solution events and translates them into snapshot lifecycle calls.
    /// </summary>
    internal sealed class SnapshotSolutionEvents : IDisposable, ISnapshotSolutionEvents
    {
        private readonly IVsDependencies _vsDependencies;
        private readonly ISnapshotManager _snapshotManager;
        private CancellationTokenSource _solutionLifetimeCts = new CancellationTokenSource();
        private bool _disposed;

        public SnapshotSolutionEvents(IVsDependencies vsDependencies, ISnapshotManager snapshotManager)
        {
            _vsDependencies = vsDependencies ?? throw new ArgumentNullException(nameof(vsDependencies));
            _snapshotManager = snapshotManager ?? throw new ArgumentNullException(nameof(snapshotManager));
        }

        public void Initialize()
        {
            _vsDependencies.SolutionOpened += OnSolutionOpened;
            _vsDependencies.SolutionClosed += OnSolutionClosed;
        }

        public void Dispose()
        {
            if (_disposed) return;

            _vsDependencies.SolutionOpened -= OnSolutionOpened;
            _vsDependencies.SolutionClosed -= OnSolutionClosed;

            try
            {
                _solutionLifetimeCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                InternalLogger.Warn("Solution lifetime CTS was already disposed when trying to cancel it during SnapshotSolutionEvents disposal.");
            }
            _solutionLifetimeCts.Dispose();

            _disposed = true;
        }

        private void OnSolutionOpened()
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _snapshotManager.LoadSnapshotAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    InternalLogger.Error($"Snapshot load failed: {ex}");
                }
            });
        }

        private void OnSolutionClosed()
        {
            var oldCts = Interlocked.Exchange(ref _solutionLifetimeCts, new CancellationTokenSource());
            try { oldCts.Cancel(); } catch (ObjectDisposedException) { }

            _ = Task.Run(async () =>
            {
                try
                {
                    await _snapshotManager.ResetAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    InternalLogger.Error($"Error during solution close cleanup: {ex}");
                }
            });
        }
    }
}
