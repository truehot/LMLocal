namespace LMLocal.Core.Models
{

    /// <summary>
    /// Contains model configuration parameters for chat requests.
    /// </summary>
    internal class ModelContext
    {
        public string ModelId { get; }
        public double? Temperature { get; }
        public double? TopP { get; }
        public int? TopK { get; }
        public double? MinP { get; }
        public double? RepeatPenalty { get; }
        public double? PresencePenalty { get; }
        public double? FrequencyPenalty { get; }
        public int? MaxOutputTokens { get; }
        public int? ContextLength { get; }
        public string Reasoning { get; }

        public ModelContext(
            string modelId,
            double? temperature = null,
            double? topP = null,
            int? topK = null,
            double? minP = null,
            double? repeatPenalty = null,
            double? presencePenalty = null,
            double? frequencyPenalty = null,
            int? maxOutputTokens = null,
            int? contextLength = null,
            string reasoning = null)
        {
            ModelId = modelId;
            Temperature = temperature;
            TopP = topP;
            TopK = topK;
            MinP = minP;
            RepeatPenalty = repeatPenalty;
            PresencePenalty = presencePenalty;
            FrequencyPenalty = frequencyPenalty;
            MaxOutputTokens = maxOutputTokens;
            ContextLength = contextLength;
            Reasoning = reasoning;
        }
    }

}
