using System;
using System.Threading.Tasks;
using LMLocal.Application.Tool;

namespace LMLocal.Infrastructure.WebView.Messaging
{
    /// <summary>Bridges tool activity events to WebView2: steps to StreamToolStep, token usage to StreamToolTokens.</summary>
    internal sealed class ToolActivityForwarder : IProgress<ToolActivityEvent>
    {
        private readonly Func<WebView2ScriptMessage, Task> _onMessage;
        private readonly string _activityId;

        public ToolActivityForwarder(string activityId, Func<WebView2ScriptMessage, Task> onMessage)
        {
            _activityId = activityId;
            _onMessage = onMessage ?? throw new ArgumentNullException(nameof(onMessage));
        }

        public void Report(ToolActivityEvent e)
        {
            if (e == null)
                return;

            if (e.IsTokenEvent)
            {
                _ = _onMessage(new WebView2ToolTokensMessage
                {
                    Type = WebView2MessageType.StreamToolTokens,
                    CallId = _activityId,
                    AgentName = e.AgentName,
                    Tokens = e.Tokens.Value,
                    TokensPerSecond = e.TokensPerSecond ?? 0
                });

                return;
            }

            if (string.IsNullOrEmpty(e.ToolName) && string.IsNullOrEmpty(e.Message))
                return;

            var message = new WebView2ToolCallMessage
            {
                Type = WebView2MessageType.StreamToolStep,
                FunctionName = e.ToolName,
                CallId = _activityId,
                Message = e.Message ?? e.ToolName,
                IsError = false,
                Step = e.Step
            };

            _ = _onMessage(message);
        }
    }
}
