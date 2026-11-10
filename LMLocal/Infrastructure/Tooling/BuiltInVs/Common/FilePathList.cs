using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common
{
    /// <summary>
    /// Reads the file list of a tool that accepts several files in one call: <c>file_path</c> as one path or comma-separated paths,
    /// and/or <c>file_paths</c> as an array (a comma-separated string is accepted there too).
    /// </summary>
    internal static class FilePathList
    {
        public const string SingleParameter = "file_path";
        public const string ArrayParameter = "file_paths";

        /// <summary>
        /// Upper bound on files in one call.
        /// </summary>
        public const int MaxFiles = 50;

        /// <summary>
        /// Collects the requested paths in order, trimmed, without empty entries and without duplicates (case-insensitive).
        /// </summary>
        public static bool TryParse(
            IDictionary<string, object> parameters, out List<string> paths, out string error, Func<string, bool> isExistingFile = null)
        {
            paths = new List<string>();
            error = null;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (parameters != null)
            {
                if (parameters.TryGetValue(SingleParameter, out object single))
                    AddAll(single, paths, seen, isExistingFile);
                if (parameters.TryGetValue(ArrayParameter, out object many))
                    AddAll(many, paths, seen, isExistingFile);
            }

            if (paths.Count == 0)
            {
                error = $"Parameter '{SingleParameter}' (or '{ArrayParameter}') is required.";
                return false;
            }

            if (paths.Count > MaxFiles)
            {
                error = $"At most {MaxFiles} files per call (got {paths.Count}).";
                paths = new List<string>();
                return false;
            }

            return true;
        }

        private static void AddAll(object value, List<string> paths, HashSet<string> seen, Func<string, bool> isExistingFile)
        {
            if (value == null)
                return;

            if (value is JValue scalar)
                value = scalar.Value;

            if (value is string text)
            {
                string trimmed = text.Trim();
                if (trimmed.StartsWith("[", StringComparison.Ordinal) && TryParseJsonArray(trimmed, out JArray parsed))
                {
                    AddAll(parsed, paths, seen, isExistingFile);
                    return;
                }

                if (trimmed.IndexOf(',') >= 0 && trimmed.IndexOf('\n') < 0 && IsExistingFile(isExistingFile, trimmed))
                {
                    AddOne(trimmed, paths, seen);
                    return;
                }

                foreach (string part in text.Split(new[] { ',', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    AddOne(part, paths, seen);
                return;
            }

            if (value is IEnumerable items)
            {
                foreach (object item in items)
                    AddAll(item, paths, seen, isExistingFile);
                return;
            }

            AddOne(Convert.ToString(value), paths, seen);
        }

        private static void AddOne(string raw, List<string> paths, HashSet<string> seen)
        {
            string path = raw?.Trim().Trim('"', '\'').Trim();
            if (string.IsNullOrEmpty(path))
                return;

            if (seen.Add(path))
                paths.Add(path);
        }

        private static bool IsExistingFile(Func<string, bool> isExistingFile, string path)
        {
            if (isExistingFile == null)
                return false;

            try
            {
                return isExistingFile(path.Trim('"', '\''));
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool TryParseJsonArray(string text, out JArray array)
        {
            try
            {
                array = JArray.Parse(text);
                return true;
            }
            catch (JsonException)
            {
                array = null;
                return false;
            }
        }
    }

    /// <summary>
    /// Response of a tool call that processed several files: one entry per file, in request order.
    /// </summary>
    public class FileBatchResponse<T>
    {
        /// <summary>
        /// False only when no file could be processed; per-file outcomes are in <see cref="Files"/>.
        /// </summary>
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("error_message", NullValueHandling = NullValueHandling.Ignore)]
        public string ErrorMessage { get; set; }

        /// <summary>One note for the whole call (not repeated per file).</summary>
        [JsonProperty("warning", NullValueHandling = NullValueHandling.Ignore)]
        public string Warning { get; set; }

        [JsonProperty("total_files")]
        public int TotalFiles { get; set; }

        [JsonProperty("succeeded")]
        public int Succeeded { get; set; }

        [JsonProperty("files")]
        public List<T> Files { get; set; } = new List<T>();
    }
}
