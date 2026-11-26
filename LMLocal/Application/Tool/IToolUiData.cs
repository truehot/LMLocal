namespace LMLocal.Application.Tool
{
    /// <summary>
    /// Optional contract for a tool response that also carries data for the WebView.
    /// </summary>
    public interface IToolUiData
    {
        /// <summary>
        /// Data for the UI only, like the completion message: not sent to the model and not persisted.
        /// Null when there is nothing to show or when the tool reported a failure.
        /// </summary>
        object UiData { get; }
    }
}
