using System;
using System.Collections.Generic;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Core.Models.Usage;

namespace LMLocal.Application.Usage
{
    /// <summary>
    /// Sums the usage of the rounds of ONE request: tokens, generation speed, context peak, rounds and tool calls.
    /// </summary>
    internal sealed class UsageAccumulator
    {
        private int? _prompt;
        private int? _completion;
        private int? _cached;
        private int? _reasoning;

        /// <summary>Generation rounds of the request, including the ones that reported no usage.</summary>
        public int Rounds { get; private set; }

        /// <summary>Tool calls requested by the model during the request.</summary>
        public int ToolCalls { get; private set; }

        /// <summary>Input tokens summed over the rounds that reported them.</summary>
        public int? Prompt { get { return _prompt; } }

        /// <summary>Output tokens summed over the rounds that reported them.</summary>
        public int? Completion { get { return _completion; } }

        /// <summary>Cached prompt tokens summed over the rounds that reported them.</summary>
        public int? Cached { get { return _cached; } }

        /// <summary>Reasoning tokens summed over the rounds that reported them.</summary>
        public int? Reasoning { get { return _reasoning; } }

        /// <summary>Largest prompt among the rounds; the closest thing to the context the request reached.</summary>
        public int? ContextPeak { get; private set; }

        /// <summary>Output tokens of the rounds that also have a measured speed.</summary>
        public long GenCompletion { get; private set; }

        /// <summary>Generation time of the same rounds as <see cref="GenCompletion"/>.</summary>
        public long GenMs { get; private set; }

        /// <summary>True when at least one successful round had to be estimated because the provider reported no usage.</summary>
        public bool Estimated { get; private set; }

        /// <summary>
        /// Adds every round of a request.
        /// </summary>
        public void Add(IReadOnlyList<StreamCompletionResult> results)
        {
            if (results == null)
                return;

            for (int i = 0; i < results.Count; i++)
                Add(results[i], null);
        }

        /// <summary>
        /// Adds one round. <paramref name="estimatedPromptTokens"/> is used only when the provider reported no usage for
        /// a round that produced an answer: the caller may pass an estimate of the prompt it sent.
        /// </summary>
        public void Add(StreamCompletionResult result, int? estimatedPromptTokens = null)
        {
            if (result == null)
                return;

            Rounds++;
            ToolCalls += result.ToolCalls != null ? result.ToolCalls.Count : 0;

            var usage = result.TokenUsage;
            bool succeeded = !result.WasCancelled && string.IsNullOrEmpty(result.ErrorMessage);

            int? prompt = usage != null ? usage.PromptTokens : null;
            int? completion = usage != null ? usage.CompletionTokens : null;
            int? cached = usage != null ? usage.CachedTokens : null;
            int? reasoning = usage != null ? usage.ReasoningTokens : null;

            if (usage == null && succeeded)
            {
                completion = EstimateCompletion(result.ContentResponse);
                prompt = estimatedPromptTokens;
                Estimated = true;
            }

            if (prompt.HasValue)
            {
                _prompt = (_prompt ?? 0) + prompt.Value;

                if (!ContextPeak.HasValue || prompt.Value > ContextPeak.Value)
                    ContextPeak = prompt.Value;
            }

            if (completion.HasValue)
                _completion = (_completion ?? 0) + completion.Value;

            if (cached.HasValue)
                _cached = (_cached ?? 0) + cached.Value;

            if (reasoning.HasValue)
                _reasoning = (_reasoning ?? 0) + reasoning.Value;

            if (result.TokensPerSecond > 0 && completion.HasValue && completion.Value > 0)
            {
                GenCompletion += completion.Value;
                GenMs += (long)Math.Round(completion.Value * 1000d / result.TokensPerSecond);
            }
        }

        /// <summary>
        /// Copies the accumulated numbers into a record, leaving the identity fields of the record untouched.
        /// </summary>
        public UsageRecord ApplyTo(UsageRecord record)
        {
            if (record == null)
                return null;

            record.Prompt = Prompt;
            record.Completion = Completion;
            record.Cached = Cached;
            record.Reasoning = Reasoning;
            record.Estimated = Estimated;
            record.Rounds = Rounds;
            record.ToolCalls = ToolCalls;
            record.GenCompletion = GenCompletion;
            record.GenMs = GenMs;
            record.ContextPeak = ContextPeak;

            return record;
        }

        /// <summary>
        /// Rough output size of a round that reported no usage, or null when there is no text to measure.
        /// </summary>
        private static int? EstimateCompletion(string text)
        {
            if (string.IsNullOrEmpty(text))
                return null;

            var estimator = new StreamedTokenEstimator();
            estimator.Add(text);

            return estimator.EstimatedTokens;
        }
    }
}
