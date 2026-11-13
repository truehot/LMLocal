using System.Collections.Generic;
using System.Linq;
using System.Text;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LMLocal.Infrastructure.Streaming
{
    /// <summary>
    /// Collects the tool calls of one streamed response: metadata (id, name, extra content) and argument fragments per call index.
    /// An optional guard may ask to stop the stream when the arguments of a call turn out to be broken.
    /// Not thread-safe: one instance per stream, used under the processor's lock.
    /// </summary>
    internal sealed class ToolCallAccumulator
    {
        private sealed class Entry
        {
            public readonly StringBuilder Arguments = new StringBuilder();
            public bool HasMetadata;
            public string CallId;
            public string FunctionName;
            public string ExtraContentJson;
        }

        private readonly Dictionary<int, Entry> _entries = new Dictionary<int, Entry>();
        private readonly IToolCallArgumentsGuard _guard;

        public ToolCallAccumulator(IToolCallArgumentsGuard guard = null)
        {
            _guard = guard;
        }

        /// <summary>
        /// Index of the call whose arguments the guard rejected; null while no call was rejected.
        /// </summary>
        public int? AbortedIndex { get; private set; }

        /// <summary>
        /// Why the guard rejected <see cref="AbortedIndex"/>; null while no call was rejected.
        /// </summary>
        public string AbortReason { get; private set; }

        /// <summary>
        /// Records the call metadata; a repeated metadata delta overwrites the previous one.
        /// </summary>
        public bool SetMetadata(int index, string callId, string functionName, string extraContentJson, string initialArguments)
        {
            var entry = GetEntry(index);
            entry.HasMetadata = true;
            entry.CallId = callId;
            entry.FunctionName = functionName;
            entry.ExtraContentJson = extraContentJson;

            return string.IsNullOrEmpty(initialArguments) || AppendArguments(index, initialArguments);
        }

        /// <summary>
        /// Appends an argument fragment to the call. Returns false when the guard rejected the arguments and the stream should stop.
        /// </summary>
        public bool AppendArguments(int index, string fragment)
        {
            if (string.IsNullOrEmpty(fragment))
                return true;

            var entry = GetEntry(index);
            entry.Arguments.Append(fragment);

            if (_guard == null || AbortedIndex.HasValue)
                return AbortedIndex == null;

            string reason = _guard.Check(entry.FunctionName, entry.Arguments, fragment);
            if (reason == null)
                return true;

            AbortedIndex = index;
            AbortReason = reason;
            return false;
        }

        /// <summary>
        /// The start of the call's arguments, for logs.
        /// </summary>
        public string GetArgumentsPreview(int index, int maxLength)
        {
            if (!_entries.TryGetValue(index, out var entry))
                return string.Empty;

            var arguments = entry.Arguments;
            return arguments.Length <= maxLength ? arguments.ToString() : arguments.ToString(0, maxLength) + "...";
        }

        /// <summary>
        /// The function name of the call, or null when its metadata has not arrived.
        /// </summary>
        public string GetFunctionName(int index)
        {
            return _entries.TryGetValue(index, out var entry) ? entry.FunctionName : null;
        }

        /// <summary>
        /// Builds the tool call records ordered by index.
        /// </summary>
        public IReadOnlyList<ToolCallRecord> Build()
        {
            var toolCalls = new List<ToolCallRecord>(_entries.Count);

            foreach (var pair in _entries.OrderBy(p => p.Key))
            {
                int index = pair.Key;
                var entry = pair.Value;
                string argumentsJson = entry.Arguments.ToString();

                if (!entry.HasMetadata)
                {
                    InternalLogger.Warn($"Missing metadata for tool call at index {index}. Arguments: {argumentsJson}");
                    continue;
                }

                bool isValid = index != AbortedIndex && IsValidJson(argumentsJson);
                if (!isValid && index != AbortedIndex)
                {
                    InternalLogger.Warn($"[StreamProcessor] Invalid tool arguments for '{entry.FunctionName}' (id: {entry.CallId})");
                }

                toolCalls.Add(new ToolCallRecord
                {
                    Index = index,
                    CallId = entry.CallId,
                    FunctionName = entry.FunctionName,
                    ArgumentsJson = isValid ? argumentsJson : "{}",
                    IsInvalid = !isValid,
                    ExtraContentJson = entry.ExtraContentJson
                });
            }

            return toolCalls.AsReadOnly();
        }

        private Entry GetEntry(int index)
        {
            if (!_entries.TryGetValue(index, out var entry))
            {
                entry = new Entry();
                _entries[index] = entry;
            }

            return entry;
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
