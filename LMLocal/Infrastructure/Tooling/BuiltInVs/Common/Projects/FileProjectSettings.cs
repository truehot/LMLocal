using System;
using System.Collections.Generic;
using System.Linq;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Projects
{
    /// <summary>
    /// How a file is registered in a project, beyond "it is a member": the item type (build action), whether it is copied to the output
    /// folder and whether the VSIX package carries it.
    /// </summary>
    internal sealed class FileProjectSettings
    {
        internal const string BuildActionParameter = "build_action";
        internal const string CopyToOutputParameter = "copy_to_output";
        internal const string IncludeInVsixParameter = "include_in_vsix";

        internal static readonly string[] BuildActions = { "Compile", "Content", "None", "EmbeddedResource" };
        internal static readonly string[] CopyToOutputValues = { "Never", "Always", "PreserveNewest" };

        /// <summary>Item type (<c>Compile</c>, <c>Content</c>, <c>None</c>, <c>EmbeddedResource</c>), or null to leave it as the project system chose.</summary>
        public string BuildAction { get; private set; }

        /// <summary>Value of <c>CopyToOutputDirectory</c> (<c>Never</c>, <c>Always</c>, <c>PreserveNewest</c>), or null to leave it.</summary>
        public string CopyToOutput { get; private set; }

        /// <summary>Value of <c>IncludeInVSIX</c>, or null to leave it.</summary>
        public bool? IncludeInVsix { get; private set; }

        public bool HasAny => BuildAction != null || CopyToOutput != null || IncludeInVsix.HasValue;

        public static readonly FileProjectSettings None = new FileProjectSettings();

        /// <summary>
        /// Reads the three optional parameters.
        /// </summary>
        public static bool TryParse(Dictionary<string, object> parameters, out FileProjectSettings settings, out string error)
        {
            settings = new FileProjectSettings();
            error = null;
            if (parameters == null)
                return true;

            if (!TryReadChoice(parameters, BuildActionParameter, BuildActions, out string buildAction, out error))
                return false;
            if (!TryReadChoice(parameters, CopyToOutputParameter, CopyToOutputValues, out string copyToOutput, out error))
                return false;

            bool? includeInVsix = null;
            if (parameters.TryGetValue(IncludeInVsixParameter, out object raw) && raw != null && !string.IsNullOrWhiteSpace(raw.ToString()))
            {
                if (raw is bool flag)
                    includeInVsix = flag;
                else if (bool.TryParse(raw.ToString().Trim(), out bool parsed))
                    includeInVsix = parsed;
                else
                {
                    error = $"Parameter '{IncludeInVsixParameter}' must be true or false.";
                    return false;
                }
            }

            settings.BuildAction = buildAction;
            settings.CopyToOutput = copyToOutput;
            settings.IncludeInVsix = includeInVsix;
            return true;
        }

        /// <summary>The settings as a short text for the tool result, e.g. <c>item type Content, CopyToOutputDirectory=PreserveNewest, IncludeInVSIX=true</c>.</summary>
        public string Describe()
        {
            var parts = new List<string>();
            if (BuildAction != null) parts.Add("item type " + BuildAction);
            if (CopyToOutput != null) parts.Add("CopyToOutputDirectory=" + CopyToOutput);
            if (IncludeInVsix.HasValue) parts.Add("IncludeInVSIX=" + (IncludeInVsix.Value ? "true" : "false"));
            return string.Join(", ", parts);
        }

        private static bool TryReadChoice(Dictionary<string, object> parameters, string name, string[] allowed, out string value, out string error)
        {
            value = null;
            error = null;
            if (!parameters.TryGetValue(name, out object raw) || raw == null)
                return true;

            string text = raw.ToString().Trim();
            if (text.Length == 0)
                return true;

            value = allowed.FirstOrDefault(a => string.Equals(a, text, StringComparison.OrdinalIgnoreCase));
            if (value != null)
                return true;

            error = $"Parameter '{name}' must be one of: {string.Join(", ", allowed)}.";
            return false;
        }
    }
}
