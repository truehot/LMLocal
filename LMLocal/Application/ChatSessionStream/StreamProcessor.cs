using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Streaming;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LMLocal.Application.ChatSessionStream
{
    /// <summary>
    /// Parses an SSE response stream.
    /// </summary>
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
                                    else if (chunk is ErrorStreamChunk errChunk)
                                    {
                                        result.ErrorMessage = errChunk.Message;
                                        result.ErrorType = errChunk.ErrorType;
                                        result.ErrorCode = errChunk.ErrorCode;
                                        result.FinishReason = "error";
                                        isReading = false;
                                    }

                                    else if (chunk is CompletionStreamChunk completion)
                                    {
                                        if (!string.IsNullOrEmpty(completion.FinishReason) && string.IsNullOrEmpty(result.FinishReason))
                                        {
                                            result.FinishReason = completion.FinishReason; //register once
                                        }

                                        if (completion.TotalTokens.HasValue)
                                            result.TokenUsage.TotalTokens = completion.TotalTokens;

                                        if (completion.PromptTokens.HasValue)
                                            result.TokenUsage.PromptTokens = completion.PromptTokens;

                                        if (completion.CompletionTokens.HasValue)
                                            result.TokenUsage.CompletionTokens = completion.CompletionTokens;

                                        if (completion.ReasoningTokens.HasValue)
                                            result.TokenUsage.ReasoningTokens = completion.ReasoningTokens;

                                        if (completion.CachedTokens.HasValue)
                                            result.TokenUsage.CachedTokens = completion.CachedTokens;

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

                result.TokensPerSecond = _tokenSpeedCalculator.GetAverageTokensPerSecond();
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

            var toolCalls = new List<ToolCallRecord>();
            foreach (var bufferEntry in toolCallBuffers.OrderBy(kvp => kvp.Key))
            {
                int index = bufferEntry.Key;
                string argumentsJson = bufferEntry.Value.ToString();

                if (_toolCallMetadata.TryGetValue(index, out var metadata))
                {
                    bool isValid = IsValidJson(argumentsJson);

                    if (!isValid)
                    {
                        InternalLogger.Warn($"[StreamProcessor] Invalid tool arguments for '{metadata.FunctionName}' (id: {metadata.CallId})");
                    }

                    toolCalls.Add(new ToolCallRecord
                    {
                        Index = index,
                        CallId = metadata.CallId,
                        FunctionName = metadata.FunctionName,
                        ArgumentsJson = isValid ? argumentsJson : "{}",
                        IsInvalid = !isValid
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
        /// Validates that accumulated tool arguments are a JSON object. 
        /// </summary>
        private static bool IsValidJson(string argumentsJson)
        {
            if (string.IsNullOrWhiteSpace(argumentsJson))
                return true;

            try
            {
                JObject.Parse(argumentsJson);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}







