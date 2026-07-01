using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Sockets;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Settings;
using LMLocal.Infrastructure.Streaming;
using Newtonsoft.Json;

namespace LMLocal.Application.ChatSessionStream
{
    internal interface IStreamProcessor
    {
        Task<StreamCompletionResult> ProcessStreamAsync(Stream stream, CancellationToken cancellationToken, Func<TextStreamChunk, TokenGenerationStats, Task> onChunk, int batchIntervalMs = 50);
    }

    internal class StreamProcessor : IStreamProcessor
    {
        private readonly ITokenSpeedCalculator _tokenSpeedCalculator;
        private readonly ISettingsManager _settingsManager;

        private readonly Dictionary<int, (string CallId, string FunctionName)> _toolCallMetadata =
            new Dictionary<int, (string CallId, string FunctionName)>();

        private readonly List<string> _rawToolCallBlocks = new List<string>();

        public StreamProcessor(
            ITokenSpeedCalculator tokenSpeedCalculator,
            ISettingsManager settingsManager)
        {
            _tokenSpeedCalculator = tokenSpeedCalculator ?? throw new ArgumentNullException(nameof(tokenSpeedCalculator));
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
        }

        public async Task<StreamCompletionResult> ProcessStreamAsync(
            Stream stream,
            CancellationToken cancellationToken,
            Func<TextStreamChunk, TokenGenerationStats, Task> onChunk,
            int batchIntervalMs = 50)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _rawToolCallBlocks.Clear();
            _toolCallMetadata.Clear();

            var fullResponse = new StringBuilder();
            var contentBuffer = new StringBuilder();
            var reasoningBuffer = new StringBuilder();
            var toolCallBuffers = new Dictionary<int, StringBuilder>();

            var result = new StreamCompletionResult
            {
                TokenUsage = new TokenUsageMetadata()
            };

            int currentTokens = 0;

            var syncLock = new object();
            bool isReading = true;

            var cancelRegistration = cancellationToken.Register(() => stream.Close());

            // Create a new parser instance for this stream processing session
            var parser = new LlmSseParser();

