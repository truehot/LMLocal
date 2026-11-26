using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Tool;
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

        // The tool name and the statuses are mirrored in Resources/js/constants/app.globals.js (TodoToolName, TodoStatus).
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
                Description = "Tracks progress on a multi-step request. Purpose: keep completed, current and remaining work " +
                              "visible to you and the user across tool rounds, preventing skipped steps, repeated work and " +
                              "premature completion. Use it only for requests with 3 or more meaningful steps: " +
                              "1. Before starting, send the complete ordered plan: the first actionable step in_progress, " +
                              "all others pending. " +
                              "2. After each completed step, send the entire list again: finished steps done, the next " +
                              "unfinished step in_progress, the rest pending. " +
                              "3. Keep working and updating until the task is finished; before the final response, make " +
                              "sure the list shows the final state. " +
                              "Mark a step done only when its work is actually finished and verified where applicable. " +
                              "Write the steps in the order you will actually do them. The list is yours to reshape at any " +
                              "time: add, remove or reorder steps, or replace it with a new list for the next phase (for " +
                              "example one list while reading, then one for the changes); keep the statuses that are still valid. " +
                              "A call only records progress; it does not do the work. " +
                              "At most one call per tool round; steps finished in the same round go into one update. " +
                              "Every call replaces the list, so always send all steps. A call that breaks a rule is rejected " +
                              $"as a whole: at most {MaxItems} items, texts of 1 to {MaxTextLength} characters, at most one " +
                              "item in_progress, every text unique.",
                Parameters = new ToolParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ToolDetails>
                    {
                        { ItemsParameter, new ToolDetails
                            {
                                Type = "array",
                                Description = $"The complete ordered step list (at most {MaxItems} items; an empty array clears it): " +
                                              "completed, current and remaining steps. Each item is an object with text and status. " +
                                              "An array serialized into a JSON string is accepted too.",
                                Items = new ToolDetails
                                {
                                    Type = "object",
                                    Properties = new Dictionary<string, ToolDetails>
                                    {
                                        { TextField, new ToolDetails { Type = "string", Description = $"One concrete, actionable step, 1 to {MaxTextLength} characters, trimmed; unique in the list." } },
                                        { StatusField, new ToolDetails { Type = "string", Description = "pending = not started; in_progress = being worked on now; done = finished. " +
                                                                                                        "At most one item in_progress; finished steps stay done in later updates." } }
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
                InternalLogger.Error($"Error in {ToolName}", ex);
                return Task.FromResult<object>(Error($"Error: {ex.Message}"));
            }
        }

        public string GetProcessingMessage(Dictionary<string, object> parameters)
        {
            return "Updating task list...";
        }

        public string GetCompletionMessage(object result)
        {
            if (!(result is TodoWriteResponse response))
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
            if (!TryGetArray(value, out JArray array, out string arrayError))
                return (null, arrayError);

            if (array.Count > MaxItems)
                return (null, $"at most {MaxItems} items; merge steps ({array.Count} were sent).");

            var items = new List<TodoItem>(array.Count);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string inProgressText = null;

            for (int i = 0; i < array.Count; i++)
            {
                string position = $"item {i + 1}";

                if (!(array[i] is JObject raw))
                    return (null, $"{position} must be an object with text and status.");

                if (!TryReadStringField(raw, TextField, out string text, out bool present))
                    return (null, present ? $"{position} text must be a string." : $"{position} has no text.");

                text = text.Trim();
                if (text.Length == 0)
                    return (null, $"{position} text is empty after trimming.");
                if (text.Length > MaxTextLength)
                    return (null, $"{position} text is {text.Length} characters; at most {MaxTextLength}.");
                if (!seen.Add(text))
                    return (null, $"duplicate text: \"{text}\" - every step must be different.");

                if (!TryReadStringField(raw, StatusField, out string rawStatus, out present))
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

            if (value is JValue scalar)
                value = scalar.Value;

            if (value is JArray given)
            {
                array = given;
                return true;
            }

            if (value is string text)
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

            if (value is IEnumerable list)
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

    internal class TodoWriteResponse : IToolUiData
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("items", NullValueHandling = NullValueHandling.Ignore)]
        public IReadOnlyList<TodoItem> Items { get; set; }

        [JsonProperty("summary", NullValueHandling = NullValueHandling.Ignore)]
        public string Summary { get; set; }

        [JsonProperty("error_message", NullValueHandling = NullValueHandling.Ignore)]
        public string ErrorMessage { get; set; }

        /// <summary>
        /// The accepted list for the live task widget of the WebView; null when the call was rejected.
        /// </summary>
        [JsonIgnore]
        public object UiData => Success ? (object)Items : null;
    }
}
