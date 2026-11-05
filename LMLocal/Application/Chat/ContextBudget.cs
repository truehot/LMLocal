using System;
using System.Collections.Generic;
using LMLocal.Core.Common;
using LMLocal.Core.Models;

namespace LMLocal.Application.Chat
{
    /// <summary>
    /// Holds the calibration of the token estimator against what the provider actually reported.
    /// </summary>
    internal interface IContextCalibration
    {
        /// <summary>
        /// Ratio between the real envelope size and the estimated one, clamped to a sane range. 1 when unknown.
        /// </summary>
        double Factor { get; }

        /// <summary>
        /// Context length reported by the provider in the last overflow error, if it did.
        /// </summary>
        int? ErrorContextLength { get; }

        /// <summary>
        /// Real size of what the last request carried outside the history: system prompt, tool definitions and the
        /// chat protocol.
        /// </summary>
        int FixedOverhead { get; }

        /// <summary>
        /// Records the size of the last request envelope and of its non-history part.
        /// </summary>
        void RecordEnvelope(int envelopeEstimate, int fixedOverhead);

        /// <summary>
        /// Records a round that the provider accepted, which is the most reliable calibration source.
        /// </summary>
        void RecordSuccessfulRound(int? promptTokens, int envelopeEstimate);

        /// <summary>
        /// Records an overflow error.
        /// </summary>
        void RecordOverflowError(int? promptTokens, int? contextLength, int envelopeEstimate = 0);

        /// <summary>
        /// Forgets everything when the model changed.
        /// </summary>
        void ResetForModel(string modelId);
    }

    internal sealed class ContextCalibration : IContextCalibration
    {
        /// <summary>Below this the coefficient is not credible: the estimate would be more than twice the real size.</summary>
        internal const double MinFactor = 0.5;

        /// <summary>Above this the coefficient is not credible either: the estimate would be less than a third of the real size.</summary>
        internal const double MaxFactor = 3.0;

        private readonly object _lock = new object();
        private double _factor = 1.0;
        private int? _errorContextLength;
        private int _fixedOverhead;
        private string _modelId;

        public int FixedOverhead
        {
            get
            {
                lock (_lock)
                    return _fixedOverhead;
            }
        }

        public void RecordEnvelope(int envelopeEstimate, int fixedOverhead)
        {
            lock (_lock)
            {
                if (envelopeEstimate > 0)
                    _fixedOverhead = Math.Max(0, fixedOverhead);
            }
        }

        public double Factor
        {
            get
            {
                lock (_lock)
                    return _factor;
            }
        }

        public int? ErrorContextLength
        {
            get
            {
                lock (_lock)
                    return _errorContextLength;
            }
        }

        public void RecordSuccessfulRound(int? promptTokens, int envelopeEstimate)
        {
            if (!promptTokens.HasValue || promptTokens.Value <= 0 || envelopeEstimate <= 0)
                return;

            SetFactor((double)promptTokens.Value / envelopeEstimate);
        }

        public void RecordOverflowError(int? promptTokens, int? contextLength, int envelopeEstimate = 0)
        {
            lock (_lock)
            {
                if (contextLength.HasValue && contextLength.Value > 0)
                    _errorContextLength = contextLength;
            }

            if (!promptTokens.HasValue || promptTokens.Value <= 0 || envelopeEstimate <= 0)
                return;

            SetFactor((double)promptTokens.Value / envelopeEstimate);
        }

        public void ResetForModel(string modelId)
        {
            lock (_lock)
            {
                if (string.Equals(_modelId, modelId, StringComparison.Ordinal))
                    return;

                _factor = 1.0;
                _errorContextLength = null;
                _fixedOverhead = 0;
                _modelId = modelId;
            }
        }

        private void SetFactor(double factor)
        {
            if (double.IsNaN(factor) || double.IsInfinity(factor) || factor <= 0)
                return;

            lock (_lock)
                _factor = Math.Min(MaxFactor, Math.Max(MinFactor, factor));
        }
    }

    /// <summary>
    /// Sizes of the request envelope for one compaction, computed once so the prune pass and the summary call cannot disagree about how much room is left.
    /// </summary>
    internal sealed class ContextBudget
    {
        /// <summary>Share of the window reserved for the answer when the model profile does not say.</summary>
        internal const double DefaultOutputReserveShare = 0.15;

        /// <summary>Margin used when the estimator is not calibrated: without a coefficient it cannot be trusted.</summary>
        internal const double UncalibratedMarginShare = 0.15;

        /// <summary>Margin used once a coefficient exists.</summary>
        internal const double CalibratedMarginShare = 0.07;

        /// <summary>Share of the history budget the summary itself may occupy.</summary>
        internal const double SummaryOutputShare = 0.25;

        private ContextBudget(int contextLength, int outputReserve, int margin, int fixedOverhead, double factor,
            int summaryOutput, int summaryInputLimit)
        {
            ContextLength = contextLength;
            OutputReserve = outputReserve;
            Margin = margin;
            FixedOverhead = fixedOverhead;
            Factor = factor;
            SummaryOutput = summaryOutput;
            SummaryInputLimit = summaryInputLimit;
        }

        /// <summary>Window of the model, in tokens.</summary>
        public int ContextLength { get; }

        /// <summary>Room reserved for the answer.</summary>
        public int OutputReserve { get; }

        /// <summary>Safety margin, larger while the estimator is uncalibrated.</summary>
        public int Margin { get; }

