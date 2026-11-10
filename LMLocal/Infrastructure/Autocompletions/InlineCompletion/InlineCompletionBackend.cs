using System;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.DependencyInjection;

namespace LMLocal.Infrastructure.Autocompletions.InlineCompletion
{
    /// <summary>
    /// Shared, process-wide view of which inline completion backend is active
    /// ("legacy" ghost-text stack or "native" VS Inline Suggestions).
    /// </summary>
    internal static class InlineCompletionBackend
    {
        internal const string Native = "native";
        internal const int RefreshIntervalMs = 2000;

        private static volatile bool _isNative;
        private static int _lastRefreshTick;
        private static int _refreshing;

        /// <summary>True when the native (Proposals API) backend is selected.</summary>
        internal static bool IsNative => _isNative;

        internal static bool IsNativeBackend(string backend)
        {
            return string.Equals(backend?.Trim(), Native, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Applies an already loaded config (e.g. when a caller read it for other settings).</summary>
        internal static void Apply(AutocompletionsConfig config)
        {
            _isNative = IsNativeBackend(config?.Backend);
            _lastRefreshTick = Environment.TickCount;
        }

        /// <summary>
        /// Re-reads the backend from the config if the last read is older than <see cref="RefreshIntervalMs"/>.
        /// </summary>
        internal static async Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            if (unchecked(Environment.TickCount - _lastRefreshTick) < RefreshIntervalMs && _lastRefreshTick != 0)
                return;

            if (Interlocked.Exchange(ref _refreshing, 1) == 1)
                return;

            try
            {
                if (!ServiceConfiguration.IsInitialized)
                    return;

                var configManager = ServiceConfiguration.GetService<IAutocompletionsConfigManager>();
                if (configManager == null)
                    return;

                var config = await configManager.GetAsync(cancellationToken).ConfigureAwait(false);
                Apply(config);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                InternalLogger.Warn("InlineCompletionBackend.RefreshAsync: " + ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _refreshing, 0);
            }
        }
    }
}
