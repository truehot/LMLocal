using System.Collections.Generic;
using LMLocal.Core.Common;
using LMLocal.Core.Models;

namespace LMLocal.Application.Tool
{
    /// <summary>
    /// Estimates how many tokens the collected tool results will add to the next round's prompt.
    /// </summary>
    internal static class ToolResultTokenEstimator
    {
        /// <summary>
        /// Characters per token for tool results.
        /// </summary>
        private const int ToolCharsPerToken = 3;

        /// <summary>
        /// Estimated tokens of the given tool results.
        /// </summary>
        public static int Estimate(IReadOnlyList<ToolResultMessage> results)
        {
            if (results == null || results.Count == 0)
                return 0;

            var estimator = new StreamedTokenEstimator(
                ToolCharsPerToken,
                StreamedTokenEstimator.DefaultNonAsciiCharsPerToken);

            for (var i = 0; i < results.Count; i++)
            {
                estimator.Add(ToolMessageFactory.ToContent(results[i]?.Result));
            }

            return estimator.EstimatedTokens;
        }
    }
}
