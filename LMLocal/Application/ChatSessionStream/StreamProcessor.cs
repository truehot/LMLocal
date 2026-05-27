using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Streaming;

namespace LMLocal.Application.ChatSessionStream
{
    internal interface IStreamProcessor
    {
        Task<StreamCompletionResult> ProcessStreamAsync(Stream stream, CancellationToken cancellationToken, Func<TextStreamChunk, TokenGenerationStats, Task> onChunk, int batchIntervalMs = 50);
    }

    internal class StreamProcessor : IStreamProcessor
    {
        private readonly ITokenSpeedCalculator _tokenSpeedCalculator;
        private readonly IStreamInactivityWatcher _inactivityWatcher;

        private readonly Dictionary<int, (string CallId, string FunctionName)> _toolCallMetadata =
            new Dictionary<int, (string CallId, string FunctionName)>();

        public StreamProcessor(
            ITokenSpeedCalculator tokenSpeedCalculator,
            IStreamInactivityWatcher inactivityWatcher)
        {
            _tokenSpeedCalculator = tokenSpeedCalculator ?? throw new ArgumentNullException(nameof(tokenSpeedCalculator));
            _inactivityWatcher = inactivityWatcher ?? throw new ArgumentNullException(nameof(inactivityWatcher));
        }

        public async Task<StreamCompletionResult> ProcessStreamAsync(
            Stream stream,
            CancellationToken cancellationToken,
            Func<TextStreamChunk, TokenGenerationStats, Task> onChunk,
            int batchIntervalMs = 50)
        {
            cancellationToken.ThrowIfCancellationRequested();

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

            try
            {
                var inactivityWatcherTask = _inactivityWatcher.WatchAsync(cancellationToken);

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
                            _inactivityWatcher.SignalCompletion();
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
                        string line;
                        while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            _inactivityWatcher.SignalActivity();

                            if (string.IsNullOrWhiteSpace(line))
                                continue;

                            var chunk = LlmSseParser.ExtractDelta(line);

                            if (chunk == null)
                                continue;

                            lock (syncLock)
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
                    finally
                    {
                        lock (syncLock)
                        {
                            isReading = false;
                        }

                        cancelRegistration.Dispose();
                        _inactivityWatcher.SignalCompletion();
                    }
                }

                await consumerTask.ConfigureAwait(false);
                await inactivityWatcherTask.ConfigureAwait(false);
            }
            catch (ObjectDisposedException ex)
            {
                if (_inactivityWatcher.IsTimeout)
                {
                    InternalLogger.Info($"Stream canceled due to inactivity timeout: {ex.Message}");
                    result.ErrorMessage = "Stream canceled due to inactivity timeout";
                }
                else
                {
                    InternalLogger.Info($"Stream canceled by user: {ex.Message}");
                }
                result.WasCancelled = true;
            }
            catch (OperationCanceledException ex)
            {
                if (_inactivityWatcher.IsTimeout)
                {
                    InternalLogger.Info($"Stream canceled due to inactivity timeout: {ex.Message}");
                    result.ErrorMessage = "Stream canceled due to inactivity timeout";
                }
                else
                {
                    InternalLogger.Info($"Stream canceled by user: {ex.Message}");
                }
                result.WasCancelled = true;
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
                _inactivityWatcher.SignalCompletion();
            }

            result.ContentResponse = fullResponse.ToString();

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
    }
}
