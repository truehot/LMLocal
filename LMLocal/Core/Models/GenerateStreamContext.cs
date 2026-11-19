using System;
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

        /// <summary>
        /// True when the next round retries a request that failed on context overflow.
        /// </summary>
        public bool IsContextOverflowRetry { get; set; }

        /// <summary>
        /// Set when the session must replace the last exchange before its first round, one of
        /// <see cref="ReplaceLastModes"/>. Null for a normal prompt.
        /// </summary>
        public string ReplaceLast { get; set; }

        /// <summary>
        /// Prompt of the last user message the UI is showing. The replacement is refused when the history ends with
        /// another prompt.
        /// </summary>
        public string ReplaceLastPrompt { get; set; }
    }

    /// <summary>
    /// Accepted values of <see cref="GenerateStreamContext.ReplaceLast"/>.
    /// </summary>
    public static class ReplaceLastModes
    {
        /// <summary>Keeps the last user message and drops everything after it, so the answer is generated again.</summary>
        public const string Answer = "answer";

        /// <summary>Drops the last user message too, so a new prompt replaces the whole exchange.</summary>
        public const string Exchange = "exchange";

        /// <summary>
        /// Maps a known mode to whether the last user message is kept.
        /// </summary>
        public static bool TryGetKeepUserMessage(string mode, out bool keepUserMessage)
        {
            if (string.Equals(mode, Answer, StringComparison.Ordinal))
            {
                keepUserMessage = true;
                return true;
            }

            if (string.Equals(mode, Exchange, StringComparison.Ordinal))
            {
                keepUserMessage = false;
                return true;
            }

            keepUserMessage = false;
            return false;
        }
    }
}