            try
            {
                var consumerTask = Task.Run(async () =>
                {
                    while (true)
                    {
                        TextStreamChunk chunkToSend = null;
                        TokenGenerationStats statsToSend = default;
                        bool done = false;

                        lock (syncLock)
                        {
                            if (reasoningBuffer.Length > 0)
                            {
                                var t = reasoningBuffer.ToString();
                                reasoningBuffer.Clear();
                                chunkToSend = new TextStreamChunk(t, ChunkKind.Reasoning);
                            }
                            else if (contentBuffer.Length > 0)
                            {
                                var t = contentBuffer.ToString();
                                contentBuffer.Clear();
                                chunkToSend = new TextStreamChunk(t, ChunkKind.Content);
                            }

                            statsToSend = new TokenGenerationStats(currentTokens, _tokenSpeedCalculator.GetTokensPerSecond());
                            done = !isReading && reasoningBuffer.Length == 0 && contentBuffer.Length == 0;
                        }

                        if (chunkToSend != null && !chunkToSend.IsEmpty && onChunk != null)
                        {
                            await onChunk(chunkToSend, statsToSend).ConfigureAwait(false);
                        }

                        if (done || cancellationToken.IsCancellationRequested)
                        {
                            break;
                        }

                        try
                        {
                            await Task.Delay(batchIntervalMs, cancellationToken).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                }, cancellationToken);

                using (var reader = new StreamReader(stream))
                {
                    try
                    {
                        int timeoutSeconds = _settingsManager.Current.StreamInactivityTimeoutSeconds;
                        if (stream.CanTimeout)
                        {
                            stream.ReadTimeout = timeoutSeconds > 0 ? timeoutSeconds * 1000 : Timeout.Infinite;
                        }

                        // Deliberately using sync ReadLine with ReadTimeout instead of ReadLineAsync,
                        // because ReadLineAsync ignores NetworkStream.ReadTimeout on .NET Framework 4.7.2
#pragma warning disable VSTHRD103
                        string line;
                        while ((line = reader.ReadLine()) != null)
#pragma warning restore VSTHRD103
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            if (string.IsNullOrWhiteSpace(line))
                                continue;

                            var chunks = parser.ExtractDeltas(line);

                            lock (syncLock)
                            {
                                foreach (var chunk in chunks)
                                {
                                    if (chunk is TextStreamChunk textChunk)
                                    {
                                        switch (textChunk.Kind)
                                        {
                                            case ChunkKind.Reasoning:
                                                reasoningBuffer.Append(textChunk.Text);
                                                break;
                                            case ChunkKind.Content:
                                                contentBuffer.Append(textChunk.Text);
                                                fullResponse.Append(textChunk.Text);
                                                break;
                                            case ChunkKind.ToolCallArguments:

                                                int bufferIndex = textChunk.ToolCallIndex ?? 0;
                                                if (!toolCallBuffers.ContainsKey(bufferIndex))
                                                    toolCallBuffers[bufferIndex] = new StringBuilder();
                                                toolCallBuffers[bufferIndex].Append(textChunk.Text);
                                                break;
                                            case ChunkKind.ToolCallRaw:
                                                _rawToolCallBlocks.Add(textChunk.Text);
                                                break;
                                        }

                                        currentTokens++;
                                        _tokenSpeedCalculator.Update(currentTokens);

                                    }
                                    else if (chunk is ToolCallMetadataChunk metadata)
                                    {
                                        _toolCallMetadata[metadata.Index] = (metadata.CallId, metadata.FunctionName);

                                        if (!toolCallBuffers.ContainsKey(metadata.Index))
                                            toolCallBuffers[metadata.Index] = new StringBuilder();

                                        if (!string.IsNullOrEmpty(metadata.InitialArguments))
                                        {
                                            toolCallBuffers[metadata.Index].Append(metadata.InitialArguments);
                                        }
                                    }
                                    else if (chunk is CompletionStreamChunk completion)
                                    {
                                        if (!string.IsNullOrEmpty(completion.FinishReason))
                                            result.FinishReason = completion.FinishReason;

                                        if (completion.TotalTokens.HasValue)
                                            result.TokenUsage.TotalTokens = completion.TotalTokens;

                                        if (completion.PromptTokens.HasValue)
                                            result.TokenUsage.PromptTokens = completion.PromptTokens;

                                        if (completion.CompletionTokens.HasValue)
                                            result.TokenUsage.CompletionTokens = completion.CompletionTokens;

                                        if (completion.ReasoningTokens.HasValue)
                                            result.TokenUsage.ReasoningTokens = completion.ReasoningTokens;

                                        if (!string.IsNullOrEmpty(completion.Refusal))
                                            result.RefusalReason = completion.Refusal;

                                        if (!string.IsNullOrEmpty(completion.SystemFingerprint))
                                            result.SystemFingerprint = completion.SystemFingerprint;
                                    }
                                }
                            }
                        }
                    }
                    finally
                    {
                        lock (syncLock)
                        {
                            isReading = false;
                        }

                        cancelRegistration.Dispose();
                    }
                }

                await consumerTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException ex)
            {
                InternalLogger.Info($"Stream canceled by user: {ex.Message}");
                result.WasCancelled = true;
            }
            catch (ObjectDisposedException ex)
            {
                InternalLogger.Info($"Stream closed by user cancellation: {ex.Message}");
                result.WasCancelled = true;
            }
            catch (IOException ex) when (ex.InnerException is SocketException socketEx && socketEx.SocketErrorCode == SocketError.TimedOut)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    InternalLogger.Info("Stream canceled by user (timeout exception after cancellation)");
                    result.WasCancelled = true;
                }
                else
                {
                    InternalLogger.Info($"Stream read timeout: {ex.Message}");
                    result.ErrorMessage = "Stream read timeout";
                    result.WasCancelled = false;
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"Error in StreamProcessor: {ex.Message}", ex);
                result.ErrorMessage = ex.Message;
                result.WasCancelled = true;
            }
            finally
            {
                stream?.Close();
            }

