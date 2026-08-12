using System.Collections.Generic;

namespace LMLocal.Core.Models
{
    public class GenerateStreamContext
    {
        /// <summary>
        /// Prompt for the model to execute. Can be a simple string or a structured object depending on the model's requirements.
        /// </summary>
        public string Prompt { get; set; }

        /// <summary>
        /// Active document content to provide context for the model. Optional.
        /// </summary>
        public string ActiveDocumentContent { get; set; }

        /// <summary>
        /// Additional prompt for the model to execute, which can be used for follow-up instructions or clarifications. Optional.
        /// </summary>
        public string AdditionalPrompt { get; set; }

        /// <summary>
        /// Id of the model to execute the prompt on.
        /// </summary>
        public string ModelId { get; set; }

        /// <summary>
        /// Temperature setting for the model. Controls randomness of output. Optional.
        /// </summary>
        public double? Temperature { get; set; }

        /// <summary>
        /// Optional list of base64 image data URLs attached to the user message as multimodal content.
        /// </summary>
        public IReadOnlyList<string> Images { get; set; }
    }
}