        /// <summary>Real size of what is not history: system prompt, tool definitions, protocol.</summary>
        public int FixedOverhead { get; }

        /// <summary>Calibration coefficient applied to estimates.</summary>
        public double Factor { get; }

        /// <summary>Output budget of the summary call.</summary>
        public int SummaryOutput { get; }

        /// <summary>Input budget of the summary call.</summary>
        public int SummaryInputLimit { get; }

        /// <summary>What the whole envelope may occupy.</summary>
        public int EnvelopeLimit
        {
            get { return Math.Max(1, ContextLength - OutputReserve - Margin); }
        }

        /// <summary>What the history may occupy, once the fixed overhead is taken out.</summary>
        public int HistoryLimit
        {
            get { return Math.Max(1, EnvelopeLimit - Real(FixedOverhead)); }
        }

        /// <summary>
        /// Converts an estimate into the provider's scale.
        /// </summary>
        public int Real(int estimated)
        {
            return RealTokens(estimated, Factor);
        }

        /// <summary>
        /// Converts an estimate into the provider's scale with an explicit coefficient, for the static factory.
        /// </summary>
        private static int RealTokens(int estimated, double factor)
        {
            return Math.Max(0, (int)Math.Round(estimated * factor));
        }


        /// <summary>
        /// Builds the budget for one compaction.
        /// </summary>
        public static ContextBudget Create(
            IContextCalibration calibration,
            int? errorContextLength,
            int? profileContextLength,
            int fallbackContextLength,
            int? maxOutputTokens,
            int fixedOverheadEstimate)
        {
            double factor = calibration?.Factor ?? 1.0;

            int contextLength;
            string source;
            if (errorContextLength.HasValue && errorContextLength.Value > 0)
            {
                contextLength = errorContextLength.Value;
                source = "provider error";
            }
            else if (profileContextLength.HasValue && profileContextLength.Value > 0)
            {
                contextLength = profileContextLength.Value;
                source = "model profile";
            }
            else
            {
                contextLength = fallbackContextLength > 0 ? fallbackContextLength : 16384;
                source = "fallback";
                InternalLogger.Warn($"ContextBudget: window unknown, falling back to {contextLength} tokens.");
            }

            int outputReserve = maxOutputTokens.HasValue && maxOutputTokens.Value > 0
                ? maxOutputTokens.Value
                : (int)(contextLength * DefaultOutputReserveShare);

            double marginShare = factor > 1.0 || factor < 1.0 ? CalibratedMarginShare : UncalibratedMarginShare;
            int margin = (int)(contextLength * marginShare);

            int summaryOutput = Math.Max(1, Math.Min(outputReserve, (int)(Math.Max(1, contextLength - outputReserve - margin) * SummaryOutputShare)));
            int summaryPromptTokens = RealTokens(EnvelopeEstimator.EstimateText(ForcedSummaryPromptTokens()), factor);
            int summaryInputLimit = Math.Max(1, contextLength - summaryOutput - margin - summaryPromptTokens);

            InternalLogger.Info(
                $"ContextBudget: ctx={contextLength} ({source}), k={factor:0.00}, overhead={fixedOverheadEstimate}, " +
                $"reserve={outputReserve}, margin={margin}, history={Math.Max(1, contextLength - outputReserve - margin - RealTokens(fixedOverheadEstimate, factor))}.");

            return new ContextBudget(
                contextLength, outputReserve, margin, fixedOverheadEstimate, factor, summaryOutput, summaryInputLimit);
        }

        /// <summary>
        /// Rough size of the forced-summary system prompt, used to subtract it from the summary input budget.
        /// </summary>
        private static string ForcedSummaryPromptTokens()
        {
            return HistoryCompactor.ForcedSummaryPromptForBudget;
        }
    }

    /// <summary>
    /// One implementation of the envelope estimate, shared by the round that sends the request and by the compaction that has to fit into the same window.
    /// </summary>
    internal static class EnvelopeEstimator
    {
        /// <summary>
        /// Estimated size of a full request: system prompt, every message, and the tool definitions.
        /// </summary>
        public static int Estimate(IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDefinition> tools)
        {
            int total = 0;

            if (messages != null)
            {
                foreach (var message in messages)
                {
                    if (message == null)
                        continue;

                    total += EstimateText(message.Role);
                    total += EstimateText(ContentTextExtractor.ExtractTextContent(message.Content));

                    if (message.ToolCalls is List<LMLocal.Infrastructure.LlmApi.Requests.ToolCall> calls)
                    {
                        foreach (var call in calls)
                        {
                            if (call?.Function == null)
                                continue;

                            total += EstimateText(call.Function.Name);
                            total += EstimateText(call.Function.Arguments);
                        }
                    }
                }
            }

            if (tools != null)
            {
                foreach (var tool in tools)
                {
                    if (tool == null)
                        continue;

                    total += EstimateText(tool.Name);
                    total += EstimateText(tool.Description);

                    if (tool.Parameters?.Properties != null)
                    {
                        foreach (var property in tool.Parameters.Properties)
                        {
                            total += EstimateText(property.Key);
                            total += EstimateText(property.Value?.Description);
                        }
                    }
                }
            }

            return total;
        }

        /// <summary>
        /// Estimated tokens of a piece of text.
        /// </summary>
        public static int EstimateText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            var estimator = new StreamedTokenEstimator();
            estimator.Add(text);
            return estimator.EstimatedTokens;
        }
    }
}