            result.ContentResponse = fullResponse.ToString();

            foreach (var rawBlock in _rawToolCallBlocks)
            {
                ParseRawToolCallBlock(rawBlock, _toolCallMetadata, toolCallBuffers);
                InternalLogger.Info($"Parse raw xml tool block completed");
            }

            var toolCalls = new List<ToolCallRecord>();
            foreach (var bufferEntry in toolCallBuffers.OrderBy(kvp => kvp.Key))
            {
                int index = bufferEntry.Key;
                string argumentsJson = bufferEntry.Value.ToString();

                if (_toolCallMetadata.TryGetValue(index, out var metadata))
                {
                    toolCalls.Add(new ToolCallRecord
                    {
                        Index = index,
                        CallId = metadata.CallId,
                        FunctionName = metadata.FunctionName,
                        ArgumentsJson = argumentsJson
                    });
                }
                else
                {
                    InternalLogger.Warn($"Missing metadata for tool call at index {index}. Arguments: {argumentsJson}");
                }
            }

            if (toolCalls.Count > 0)
            {
                result.ToolCalls = toolCalls.AsReadOnly();
            }
            else
            {
                result.ToolCalls = new List<ToolCallRecord>().AsReadOnly();
            }

            return result;
        }

        /// <summary>
        /// Parses a raw tool call block like &lt;tool_call&gt;function_name arguments&lt;/tool_call&gt;
        /// and extracts function name and arguments into the tool call buffers.
        /// </summary>
        private void ParseRawToolCallBlock(string rawBlock,
            Dictionary<int, (string, string)> toolCallMetadata,
            Dictionary<int, StringBuilder> toolCallBuffers)
        {
            const string ToolCallStart = "<tool_call>";
            const string ToolCallEnd = "</tool_call>";

            string trimmed = rawBlock.Trim();
            if (!trimmed.StartsWith(ToolCallStart) || !trimmed.EndsWith(ToolCallEnd))
            {
                InternalLogger.Warn($"Invalid tool call block: {rawBlock}");
                return;
            }

            int contentStart = ToolCallStart.Length;
            int contentEnd = trimmed.Length - ToolCallEnd.Length;
            string inner = trimmed.Substring(contentStart, contentEnd - contentStart);

            if (string.IsNullOrWhiteSpace(inner))
            {
                return;
            }

            var funcMatch = Regex.Match(inner, @"<function\s*=\s*([^>]+)>");
            if (!funcMatch.Success)
            {
                InternalLogger.Warn($"No <function=...> in tool call block: {inner}");
                return;
            }

            string functionName = funcMatch.Groups[1].Value;
            int argsStart = funcMatch.Index + funcMatch.Length;
            int endFunc = inner.IndexOf("</function>", argsStart);
            string arguments = (endFunc != -1)
                ? inner.Substring(argsStart, endFunc - argsStart)
                : inner.Substring(argsStart);
            arguments = arguments.Trim();

            int toolIndex = toolCallMetadata.Count;
            toolCallMetadata[toolIndex] = ($"call_{toolIndex}", functionName);
            if (!toolCallBuffers.ContainsKey(toolIndex))
            {
                toolCallBuffers[toolIndex] = new StringBuilder();
            }

            if (!string.IsNullOrEmpty(arguments))
            {
                string argumentsJson = ConvertToolParametersToJson(arguments);
                toolCallBuffers[toolIndex].Append(argumentsJson);
            }

            InternalLogger.Info($"[StreamProcessor] Parsed tool: {functionName}, args length={arguments.Length}");
        }

        private string ConvertToolParametersToJson(string xmlParameters)
        {
            var dict = new Dictionary<string, string>();
            var matches = Regex.Matches(xmlParameters, @"<parameter=([^>]+)>([\s\S]*?)</parameter>");
            foreach (Match m in matches)
            {
                string name = m.Groups[1].Value;
                string value = m.Groups[2].Value.Trim();
                dict[name] = value;
            }
            return JsonConvert.SerializeObject(dict);
        }
    }
}
