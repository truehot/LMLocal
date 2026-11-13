namespace LMLocal.Application.Abstractions.Ports
{
    /// <summary>
    /// Creates instances of IStreamProcessor, which handle the processing of streaming responses from the LM backend.
    /// </summary>
    internal interface IStreamProcessorFactory
    {
        /// <summary>
        /// Creates a processor for one streamed round of the given model.
        /// </summary>
        IStreamProcessor Create(string modelId);
    }
}
