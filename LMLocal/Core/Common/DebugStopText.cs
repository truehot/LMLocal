using System;
using System.Text;

namespace LMLocal.Core.Common
{
    /// <summary>
    /// Pure rules about the text of a debugger stop: which values may be read at all, how a value the
    /// debugger printed is cleaned up, and how a multi-line message is flattened.
    /// </summary>
    internal static class DebugStopText
    {
        /// <summary>
        /// True for the types whose value can be read without running any code of the debugged process:
        /// primitives, <c>string</c> and the few structs that are safe to print.
        /// </summary>
        public static bool IsSimpleType(string type)
        {
            string name = Trimmed(type);
            if (name == null)
                return false;

            name = UnwrapNullable(name);

            switch (name)
            {
                case "bool":
                case "byte":
                case "sbyte":
                case "char":
                case "short":
                case "ushort":
                case "int":
                case "uint":
                case "long":
                case "ulong":
                case "float":
                case "double":
                case "decimal":
                case "string":
                case "System.Boolean":
                case "System.Byte":
                case "System.SByte":
                case "System.Char":
                case "System.Int16":
                case "System.UInt16":
                case "System.Int32":
                case "System.UInt32":
                case "System.Int64":
                case "System.UInt64":
                case "System.Single":
                case "System.Double":
                case "System.Decimal":
                case "System.String":
                case "System.Guid":
                case "System.DateTime":
                case "System.DateTimeOffset":
                case "System.TimeSpan":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Drops the quotes a debugger puts around a string value. Anything else is returned unchanged.
        /// </summary>
        public static string Unquote(string value)
        {
            if (value == null || value.Length < 2 || value[0] != '"' || value[value.Length - 1] != '"')
                return value;

            return value.Substring(1, value.Length - 2);
        }

        /// <summary>
        /// False for the sign that an expression evaluated to nothing.
        /// </summary>
        public static bool HasValue(string value)
        {
            return !string.IsNullOrEmpty(value)
                && !string.Equals(value, "null", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(value, "Nothing", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// One line out of a multi-line text: real line breaks and the <c>\r\n</c> escape a debugger prints
        /// instead of them become single spaces, and runs of whitespace collapse.
        /// </summary>
        public static string Flatten(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            var result = new StringBuilder(value.Length);
            bool pendingSpace = false;

            for (int i = 0; i < value.Length; i++)
            {
                char current = value[i];

                if (current == '\\' && IsEscapedLineBreak(value, i))
                {
                    i += 3;
                    pendingSpace = true;
                    continue;
                }

                if (char.IsWhiteSpace(current))
                {
                    pendingSpace = true;
                    continue;
                }

                if (pendingSpace && result.Length > 0)
                    result.Append(' ');

                pendingSpace = false;
                result.Append(current);
            }

            return result.ToString();
        }

        /// <summary>True for the four characters of an escaped line break: <c>\r\n</c>.</summary>
        private static bool IsEscapedLineBreak(string value, int index)
        {
            return index + 3 < value.Length
                && value[index + 1] == 'r'
                && value[index + 2] == '\\'
                && value[index + 3] == 'n';
        }

        /// <summary>
        /// <c>int?</c> and <c>System.Nullable&lt;System.Int32&gt;</c> carry a simple value: the debugger
        /// prints the underlying one.
        /// </summary>
        private static string UnwrapNullable(string name)
        {
            if (name.EndsWith("?", StringComparison.Ordinal))
                name = name.Substring(0, name.Length - 1);

            const string nullablePrefix = "System.Nullable<";

            if (name.StartsWith(nullablePrefix, StringComparison.Ordinal) && name.EndsWith(">", StringComparison.Ordinal))
                name = name.Substring(nullablePrefix.Length, name.Length - nullablePrefix.Length - 1);

            return name.Trim();
        }

        private static string Trimmed(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
