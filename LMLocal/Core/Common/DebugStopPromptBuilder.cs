using System;
using System.Collections.Generic;
using System.Text;
using LMLocal.Core.Models;

namespace LMLocal.Core.Common
{
    /// <summary>
    /// Builds the prompt that accompanies the "Explain" click on the debugger stop banner.
    /// </summary>
    internal static class DebugStopPromptBuilder
    {
        /// <summary>Maximum number of stack frames sent.</summary>
        public const int MaxFrames = 5;

        /// <summary>Maximum number of local variables sent.</summary>
        public const int MaxLocals = 20;

        /// <summary>Maximum length of a single variable value sent.</summary>
        public const int MaxValueLength = 200;

        /// <summary>Maximum depth of the exception chain sent.</summary>
        public const int MaxExceptionDepth = 3;

        /// <summary>
        /// Maximum length of one exception message sent. SQL and JSON messages can be enormous, and the
        /// whole prompt is paid for in tokens.
        /// </summary>
        public const int MaxMessageLength = 500;

        private const string ExceptionInstruction =
            "Explain this Visual Studio debugger stop. Visual Studio stopped because an exception was thrown. " +
            "Find the root cause: give the innermost InnerException priority over the outer exception type, " +
            "and describe the minimal fix for that cause instead of patching the symptom. " +
            "Only explain and describe the fix: do not apply any change and do not output a diff. " +
            "Read the code around the reported location before answering.";

        private const string BreakpointInstruction =
            "Explain this Visual Studio debugger stop. Visual Studio stopped at a breakpoint. " +
            "Analyse the state at the stop: say what looks suspicious and what may break in the next few lines. " +
            "If nothing looks wrong, say so instead of inventing a bug. " +
            "Only explain and describe the fix: do not apply any change and do not output a diff. " +
            "Read the code around the reported location before answering.";

        /// <summary>
        /// Builds the instruction plus the stop, its location, the exception chain, the stack and the
        /// locals. Returns null when there is no stop to describe. A stop without frames, locals or an
        /// exception still yields a minimal prompt.
        /// </summary>
        /// <param name="context">Context collected while Visual Studio is in Break Mode.</param>
        /// <param name="toRelativePath">Optional path converter (full path to solution-relative path). May be null.</param>
        public static string Build(DebugStopContext context, Func<string, string> toRelativePath)
        {
            DebugStopInfo info = context?.Info;
            if (info == null)
                return null;

            var result = new StringBuilder();
            result.Append(info.Kind == DebugStopKind.Exception ? ExceptionInstruction : BreakpointInstruction);
            result.Append("\n\n");
            result.Append("Stop: ").Append(BuildCaption(info));
            result.Append("\nLocation: ").Append(BuildLocation(info, toRelativePath));

            AppendExceptions(result, context.Exceptions);
            AppendFrames(result, context.Frames, toRelativePath);
            AppendLocals(result, context.Locals);

            return result.ToString();
        }

        /// <summary>
        /// One plain line naming the reason, so that the model does not have to interpret enum names.
        /// </summary>
        private static string BuildCaption(DebugStopInfo info)
        {
            switch (info.Reason)
            {
                case DebugStopReason.Breakpoint:
                    return "break mode on a breakpoint";
                case DebugStopReason.UserBreak:
                    return "break mode on a user break";
                case DebugStopReason.ExceptionThrown:
                    return "break mode on an exception";
                case DebugStopReason.ExceptionNotHandled:
                    return "break mode on an unhandled exception";
                case DebugStopReason.None:
                    return "break mode";
                default:
                    return $"break mode ({info.Reason})";
            }
        }

        /// <summary>
        /// "SomeService.cs, line 42"; a stop without a file degrades to "unknown" instead of failing.
        /// </summary>
        private static string BuildLocation(DebugStopInfo info, Func<string, string> toRelativePath)
        {
            string file = Trimmed(info.File);
            if (file == null)
                return "unknown";

            string path = ToDisplayPath(file, toRelativePath);
            return info.Line > 0 ? $"{path}, line {info.Line}" : path;
        }

