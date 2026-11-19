using System.Collections.Generic;

namespace LMLocal.Models
{
    public class ExecutePromptRequest
    {
        /// <summary>
        /// Prompt for the model to execute. Can be a simple string or a structured object depending on the model's requirements.
        /// </summary>
        public string Prompt { get; set; }

        /// <summary>
        /// Indicates whether to include the content in the response.
        /// </summary>
        public bool IncludeContent { get; set; }

        /// <summary>
        /// Additional prompt for the model to execute, which can be used for follow-up instructions or clarifications. Optional.
        /// </summary>
        public string AdditionalPrompt { get; set; }

        /// <summary>
        /// ID of the model to execute the prompt on.
        /// </summary>
        public string ModelId { get; set; }

        /// <summary>
        /// Temperature setting for the model. Controls randomness of output. Optional.
        /// </summary>
        public double? Temperature { get; set; }

        /// <summary>
        /// Optional list of base64 image data URLs (data:image/...) attached to the user message as multimodal content.
        /// </summary>
        public List<string> Images { get; set; }

        /// <summary>
        /// Null for a normal prompt. "answer" regenerates the last exchange (its user message is kept),
        /// "exchange" replaces it, so the prompt of this request is asked instead.
        /// </summary>
        public string ReplaceLast { get; set; }

        /// <summary>
        /// Prompt of the last user message the UI is showing. Sent with <see cref="ReplaceLast"/> so the backend can
        /// refuse the replacement when the history ends with another prompt.
        /// </summary>
        public string ReplaceLastPrompt { get; set; }
    }
}

