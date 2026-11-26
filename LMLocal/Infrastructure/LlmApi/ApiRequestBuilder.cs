using System;
using System.Collections.Generic;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.LlmApi.Converter;
using LMLocal.Infrastructure.LlmApi.Requests;
using LMLocal.Infrastructure.Tooling;
using Newtonsoft.Json.Linq;

namespace LMLocal.Infrastructure.LlmApi
{
    internal interface IApiRequestBuilder
    {
        /// <summary>
        /// Builds a request using the main chat tool queue.
        /// </summary>
        SendChatRequest BuildRequest(MessageContext messageContext, ModelContext modelContext, bool stream, bool useTools = true);

        /// <summary>
        /// Builds a request with an explicit tool set.
        /// </summary>
        SendChatRequest BuildRequest(MessageContext messageContext, ModelContext modelContext, bool stream, IReadOnlyList<ToolDefinition> tools);
    }

    internal class ApiRequestBuilder : IApiRequestBuilder
    {
        private readonly ISettingsManager _settingsManager;
        private readonly IToolQueueProvider _toolQueueProvider;

        public ApiRequestBuilder(ISettingsManager settingsManager, IToolQueueProvider toolQueueProvider)
        {
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _toolQueueProvider = toolQueueProvider ?? throw new ArgumentNullException(nameof(toolQueueProvider));
        }

        public SendChatRequest BuildRequest(MessageContext messageContext, ModelContext modelContext, bool stream, bool useTools = true)
        {
            if (messageContext == null) throw new ArgumentNullException(nameof(messageContext));
            if (modelContext == null) throw new ArgumentNullException(nameof(modelContext));

            var request = CreateRequest(messageContext, modelContext, stream);

            if (_settingsManager.Current.EnableAiTools && useTools)
            {
                AddTools(request, _toolQueueProvider.GetMainQueue().Definitions);
            }

            ApplyReasoningEchoGate(request);
            return request;
        }

        public SendChatRequest BuildRequest(MessageContext messageContext, ModelContext modelContext, bool stream, IReadOnlyList<ToolDefinition> tools)
        {
            if (messageContext == null) throw new ArgumentNullException(nameof(messageContext));
            if (modelContext == null) throw new ArgumentNullException(nameof(modelContext));

            var request = CreateRequest(messageContext, modelContext, stream);
            AddTools(request, tools);
            ApplyReasoningEchoGate(request);
            return request;
        }

        private static SendChatRequest CreateRequest(MessageContext messageContext, ModelContext modelContext, bool stream)
        {
            var messages = new List<Message>();

            foreach (var msg in messageContext.Input)
            {
                var apiMessage = new Message
                {
                    Role = msg.Role,
                    Content = msg.Content,
                    ToolCallId = msg.ToolCallId,
                    ToolCalls = ToolCallProjection.ToWire(msg.ToolCalls),
                    ReasoningContent = modelContext.EchoReasoning ? msg.ReasoningContent : null
                };
                messages.Add(apiMessage);
            }

            return new SendChatRequest
            {
                Model = modelContext.ModelId,
                Messages = messages,
                Stream = stream,
                Temperature = modelContext.Temperature,
                TopP = modelContext.TopP,
                MaxCompletionTokens = modelContext.MaxOutputTokens,
                PresencePenalty = modelContext.PresencePenalty,
                FrequencyPenalty = modelContext.FrequencyPenalty,
                ReasoningEffort = modelContext.Reasoning,
                StreamOptions = stream ? new StreamOptions { IncludeUsage = stream } : null
            };
        }

        private static void AddTools(SendChatRequest request, IReadOnlyList<ToolDefinition> tools)
        {
            if (tools == null || tools.Count == 0)
                return;

            try
            {
                var openAiTools = ToolDefinitionConverter.ConvertToOpenAiFormat(tools);
                request.Tools = openAiTools;
                request.ToolChoice = "auto";
                request.ParallelToolCalls = true;
            }
            catch (Exception ex)
            {
                InternalLogger.Warn("ApiRequestBuilder: failed to add tools to request", ex);
            }
        }

        /// <summary>
        /// reasoning_content goes back only with the tools of the request, and never when the model profile opted out
        /// (the messages were already filtered in CreateRequest). The echoed size is kept on the request for the usage log.
        /// </summary>
        private static void ApplyReasoningEchoGate(SendChatRequest request)
        {
            bool hasTools = request.Tools != null && request.Tools.Count > 0;
            int messages = 0, chars = 0;

            foreach (var message in request.Messages)
            {
                if (string.IsNullOrEmpty(message?.ReasoningContent))
                    continue;

                if (!hasTools)
                {
                    message.ReasoningContent = null;
                    continue;
                }

                messages++;
                chars += message.ReasoningContent.Length;
            }

            request.ReasoningEchoChars = chars;

            if (messages > 0)
                InternalLogger.Info($"[ReasoningEcho] attached to {messages} message(s), {chars} chars, tools: {request.Tools.Count}.");
        }

    }
}
