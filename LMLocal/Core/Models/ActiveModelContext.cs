namespace LMLocal.Core.Models
{
    /// <summary>
    /// Tracks the currently active model information.
    /// Singleton - shared across all sessions to maintain model state.
    /// Used by history compactor to determine when model changes.
    /// </summary>
    internal interface IActiveModelContext
    {
        /// <summary>
        /// Gets the current active model ID.
        /// </summary>
        string CurrentModelId { get; }

        /// <summary>
        /// Gets the maximum context length for current model.
        /// </summary>
        int MaxContextLength { get; }

        /// <summary>
        /// Updates the active model with new information.
        /// Called when status is retrieved from LM Studio.
        /// </summary>
        void SetActiveModel(string modelId, int maxContextLength);
    }

    internal class ActiveModelContext : IActiveModelContext
    {
        private readonly object _lock = new object();
        private string _currentModelId;
        private int _maxContextLength;

        public string CurrentModelId
        {
            get
            {
                lock (_lock)
                {
                    return _currentModelId;
                }
            }
        }

        public int MaxContextLength
        {
            get
            {
                lock (_lock)
                {
                    return _maxContextLength;
                }
            }
        }

        public void SetActiveModel(string modelId, int maxContextLength)
        {
            lock (_lock)
            {
                _currentModelId = modelId;
                _maxContextLength = maxContextLength;
            }
        }
    }
}
