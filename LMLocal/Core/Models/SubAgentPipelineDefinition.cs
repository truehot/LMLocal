using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace LMLocal.Core.Models
{
    /// <summary>
    /// Placeholder tokens understood inside the prompt of a pipeline step.
    /// </summary>
    public static class SubAgentPipelinePlaceholders
    {
        /// <summary>The task the main model passed to the whole pipeline.</summary>
        public const string Task = "{{task}}";

        /// <summary>
        /// Final answer of the previous step: all branch answers in config order when the previous step was a group.
        /// Not available in the first step.
        /// </summary>
        public const string Prev = "{{prev}}";

        /// <summary>Prefix of an output reference: <c>{{steps.&lt;id&gt;}}</c>.</summary>
        public const string StepRefPrefix = "steps.";

        /// <summary>
        /// Every placeholder the renderer understands: <c>{{task}}</c>, <c>{{prev}}</c> and <c>{{steps.&lt;id&gt;}}</c>.
        /// </summary>
        internal static readonly Regex PlaceholderRegex = new Regex(
            @"\{\{(task|prev|steps\.[A-Za-z0-9_-]+)\}\}",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        private static readonly Regex StepRefRegex = new Regex(
            @"\{\{steps\.([A-Za-z0-9_-]+)\}\}",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// Ids referenced through <c>{{steps.&lt;id&gt;}}</c>, in the order they appear, without duplicates.
        /// </summary>
        public static IReadOnlyList<string> FindStepRefs(string template)
        {
            var references = new List<string>();
            if (string.IsNullOrEmpty(template))
                return references;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in StepRefRegex.Matches(template))
            {
                var id = match.Groups[1].Value;
                if (seen.Add(id))
                    references.Add(id);
            }

            return references;
        }

        /// <summary>True when the template uses <c>{{prev}}</c>.</summary>
        public static bool ContainsPrev(string template)
        {
            return !string.IsNullOrEmpty(template)
                && template.IndexOf(Prev, StringComparison.Ordinal) >= 0;
        }
    }

    /// <summary>
    /// One pipeline: a fixed sequence of steps that runs without any decision of the main model between the steps.
    /// </summary>
    public class SubAgentPipelineDefinition
    {
        /// <summary>Return only the answer of the last step.</summary>
        public const string OutputLast = "last";

        /// <summary>Return the answers of every step, each under its own heading.</summary>
        public const string OutputAll = "all";

        /// <summary>Pipeline id. Required (unique, case-insensitive). Used as the tool name.</summary>
        [JsonProperty("id")]
        public string Id { get; set; }

        /// <summary>Optional human-readable name shown in the UI and in the chat. Falls back to <see cref="Id"/>.</summary>
        [JsonProperty("displayName")]
        public string DisplayName { get; set; }

        /// <summary>What the pipeline does. Required; this is the tool description the main model reads.</summary>
        [JsonProperty("description")]
        public string Description { get; set; }

        /// <summary>Whether the pipeline is enabled. Missing key means true.</summary>
        [JsonProperty("enabled")]
        public bool? Enabled { get; set; }

        /// <summary><see cref="OutputLast"/> (default) or <see cref="OutputAll"/>.</summary>
        [JsonProperty("output")]
        public string Output { get; set; }

        /// <summary>
        /// Whether the pipeline may run inside a parallel group of the main round.
        /// </summary>
        [JsonProperty("parallel")]
        public bool? Parallel { get; set; }

        /// <summary>Width of the parallel group of the main round. Optional; falls back to the root value, then to the default.</summary>
        [JsonProperty("maxParallel")]
        public int? MaxParallel { get; set; }

        /// <summary>Steps in run order.</summary>
        [JsonProperty("steps")]
        public List<SubAgentPipelineStep> Steps { get; set; } = new List<SubAgentPipelineStep>();

        /// <summary>True unless the config explicitly disables the pipeline.</summary>
        [JsonIgnore]
        public bool IsEnabled
        {
            get { return Enabled != false; }
        }

        /// <summary>True when the answer must contain the answer of every step.</summary>
        [JsonIgnore]
        public bool OutputsAllSteps
        {
            get
            {
                var value = (Output ?? string.Empty).Trim();
                return string.Equals(value, OutputAll, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>Display name, falling back to the id.</summary>
        [JsonIgnore]
        public string DisplayNameOrId
        {
            get
            {
                return !string.IsNullOrWhiteSpace(DisplayName)
                    ? DisplayName.Trim()
                    : (Id ?? string.Empty).Trim();
            }
        }

        /// <summary>True when at least one step is a parallel group.</summary>
        [JsonIgnore]
        public bool HasParallelSteps
        {
            get
            {
                if (Steps == null)
                    return false;

                foreach (var step in Steps)
                {
                    if (step != null && step.IsParallelGroup)
                        return true;
                }

                return false;
            }
        }

        /// <summary>
        /// Sequential steps plus the branches of the parallel groups: the steps that actually start an agent.
        /// </summary>
        public IReadOnlyList<SubAgentPipelineStep> EnumerateLeafSteps()
        {
            var leaves = new List<SubAgentPipelineStep>();
            if (Steps == null)
                return leaves;

            foreach (var step in Steps)
            {
                if (step == null)
                    continue;

                if (step.IsParallelGroup)
                {
                    foreach (var branch in step.Parallel)
                    {
                        if (branch != null)
                            leaves.Add(branch);
                    }
                }
                else
                {
                    leaves.Add(step);
                }
            }

            return leaves;
        }

        /// <summary>
        /// Distinct agent names used by the steps, in file order.
        /// </summary>
        public IReadOnlyList<string> GetAgentNames()
        {
            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var step in EnumerateLeafSteps())
            {
                var name = step.Agent == null ? null : step.Agent.Trim();
                if (string.IsNullOrEmpty(name))
                    continue;

                if (seen.Add(name))
                    names.Add(name);
            }

            return names;
        }

        /// <summary>
        /// Validates the pipeline and returns a list of human-readable errors.
        /// </summary>
        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();

            var id = Id == null ? null : Id.Trim();
            if (string.IsNullOrEmpty(id))
                errors.Add("'id' is required");
            else if (!SubAgentToolIdentifier.IsValid(id))
                errors.Add($"'id' '{id}' is not a valid tool identifier (allowed: letters, digits, '_', '-', 1..64 chars)");

            if (string.IsNullOrWhiteSpace(Description))
                errors.Add("'description' is required");

            if (MaxParallel.HasValue && MaxParallel.Value < 1)
                errors.Add("'maxParallel' must be >= 1");

            var output = (Output ?? string.Empty).Trim();
            if (output.Length > 0
                && !string.Equals(output, OutputLast, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(output, OutputAll, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"'output' must be '{OutputLast}' or '{OutputAll}'");
            }

            var steps = Steps ?? new List<SubAgentPipelineStep>();
            if (steps.Count == 0)
            {
                errors.Add("'steps' must contain at least one step");
                return errors;
            }

            var declaredIds = CollectDeclaredIds(errors, steps);

            var visibleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                var prefix = $"step[{i}]";

                if (step == null)
                {
                    errors.Add($"{prefix} is empty");
                    continue;
                }

                errors.AddRange(step.Validate(prefix, i == 0, visibleIds));

                var stepId = step.Id == null ? null : step.Id.Trim();
                if (!string.IsNullOrEmpty(stepId))
                    visibleIds.Add(stepId);

                if (step.Parallel != null)
                {
                    foreach (var branch in step.Parallel)
                    {
                        var branchId = branch == null || branch.Id == null ? null : branch.Id.Trim();
                        if (!string.IsNullOrEmpty(branchId))
                            visibleIds.Add(branchId);
                    }
                }
            }

            return errors;
        }

        /// <summary>
        /// Deep copy used by the config manager, which writes a cloned snapshot.
        /// </summary>
        public SubAgentPipelineDefinition Clone()
        {
            var clone = new SubAgentPipelineDefinition
            {
                Id = Id,
                DisplayName = DisplayName,
                Description = Description,
                Enabled = Enabled,
                Output = Output,
                Parallel = Parallel,
                MaxParallel = MaxParallel,
                Steps = new List<SubAgentPipelineStep>()
            };

            if (Steps == null)
                return clone;

            foreach (var step in Steps)
            {
                clone.Steps.Add(step == null ? null : step.Clone());
            }

            return clone;
        }

        /// <summary>
        /// Checks the format and the uniqueness of every step and branch id of the pipeline.
        /// </summary>
        private static HashSet<string> CollectDeclaredIds(List<string> errors, IReadOnlyList<SubAgentPipelineStep> steps)
        {
            var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                if (step == null)
                    continue;

                AddDeclaredId(errors, declared, step.Id, $"step[{i}]");

                if (step.Parallel == null)
                    continue;

                for (int b = 0; b < step.Parallel.Count; b++)
                {
                    var branch = step.Parallel[b];
                    if (branch != null)
                        AddDeclaredId(errors, declared, branch.Id, $"step[{i}] branch[{b}]");
                }
            }

            return declared;
        }

        private static void AddDeclaredId(List<string> errors, HashSet<string> declared, string rawId, string prefix)
        {
            if (string.IsNullOrWhiteSpace(rawId))
                return;

            var id = rawId.Trim();
            if (!SubAgentToolIdentifier.IsValid(id))
            {
                errors.Add($"{prefix}: 'id' '{id}' is not a valid tool identifier (allowed: letters, digits, '_', '-', 1..64 chars)");
                return;
            }

            if (!declared.Add(id))
                errors.Add($"{prefix}: duplicate step id '{id}' (step ids are unique within one pipeline)");
        }
    }

    /// <summary>
    /// One step of a pipeline: either a single agent run (<see cref="Agent"/> + <see cref="Prompt"/>)
    /// or a group of branches that run at the same time (<see cref="Parallel"/>).
    /// </summary>
    public class SubAgentPipelineStep
    {
        /// <summary>Upper bound of <see cref="Retries"/>.</summary>
        public const int MaxRetries = 3;

        /// <summary>Default of <see cref="OnBranchError"/>: the first final branch error stops the pipeline.</summary>
        public const string BranchErrorStop = "stop";

        /// <summary>The other branches still finish and the failed one is reported to the next step as an error text.</summary>
        public const string BranchErrorContinue = "continue";

        /// <summary>Optional step id; makes the answer addressable as <c>{{steps.&lt;id&gt;}}</c>.</summary>
        [JsonProperty("id")]
        public string Id { get; set; }

        /// <summary>Agent id of this step (or of one branch inside a group).</summary>
        [JsonProperty("agent")]
        public string Agent { get; set; }

        /// <summary>Prompt template: <c>{{task}}</c>, <c>{{prev}}</c> and <c>{{steps.&lt;id&gt;}}</c> are substituted.</summary>
        [JsonProperty("prompt")]
        public string Prompt { get; set; }

        /// <summary>How many times the step is repeated on a transient provider failure (0..3, default 0).</summary>
        [JsonProperty("retries")]
        public int? Retries { get; set; }

        /// <summary>Branches of a parallel group. Nested groups are not allowed.</summary>
        [JsonProperty("parallel")]
        public List<SubAgentPipelineStep> Parallel { get; set; }

        /// <summary>Width of a parallel group. Optional; falls back to the group value, then to the pipeline and root values.</summary>
        [JsonProperty("maxParallel")]
        public int? MaxParallel { get; set; }

        /// <summary>What a final branch error does: "stop" (default) or "continue".</summary>
        [JsonProperty("onBranchError")]
        public string OnBranchError { get; set; }

        /// <summary>True when the step is a group of parallel branches.</summary>
        [JsonIgnore]
        public bool IsParallelGroup
        {
            get { return Parallel != null && Parallel.Count > 0; }
        }

        /// <summary>True when a final branch error must not cancel the other branches.</summary>
        [JsonIgnore]
        public bool ContinuesOnBranchError
        {
            get
            {
                return string.Equals(
                    string.IsNullOrWhiteSpace(OnBranchError) ? BranchErrorStop : OnBranchError.Trim(),
                    BranchErrorContinue,
                    StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>Label used in progress messages, logs and headings: the id when set, else the agent.</summary>
        [JsonIgnore]
        public string Label
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Id))
                    return Id.Trim();

                return string.IsNullOrWhiteSpace(Agent) ? "step" : Agent.Trim();
            }
        }

        /// <summary>
        /// Validates the step. <paramref name="visibleStepIds"/> holds the ids of the steps declared before this one.
        /// </summary>
        public IReadOnlyList<string> Validate(string prefix, bool isFirst, ICollection<string> visibleStepIds)
        {
            var errors = new List<string>();

            var hasAgent = !string.IsNullOrWhiteSpace(Agent);
            var hasBranches = IsParallelGroup;

            if (hasAgent && hasBranches)
            {
                errors.Add($"{prefix}: use either 'agent' + 'prompt' or 'parallel', not both");
            }
            else if (!hasAgent && !hasBranches)
            {
                errors.Add($"{prefix}: 'agent' and 'prompt' are required (or a 'parallel' list of branches)");
            }

            AddRetryErrors(errors, prefix, Retries);

            if (hasBranches)
            {
                var onBranchError = string.IsNullOrWhiteSpace(OnBranchError) ? BranchErrorStop : OnBranchError.Trim();
                if (!string.Equals(onBranchError, BranchErrorStop, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(onBranchError, BranchErrorContinue, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"{prefix}: 'onBranchError' must be '{BranchErrorStop}' or '{BranchErrorContinue}'");
                }

                if (MaxParallel.HasValue && MaxParallel.Value < 1)
                    errors.Add($"{prefix}: 'maxParallel' must be >= 1");

                for (int b = 0; b < Parallel.Count; b++)
                {
                    var branch = Parallel[b];
                    var branchPrefix = $"{prefix} branch[{b}]";

                    if (branch == null)
                    {
                        errors.Add($"{branchPrefix} is empty");
                        continue;
                    }

                    if (branch.Parallel != null && branch.Parallel.Count > 0)
                        errors.Add($"{branchPrefix}: nested parallel groups are not allowed");

                    if (string.IsNullOrWhiteSpace(branch.Agent))
                        errors.Add($"{branchPrefix}: 'agent' is required");

                    if (string.IsNullOrWhiteSpace(branch.Prompt))
                        errors.Add($"{branchPrefix}: 'prompt' is required");
                    else
                        errors.AddRange(ValidatePrompt(branchPrefix, branch.Prompt, isFirst, visibleStepIds));

                    AddRetryErrors(errors, branchPrefix, branch.Retries);
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(Prompt))
                    errors.Add($"{prefix}: 'prompt' is required");
                else
                    errors.AddRange(ValidatePrompt(prefix, Prompt, isFirst, visibleStepIds));

                if (!string.IsNullOrWhiteSpace(OnBranchError))
                    errors.Add($"{prefix}: 'onBranchError' is only valid on a parallel step");

                if (MaxParallel.HasValue)
                    errors.Add($"{prefix}: 'maxParallel' is only valid on a parallel step");
            }

            return errors;
        }

        /// <summary>
        /// Deep copy of the step and of its branches.
        /// </summary>
        public SubAgentPipelineStep Clone()
        {
            var clone = new SubAgentPipelineStep
            {
                Id = Id,
                Agent = Agent,
                Prompt = Prompt,
                Retries = Retries,
                MaxParallel = MaxParallel,
                OnBranchError = OnBranchError
            };

            if (Parallel != null)
            {
                clone.Parallel = new List<SubAgentPipelineStep>();
                foreach (var branch in Parallel)
                {
                    clone.Parallel.Add(branch == null ? null : branch.Clone());
                }
            }

            return clone;
        }

        private static IEnumerable<string> ValidatePrompt(
            string prefix,
            string prompt,
            bool isFirst,
            ICollection<string> visibleStepIds)
        {
            var errors = new List<string>();

            if (isFirst && SubAgentPipelinePlaceholders.ContainsPrev(prompt))
                errors.Add($"{prefix}: '{SubAgentPipelinePlaceholders.Prev}' is not available in the first step");

            foreach (var reference in SubAgentPipelinePlaceholders.FindStepRefs(prompt))
            {
                if (visibleStepIds == null || !visibleStepIds.Contains(reference))
                    errors.Add($"{prefix}: '{{{{steps.{reference}}}}}' must reference an earlier step of the same pipeline");
            }

            return errors;
        }

        private static void AddRetryErrors(List<string> errors, string prefix, int? retries)
        {
            if (retries.HasValue && (retries.Value < 0 || retries.Value > MaxRetries))
                errors.Add($"{prefix}: 'retries' must be between 0 and {MaxRetries}");
        }
    }
}
