using System.Threading;
using LMLocal.Infrastructure.Settings;
using LMLocal.Infrastructure.Streaming;

namespace LMLocal.Application.ChatSessionStream
{
    /// <summary>
    /// Creates instances of IStreamProcessor, which handle the processing of streaming responses from the LM backend.
    /// </summary>
    internal interface IStreamProcessorFactory
    {
        IStreamProcessor Create(CancellationTokenSource cts);
    }

    internal class StreamProcessorFactory : IStreamProcessorFactory
    {
        private readonly ISettingsManager _settingsManager;

        public StreamProcessorFactory(ISettingsManager settingsManager)
        {
            _settingsManager = settingsManager ?? throw new System.ArgumentNullException(nameof(settingsManager));
        }

        public IStreamProcessor Create(CancellationTokenSource cts)
        {
            int windowSeconds = _settingsManager.WindowSeconds;
            var speedCalculator = new TokenSpeedCalculator(windowSeconds: windowSeconds);

            int timeoutSeconds = _settingsManager.Current?.StreamInactivityTimeoutSeconds ?? 0;
            IStreamInactivityWatcher watcher;
            if (timeoutSeconds > 0)
                watcher = new StreamInactivityWatcher(cts, timeoutSeconds);
            else
                watcher = new NoopStreamInactivityWatcher();

            return new StreamProcessor(speedCalculator, watcher);
        }
    }
}