        /// <summary>
        /// The exception chain, outermost first; inner levels are marked so the model sees the order.
        /// </summary>
        private static void AppendExceptions(StringBuilder result, IReadOnlyList<DebugExceptionInfo> exceptions)
        {
            if (exceptions == null)
                return;

            var lines = new List<string>();
            foreach (DebugExceptionInfo exception in exceptions)
            {
                if (exception == null)
                    continue;

                if (lines.Count >= MaxExceptionDepth)
                    break;

                string line = BuildException(exception, outermost: lines.Count == 0);
                if (line == null)
                    continue;

                lines.Add($"{lines.Count + 1}. {line}");
            }

            if (lines.Count == 0)
                return;

            result.Append("\n\nException:");
            foreach (string line in lines)
                result.Append('\n').Append(line);
        }

        private static string BuildException(DebugExceptionInfo exception, bool outermost)
        {
            string type = Trimmed(exception.Type);
            string message = BuildMessage(exception.Message);

            if (type == null && message == null)
                return null;

            string body = type ?? "(unknown type)";
            if (message != null)
                body += ": " + message;

            return outermost ? body : "Inner: " + body;
        }

        /// <summary>
        /// One line out of a message: real line breaks and the escapes the debugger prints instead of them
        /// become spaces, and an overlong message is cut.
        /// </summary>
        private static string BuildMessage(string message)
        {
            string flattened = Trimmed(DebugStopText.Flatten(message));
            return flattened == null ? null : PromptText.Truncate(flattened, MaxMessageLength);
        }

        /// <summary>
        /// The stack, innermost first. Frames without a PDB keep the name only.
        /// </summary>
        private static void AppendFrames(
            StringBuilder result,
            IReadOnlyList<DebugStackFrameInfo> frames,
            Func<string, string> toRelativePath)
        {
            if (frames == null)
                return;

            var lines = new List<string>();
            foreach (DebugStackFrameInfo frame in frames)
            {
                if (frame == null)
                    continue;

                if (lines.Count >= MaxFrames)
                    break;

                lines.Add($"{lines.Count + 1}. {BuildFrame(frame, toRelativePath)}");
            }

            if (lines.Count == 0)
                return;

            result.Append("\n\nStack:");
            foreach (string line in lines)
                result.Append('\n').Append(line);
        }

        private static string BuildFrame(DebugStackFrameInfo frame, Func<string, string> toRelativePath)
        {
            string name = Trimmed(frame.FunctionName) ?? "(unknown frame)";

            string file = Trimmed(frame.File);
            if (file == null)
                return name;

            string path = ToDisplayPath(file, toRelativePath);
            return frame.Line > 0 ? $"{name} at {path}:{frame.Line}" : $"{name} at {path}";
        }

        /// <summary>
        /// Locals of the current frame; a variable whose value was not read keeps the name and the type only.
        /// </summary>
        private static void AppendLocals(StringBuilder result, IReadOnlyList<DebugLocalVariable> locals)
        {
            if (locals == null)
                return;

            var lines = new List<string>();
            foreach (DebugLocalVariable local in locals)
            {
                if (local == null)
                    continue;

                string name = Trimmed(local.Name);
                if (name == null)
                    continue;

                if (lines.Count >= MaxLocals)
                    break;

                lines.Add("- " + BuildLocal(local, name));
            }

            if (lines.Count == 0)
                return;

            result.Append("\n\nLocals of the current frame:");
            foreach (string line in lines)
                result.Append('\n').Append(line);
        }

        private static string BuildLocal(DebugLocalVariable local, string name)
        {
            string type = Trimmed(local.Type);
            string header = type == null ? name : $"{name} ({type})";

            // A credential never reaches the prompt, whatever the collector put into the value.
            if (DebugLocalVariable.IsSensitiveName(name))
                return $"{header}: {DebugLocalVariable.HiddenValue}";

            if (local.Value == null)
                return header;

            return $"{header}: {PromptText.Truncate(DebugStopText.Flatten(local.Value), MaxValueLength)}";
        }

        /// <summary>
        /// Applies the caller's path converter. A converter that throws or returns nothing degrades to the full path.
        /// </summary>
        private static string ToDisplayPath(string fullPath, Func<string, string> toRelativePath)
        {
            if (toRelativePath == null)
                return fullPath;

            try
            {
                string converted = toRelativePath(fullPath);
                return string.IsNullOrWhiteSpace(converted) ? fullPath : converted;
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"ExplainDebugStop: cannot relativize '{fullPath}'", ex);
                return fullPath;
            }
        }

        private static string Trimmed(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
