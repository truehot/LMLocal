namespace LMLocal.Core.Models
{
    /// <summary>
    /// Protocol-neutral tool call kept in the chat history; the wire shape is mapped in Infrastructure.
    /// </summary>
    public class ChatToolCall
    {
        public string Id { get; set; }

        public string Name { get; set; }

        /// <summary>
        /// Arguments as the JSON string the model produced.
        /// </summary>
        public string Arguments { get; set; }

        /// <summary>
        /// Opaque per-call provider payload (Gemini extra_content) as raw JSON, or null.
        /// </summary>
        public string ExtraContentJson { get; set; }
    }
}
