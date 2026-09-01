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

            return request;
        }

        public SendChatRequest BuildRequest(MessageContext messageContext, ModelContext modelContext, bool stream, IReadOnlyList<ToolDefinition> tools)
        {
            if (messageContext == null) throw new ArgumentNullException(nameof(messageContext));
            if (modelContext == null) throw new ArgumentNullException(nameof(modelContext));

            var request = CreateRequest(messageContext, modelContext, stream);
            AddTools(request, tools);
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
                    ToolCalls = ConvertToolCalls(msg.ToolCalls)
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
                InternalLogger.Warn($"ApiRequestBuilder: failed to add tools to request: {ex.Message}");
            }
        }

        private static List<ToolCall> ConvertToolCalls(object toolCalls)
        {
            if (toolCalls == null)
                return null;

            if (toolCalls is List<ToolCall> typed)
                return typed;

            if (toolCalls is JArray jArray)
                return jArray.ToObject<List<ToolCall>>();

            return null;
        }
    }
}
