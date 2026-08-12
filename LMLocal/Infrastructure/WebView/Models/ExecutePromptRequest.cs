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
    }
}

