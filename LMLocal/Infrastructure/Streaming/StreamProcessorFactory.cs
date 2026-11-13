using LMLocal.Application.Abstractions.Ports;

namespace LMLocal.Infrastructure.Streaming
{
    /// <summary>
    /// Default <see cref="IStreamProcessorFactory"/>: a new processor with its own speed calculator for every round,
    /// with the tool-call arguments guard of the round's model.
    /// </summary>
    internal class StreamProcessorFactory : IStreamProcessorFactory
    {
        private readonly ISettingsManager _settingsManager;
        private readonly IToolCallArgumentsGuardFactory _argumentsGuardFactory;

        public StreamProcessorFactory(ISettingsManager settingsManager, IToolCallArgumentsGuardFactory argumentsGuardFactory)
        {
            _settingsManager = settingsManager ?? throw new System.ArgumentNullException(nameof(settingsManager));
            _argumentsGuardFactory = argumentsGuardFactory ?? throw new System.ArgumentNullException(nameof(argumentsGuardFactory));
        }

        public IStreamProcessor Create(string modelId)
        {
            int windowSeconds = _settingsManager.WindowSeconds;
            var speedCalculator = new TokenSpeedCalculator(windowSeconds: windowSeconds);

            return new StreamProcessor(speedCalculator, _settingsManager, _argumentsGuardFactory.Create(modelId));
        }
    }
}
