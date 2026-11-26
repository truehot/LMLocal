using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Git.Operations
{
    /// <summary>
    /// Typed parameter access. The runtime hands tools values produced by JObject.ToObject, so JValue/JArray
    /// must be accepted alongside plain CLR values.
    /// </summary>
    internal static class GitParams
    {
        public static string GetString(Dictionary<string, object> parameters, string name)
        {
            object value = Get(parameters, name);
            if (value == null)
                return null;

            if (value is JValue jValue && jValue.Type == JTokenType.Null)
                return null;

            string text = Convert.ToString(value, CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        /// <summary>
        /// A search/filter value passed to git as is: spaces are kept (-S " var " differs from -S "var"); NUL is rejected
        /// because it ends the CreateProcess command line and silently drops every later argument.
        /// </summary>
        public static string GetSearchText(Dictionary<string, object> parameters, string name, out string error)
        {
            error = null;
            object value = Get(parameters, name);
            if (value == null || (value is JValue jNull && jNull.Type == JTokenType.Null))
                return null;

            string text = Convert.ToString(value is JValue jValue ? jValue.Value : value, CultureInfo.InvariantCulture);
            if (string.IsNullOrEmpty(text))
                return null;

            if (text.IndexOf('\0') >= 0)
            {
                error = $"Parameter '{name}' must not contain a NUL character.";
                return null;
            }

            return text;
        }

        public static bool GetBool(Dictionary<string, object> parameters, string name, bool defaultValue, out string error)
        {
            error = null;
            object value = Get(parameters, name);
            if (value == null || (value is JValue jNull && jNull.Type == JTokenType.Null))
                return defaultValue;

            if (value is bool flag)
                return flag;

            if (value is JValue jBool && jBool.Type == JTokenType.Boolean)
                return (bool)jBool.Value;

            string text = Convert.ToString(value, CultureInfo.InvariantCulture);
            if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(text, "false", StringComparison.OrdinalIgnoreCase)) return false;

            error = $"Parameter '{name}' must be a boolean (true or false).";
            return defaultValue;
        }

        public static int GetInt(Dictionary<string, object> parameters, string name, int defaultValue, int min, int max, out string error)
        {
            error = null;
            object value = Get(parameters, name);
            if (value == null || (value is JValue jNull && jNull.Type == JTokenType.Null))
                return defaultValue;

            int parsed;
            if (value is JValue jNumber && jNumber.Type == JTokenType.Integer)
                parsed = Convert.ToInt32(jNumber.Value, CultureInfo.InvariantCulture);
            else if (!int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            {
                error = $"Parameter '{name}' must be an integer.";
                return defaultValue;
            }

            if (parsed < min || parsed > max)
            {
                error = $"Parameter '{name}' must be between {min} and {max}.";
                return defaultValue;
            }

            return parsed;
        }

        public static string GetEnum(Dictionary<string, object> parameters, string name, string defaultValue, string[] allowed, out string error)
        {
            error = null;
            string value = GetString(parameters, name);
            if (value == null)
                return defaultValue;

            foreach (string candidate in allowed)
            {
                if (string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }

            error = $"Parameter '{name}' must be one of: {string.Join(", ", allowed)}.";
            return defaultValue;
        }

        public static List<string> GetStringArray(Dictionary<string, object> parameters, string name, out string error)
        {
            error = null;
            object value = Get(parameters, name);
            if (value == null || (value is JValue jNull && jNull.Type == JTokenType.Null))
                return new List<string>();

            if (value is string || (value is JValue jString && jString.Type == JTokenType.String))
            {
                error = $"Parameter '{name}' must be an array of strings, not a single string.";
                return new List<string>();
            }

            IEnumerable items = value as IEnumerable;
            if (items == null)
            {
                error = $"Parameter '{name}' must be an array of strings.";
                return new List<string>();
            }

            var result = new List<string>();
            foreach (object item in items)
            {
                if (item == null || (item is JValue jItemNull && jItemNull.Type == JTokenType.Null))
                    continue;

                string text = Convert.ToString(item is JValue jItem ? jItem.Value : item, CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(text))
                    result.Add(text.Trim());
            }

            return result;
        }

        private static object Get(Dictionary<string, object> parameters, string name)
        {
            object value;
            return parameters != null && parameters.TryGetValue(name, out value) ? value : null;
        }
    }
}
