using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Abstractions;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations
{
    /// <summary>
    /// Step list of the current request.
    /// </summary>
    internal interface ITodoTool : IBuiltInTool
    {
        /// <summary>Drops the stored list; called when a new request starts.</summary>
        void Reset();
    }

    /// <summary>
    /// Stores the plan the model writes for the current request.
    /// </summary>
    internal class TodoWrite : ITodoTool
    {
        internal const string ItemsParameter = "items";
        internal const string TextField = "text";
        internal const string StatusField = "status";

        /// <summary>More steps than this cannot be tracked meaningfully; the list has to be merged.</summary>
        internal const int MaxItems = 20;

        /// <summary>Longest step text accepted.</summary>
        internal const int MaxTextLength = 200;

        internal const string StatusPending = "pending";
        internal const string StatusInProgress = "in_progress";
        internal const string StatusDone = "done";

        private static readonly IReadOnlyList<TodoItem> NoItems = new List<TodoItem>().AsReadOnly();

        /// <summary>
        /// Replaced by reference only, so a reader never sees a half-written list.
        /// </summary>
        private volatile IReadOnlyList<TodoItem> _current = NoItems;

        public string ToolName => "todo_write";

        /// <summary>No file is touched, so the tool is also available when write tools are off.</summary>
        public ToolAccessLevel AccessLevel => ToolAccessLevel.ReadOnly;

        /// <summary>Last list accepted for the current request; empty when the model wrote none.</summary>
        public IReadOnlyList<TodoItem> Current => _current;

        public void Reset()
        {
            _current = NoItems;
        }

        public ToolDefinition GetToolInfo()
        {
            return new ToolDefinition
            {
                Name = ToolName,
                Description = "Stores the step list of the current request. Use it when the request needs 3 or more steps: " +
                              "call it once at the start with the plan, then after each finished step with the whole updated list. " +
                              "One call per response; the last call of a response is the list. Mark a step done only when it is " +
                              "really finished. Do not use it for single-step requests. Every call replaces the stored list, so " +
                              $"always send all steps. A call that breaks a rule is rejected as a whole: at most {MaxItems} items, " +
                              $"texts of 1 to {MaxTextLength} characters, at most one item in_progress, every text unique.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { ItemsParameter, new ToolDetails
                            {
                                Type = "array",
                                Description = $"The whole step list in order (at most {MaxItems} items; an empty array clears it). " +
                                              "Each item is an object with text and status. An array serialized into a JSON string is accepted too.",
                                Items = new ToolDetails
                                {
                                    Type = "object",
                                    Properties = new Dictionary<string, ToolDetails>
                                    {
                                        { TextField, new ToolDetails { Type = "string", Description = $"One step, 1 to {MaxTextLength} characters, trimmed; unique in the list." } },
                                        { StatusField, new ToolDetails { Type = "string", Description = "pending, in_progress or done (case-insensitive). At most one item in_progress." } }
                                    },
                                    Required = new List<string> { TextField, StatusField }
                                }
                            }
                        }
                    },
                    Required = new List<string> { ItemsParameter }
                }
            };
        }

        public Task<object> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
        {
            try
            {
                if (cancellationToken.IsCancellationRequested)
                    return Task.FromResult<object>(Error("Operation was cancelled."));

                object itemsValue = null;
                parameters?.TryGetValue(ItemsParameter, out itemsValue);

                var (items, error) = ParseItems(itemsValue);
                if (error != null)
                    return Task.FromResult<object>(Error(error));

                var accepted = new List<TodoItem>(items).AsReadOnly();
                _current = accepted;

                return Task.FromResult<object>(new TodoWriteResponse
                {
                    Success = true,
                    Items = accepted,
                    Summary = BuildSummary(accepted)
                });
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"Error in {ToolName}: {ex}");
                return Task.FromResult<object>(Error($"Error: {ex.Message}"));
            }
        }

        public string GetProcessingMessage(Dictionary<string, object> parameters)
        {
            return "Updating task list...";
        }

        public string GetCompletionMessage(object result)
        {
            var response = result as TodoWriteResponse;
            if (response == null)
                return "Tasks updated.";

            return response.Success
                ? $"Tasks updated: {response.Summary}."
                : $"Task list not updated: {response.ErrorMessage}";
        }

        /// <summary>
        /// Reads the whole list or nothing: one bad item rejects the call, so the stored list never holds half of
        /// a plan.
        /// </summary>
        private static (List<TodoItem> items, string error) ParseItems(object value)
        {
            JArray array;
            string arrayError;
            if (!TryGetArray(value, out array, out arrayError))
                return (null, arrayError);

            if (array.Count > MaxItems)
                return (null, $"at most {MaxItems} items; merge steps ({array.Count} were sent).");

            var items = new List<TodoItem>(array.Count);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string inProgressText = null;

            for (int i = 0; i < array.Count; i++)
            {
                string position = $"item {i + 1}";

                var raw = array[i] as JObject;
                if (raw == null)
                    return (null, $"{position} must be an object with text and status.");

                bool present;
                string text;
                if (!TryReadStringField(raw, TextField, out text, out present))
                    return (null, present ? $"{position} text must be a string." : $"{position} has no text.");

                text = text.Trim();
                if (text.Length == 0)
                    return (null, $"{position} text is empty after trimming.");
                if (text.Length > MaxTextLength)
                    return (null, $"{position} text is {text.Length} characters; at most {MaxTextLength}.");
                if (!seen.Add(text))
                    return (null, $"duplicate text: \"{text}\" - every step must be different.");

                string rawStatus;
                if (!TryReadStringField(raw, StatusField, out rawStatus, out present))
                    return (null, present
                        ? $"{position} status must be a string."
                        : $"{position} has no status; use {StatusPending}, {StatusInProgress} or {StatusDone}.");

                string status = NormalizeStatus(rawStatus);
                if (status == null)
                    return (null, $"{position} status \"{rawStatus}\" is not one of {StatusPending}, {StatusInProgress}, {StatusDone}.");

                if (status == StatusInProgress)
                {
                    if (inProgressText != null)
                        return (null, $"at most one item can be {StatusInProgress}; \"{inProgressText}\" and \"{text}\" both are.");

                    inProgressText = text;
                }

                items.Add(new TodoItem { Text = text, Status = status });
            }

            return (items, null);
        }

        /// <summary>
        /// The list as an array. Some local models send it as a JSON string; that form is parsed too, the same
        /// tolerance replace_file_lines has for 'edits'.
        /// </summary>
        private static bool TryGetArray(object value, out JArray array, out string error)
        {
            array = null;
            error = null;

            if (LiteralListParameter.IsMissing(value))
            {
                error = "items is required: an array of {text, status} objects.";
                return false;
            }

            var scalar = value as JValue;
            if (scalar != null)
                value = scalar.Value;

            var given = value as JArray;
            if (given != null)
            {
                array = given;
                return true;
            }

            var text = value as string;
            if (text != null)
            {
                string trimmed = text.Trim();
                if (!trimmed.StartsWith("[", StringComparison.Ordinal) || !trimmed.EndsWith("]", StringComparison.Ordinal))
                {
                    error = "items must be an array of {text, status} objects.";
                    return false;
                }

                try
                {
                    array = JArray.Parse(trimmed);
                    return true;
                }
                catch (JsonException ex)
                {
                    error = $"items is a string but not a valid JSON array: {ex.Message}";
                    return false;
                }
            }

            if (value is JObject || value is IDictionary)
            {
                error = "items must be an array of {text, status} objects; an object was sent. Put the steps in [ ].";
                return false;
            }

            var list = value as IEnumerable;
            if (list != null)
            {
                try
                {
                    array = JArray.FromObject(list);
                    return true;
                }
                catch (JsonException ex)
                {
                    error = $"items could not be read as an array: {ex.Message}";
                    return false;
                }
            }

            error = "items must be an array of {text, status} objects.";
            return false;
        }

        /// <summary>
        /// Reads a string field case-insensitively.
        /// </summary>
        private static bool TryReadStringField(JObject raw, string name, out string value, out bool present)
        {
            value = null;
            present = false;

            var property = raw.Property(name, StringComparison.OrdinalIgnoreCase);
            if (property == null || property.Value == null || property.Value.Type == JTokenType.Null)
                return false;

            present = true;
            if (property.Value.Type != JTokenType.String)
                return false;

            value = property.Value.Value<string>();
            return true;
        }

        /// <summary>Canonical status for a case-insensitive value, or null when it is not a status.</summary>
        private static string NormalizeStatus(string value)
        {
            string key = value.Trim().ToLowerInvariant().Replace('-', '_').Replace(' ', '_');

            switch (key)
            {
                case StatusPending:
                    return StatusPending;
                case StatusInProgress:
                    return StatusInProgress;
                case StatusDone:
                    return StatusDone;
                default:
                    return null;
            }
        }

        /// <summary>Short state of the list, e.g. "1/3 done; in progress: Fix the parser".</summary>
        private static string BuildSummary(IReadOnlyList<TodoItem> items)
        {
            if (items.Count == 0)
                return "no items (list cleared)";

            int done = 0;
            string inProgress = null;
            foreach (var item in items)
            {
                if (item.Status == StatusDone)
                    done++;
                else if (item.Status == StatusInProgress)
                    inProgress = item.Text;
            }

            var summary = new StringBuilder();
            summary.Append(done).Append('/').Append(items.Count).Append(" done");

            if (inProgress != null)
                summary.Append("; in progress: ").Append(inProgress);
            else if (done == items.Count)
                summary.Append("; all done");

            return summary.ToString();
        }

        private static TodoWriteResponse Error(string message)
        {
            return new TodoWriteResponse
            {
                Success = false,
                ErrorMessage = message
            };
        }
    }

    /// <summary>One step of the list: the text as sent (trimmed) and the canonical status.</summary>
    internal class TodoItem
    {
        [JsonProperty("text")]
        public string Text { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }
    }

    internal class TodoWriteResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("items", NullValueHandling = NullValueHandling.Ignore)]
        public IReadOnlyList<TodoItem> Items { get; set; }

        [JsonProperty("summary", NullValueHandling = NullValueHandling.Ignore)]
        public string Summary { get; set; }

        [JsonProperty("error_message", NullValueHandling = NullValueHandling.Ignore)]
        public string ErrorMessage { get; set; }
    }
}
