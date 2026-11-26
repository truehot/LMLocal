using System;
using System.Collections.Generic;
using LMLocal.Core.Models;

namespace LMLocal.Application.SubAgents
{
    /// <summary>
    /// Single-pass renderer for the prompt templates of pipeline steps.
    /// </summary>
    internal static class SubAgentPipelinePrompt
    {
        /// <summary>
        /// Substitutes <c>{{task}}</c>, <c>{{prev}}</c> and <c>{{steps.&lt;id&gt;}}</c> in one pass over the template:
        /// the substituted text is never scanned again, so an answer that contains a placeholder stays as it is.
        /// </summary>
        internal static string Render(
            string template,
            string task,
            string prev,
            IReadOnlyDictionary<string, string> stepOutputs)
        {
            if (string.IsNullOrEmpty(template))
                return string.Empty;

            return SubAgentPipelinePlaceholders.PlaceholderRegex.Replace(template, match =>
            {
                var token = match.Groups[1].Value;

                if (string.Equals(token, "task", StringComparison.Ordinal))
                    return task ?? string.Empty;

                if (string.Equals(token, "prev", StringComparison.Ordinal))
                    return prev ?? string.Empty;

                if (token.StartsWith(SubAgentPipelinePlaceholders.StepRefPrefix, StringComparison.Ordinal))
                {
                    var id = token.Substring(SubAgentPipelinePlaceholders.StepRefPrefix.Length);
                    string value;
                    if (stepOutputs != null && id.Length > 0 && stepOutputs.TryGetValue(id, out value))
                        return value ?? string.Empty;
                }

                return match.Value;
            });
        }

        /// <summary>
        /// Heading of a step or of a branch in the text passed to the next step and to the main model:
        /// <c>### [2/3 analyze]</c>.
        /// </summary>
        internal static string BuildHeading(int index, int total, string label)
        {
            return $"### [{index}/{total} {label}]";
        }
    }
}
