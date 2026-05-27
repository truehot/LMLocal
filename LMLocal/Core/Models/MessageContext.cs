using System.Collections.Generic;
using System.Linq;

namespace LMLocal.Core.Models
{
    internal class MessageContext
    {
        public ChatMessage[] Input { get; }
        public string SystemPrompt { get; }

        public MessageContext(IEnumerable<ChatMessage> input, string systemPrompt = null)
        {
            Input = input.ToArray();
            SystemPrompt = systemPrompt;
        }
    }
}
