using System;

namespace LMLocal.Core.Models
{
    /// <summary>
    /// One local variable of the current stack frame, already reduced to what a prompt may show.
    /// </summary>
    public sealed class DebugLocalVariable
    {
        /// <summary>Shown instead of the real value when the name looks sensitive.</summary>
        public const string HiddenValue = "<hidden>";

        private static readonly string[] SensitiveNameFragments =
        {
            "password",
            "passwd",
            "secret",
            "token",
            "apikey",
            "api_key",
            "connectionstring"
        };

        /// <summary>Name as the debugger shows it.</summary>
        public string Name { get; set; }

        /// <summary>Declared type as the debugger shows it. Null when unknown.</summary>
        public string Type { get; set; }

        /// <summary>
        /// Value for simple types only, null when it was not read on purpose (objects, collections)
        /// or when the name is sensitive.
        /// </summary>
        public string Value { get; set; }

        /// <summary>
        /// True when the name suggests a credential.
        /// </summary>
        public static bool IsSensitiveName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;

            foreach (string fragment in SensitiveNameFragments)
            {
                if (name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }
    }
}
