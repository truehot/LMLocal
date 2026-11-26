using System;
using System.Collections.Generic;
using System.Text;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common
{
    /// <summary>
    /// Builds a command line from an argv array using the CommandLineToArgvW rules of .NET Framework
    /// (ProcessStartInfo.Arguments is a single string there, there is no ArgumentList).
    /// </summary>
    internal static class CommandLineEscaper
    {
        /// <summary>Quotes one argument so that CommandLineToArgvW returns it verbatim.</summary>
        public static string Escape(string argument)
        {
            if (argument == null)
                return "\"\"";

            if (argument.Length > 0 && IndexOfSpecial(argument) < 0)
                return argument;

            var result = new StringBuilder(argument.Length + 2);
            result.Append('"');

            int backslashes = 0;
            foreach (char c in argument)
            {
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }

                if (c == '"')
                {
                    // Backslashes immediately before a quote must be doubled, plus one to escape the quote.
                    result.Append('\\', backslashes * 2 + 1);
                    result.Append('"');
                    backslashes = 0;
                    continue;
                }

                if (backslashes > 0)
                {
                    result.Append('\\', backslashes);
                    backslashes = 0;
                }

                result.Append(c);
            }

            // Backslashes at the end of a quoted argument must be doubled as well.
            if (backslashes > 0)
                result.Append('\\', backslashes * 2);

            result.Append('"');
            return result.ToString();
        }

        /// <summary>Joins arguments into a command line, escaping each one.</summary>
        public static string Join(params string[] arguments)
        {
            return JoinAll(arguments ?? new string[0]);
        }

        /// <summary>Joins arguments into a command line, escaping each one.</summary>
        public static string JoinAll(IEnumerable<string> arguments)
        {
            if (arguments == null)
                return string.Empty;

            var result = new StringBuilder();
            foreach (string argument in arguments)
            {
                if (result.Length > 0)
                    result.Append(' ');
                result.Append(Escape(argument));
            }

            return result.ToString();
        }

        private static int IndexOfSpecial(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\v' || c == '"')
                    return i;
            }

            return -1;
        }
    }
}
