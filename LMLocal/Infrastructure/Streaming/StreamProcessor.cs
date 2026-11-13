using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Core.Common;
using LMLocal.Core.Models;

namespace LMLocal.Infrastructure.Streaming
{
    /// <summary>
    /// Default <see cref="IStreamProcessor"/>: reads the SSE stream line by line and collects content, reasoning and tool calls.
    /// </summary>
    internal class StreamProcessor : IStreamProcessor
    {
        /// <summary>Characters of the rejected arguments written to the log.</summary>
        private const int AbortLogPreviewLength = 200;

        private readonly ITokenSpeedCalculator _tokenSpeedCalculator;
        private readonly ISettingsManager _settingsManager;
        private readonly IToolCallArgumentsGuard _argumentsGuard;

        /// <param name="argumentsGuard">Optional; when it rejects the arguments of a tool call, reading stops and the call is marked invalid.</param>
        public StreamProcessor(
            ITokenSpeedCalculator tokenSpeedCalculator,
            ISettingsManager settingsManager,
            IToolCallArgumentsGuard argumentsGuard = null)
        {
            _tokenSpeedCalculator = tokenSpeedCalculator ?? throw new ArgumentNullException(nameof(tokenSpeedCalculator));
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _argumentsGuard = argumentsGuard;
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
            var toolCalls = new ToolCallAccumulator(_argumentsGuard);
            var tokenEstimator = new StreamedTokenEstimator();
            var parser = new LlmSseParser();
            var syncLock = new object();

            var result = new StreamCompletionResult
            {
                TokenUsage = new TokenUsageMetadata()
            };

            bool isReading = true;

            try
            {
                using (var cancelRegistration = cancellationToken.Register(() => stream.Close()))
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

                                statsToSend = new TokenGenerationStats(tokenEstimator.EstimatedTokens, _tokenSpeedCalculator.GetTokensPerSecond());
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
                                bool toolCallAborted = false;

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
                                                    toolCallAborted = !toolCalls.AppendArguments(textChunk.ToolCallIndex ?? 0, textChunk.Text);
                                                    break;
                                            }

                                            double estimatedTokens = tokenEstimator.Add(textChunk.Text);

                                            _tokenSpeedCalculator.Add(textChunk.Text.Length, estimatedTokens);

                                        }
                                        else if (chunk is ToolCallMetadataChunk metadata)
                                        {
                                            toolCallAborted = !toolCalls.SetMetadata(metadata.Index, metadata.CallId, metadata.FunctionName, metadata.ExtraContentJson, metadata.InitialArguments);

                                            if (!string.IsNullOrEmpty(metadata.InitialArguments))
                                            {
                                                double argumentsEstimate = tokenEstimator.Add(metadata.InitialArguments);
                                                _tokenSpeedCalculator.Add(metadata.InitialArguments.Length, argumentsEstimate);
                                            }
                                        }
                                        else if (chunk is ErrorStreamChunk errChunk)
                                        {
                                            result.ErrorMessage = errChunk.Message;
                                            result.ErrorType = errChunk.ErrorType;
                                            result.ErrorCode = errChunk.ErrorCode;
                                            result.FinishReason = "error";
                                            result.StopReason = StreamStopReason.Error;
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

                                        if (toolCallAborted)
                                            break;
                                    }
                                }

                                if (toolCallAborted)
                                {
                                    int index = toolCalls.AbortedIndex ?? 0;
                                    InternalLogger.Warn($"[StreamProcessor] Tool call '{toolCalls.GetFunctionName(index)}' (index {index}) aborted: {toolCalls.AbortReason}. Arguments: {toolCalls.GetArgumentsPreview(index, AbortLogPreviewLength)}");
                                    result.StopReason = StreamStopReason.ToolCallAborted;
                                    break;
                                }
                            }
                        }
                        finally
                        {
                            lock (syncLock)
                            {
                                isReading = false;
                            }
                        }
                    }

                    await consumerTask.ConfigureAwait(false);

                    result.TokensPerSecond = _tokenSpeedCalculator.GetAverageTokensPerSecond();
                }
            }
            catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
            {
                InternalLogger.Info($"Stream canceled by user: {ex.Message}");
                result.StopReason = StreamStopReason.Cancelled;
            }
            catch (ObjectDisposedException ex) when (cancellationToken.IsCancellationRequested)
            {
                InternalLogger.Info($"Stream closed by user cancellation: {ex.Message}");
                result.StopReason = StreamStopReason.Cancelled;
            }
            catch (IOException ex) when (ex.InnerException is SocketException socketEx && socketEx.SocketErrorCode == SocketError.TimedOut)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    InternalLogger.Info("Stream canceled by user (timeout exception after cancellation)");
                    result.StopReason = StreamStopReason.Cancelled;
                }
                else
                {
                    InternalLogger.Info($"Stream read timeout: {ex.Message}");
                    result.ErrorMessage = "Stream read timeout";
                    result.StopReason = StreamStopReason.Error;
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"Error in StreamProcessor: {ex.Message}", ex);
                result.ErrorMessage = ExceptionFormatter.Format(ex);
                result.StopReason = cancellationToken.IsCancellationRequested ? StreamStopReason.Cancelled : StreamStopReason.Error;
            }
            finally
            {
                stream?.Close();
            }

            result.ContentResponse = fullResponse.ToString();
            result.ToolCalls = toolCalls.Build();

            return result;
        }
    }
}
