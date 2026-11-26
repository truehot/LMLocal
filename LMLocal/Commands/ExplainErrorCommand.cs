using System;
using System.Collections.Generic;
using System.Globalization;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.ChatSession;
using LMLocal.Core.Common;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Shell.TableManager;

namespace LMLocal.Commands
{
    /// <summary>
    /// Error List context menu command: explains the selected entries with the "Explain" instruction.
    /// </summary>
    internal sealed class ExplainErrorCommand : BaseCodeCommand
    {
        protected override int CommandId => 0x020E;

        protected override string InstructionDisplayName => "Explain";

        protected override string PromptInstruction =>
            "Explain the following Visual Studio Error List entries: what each one means, what most likely causes it in this code, and how to fix it. Describe the fix, do not apply it. Read the code around the reported locations before answering.";

        private ExplainErrorCommand(
            AsyncPackage package,
            OleMenuCommandService commandService,
            ISessionManager sessionManager,
            IInstructionsManager instructionsManager)
            : base(package, commandService, sessionManager, instructionsManager) { }

        protected override void OnBeforeQueryStatus(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            base.OnBeforeQueryStatus(sender, e);

            if (sender is OleMenuCommand menuCommand && menuCommand.Enabled)
            {
                menuCommand.Enabled = HasSelectedEntries();
            }
        }

        /// <summary>
        /// Cheap check used while the menu opens: no per-row reads and no logging, since it runs on every menu query.
        /// </summary>
        private static bool HasSelectedEntries()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var selectedEntries = (Package.GetGlobalService(typeof(SVsErrorList)) as IErrorList)
                    ?.TableControl?.SelectedEntries;
                if (selectedEntries == null)
                    return false;

                using (var enumerator = selectedEntries.GetEnumerator())
                {
                    return enumerator.MoveNext();
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        protected override string BuildPrompt()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var entries = CollectSelectedEntries();
            if (entries.Count == 0)
            {
                InternalLogger.Warn("ExplainError: no Error List entries selected — nothing to send.");
                return null;
            }

            return ErrorListPromptBuilder.Build(
                PromptInstruction,
                entries,
                CodeCommandHelper.GetRelativePath);
        }

        /// <summary>
        /// Reads the Error List rows currently selected. Never throws: a broken table yields an empty list.
        /// </summary>
        private static List<ErrorListEntry> CollectSelectedEntries()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var result = new List<ErrorListEntry>();

            try
            {
                if (!(Package.GetGlobalService(typeof(SVsErrorList)) is IErrorList errorList))
                {
                    InternalLogger.Warn("ExplainError: the Error List service is not available.");
                    return result;
                }

                var tableControl = errorList.TableControl;
                if (tableControl == null)
                {
                    InternalLogger.Warn("ExplainError: the Error List table control is not created yet.");
                    return result;
                }

                var selectedEntries = tableControl.SelectedEntries;
                if (selectedEntries == null)
                {
                    InternalLogger.Info("ExplainError: nothing is selected in the Error List.");
                    return result;
                }

                foreach (var entryHandle in selectedEntries)
                {
                    if (entryHandle == null)
                        continue;

                    var entry = new ErrorListEntry
                    {
                        ErrorCode = GetString(entryHandle, StandardTableKeyNames.ErrorCode),
                        Severity = MapSeverity(entryHandle),
                        Text = GetString(entryHandle, StandardTableKeyNames.Text),
                        FilePath = GetString(entryHandle, StandardTableKeyNames.DocumentName),
                        Line = GetInt(entryHandle, StandardTableKeyNames.Line),
                        Column = GetInt(entryHandle, StandardTableKeyNames.Column),
                        ProjectName = GetString(entryHandle, StandardTableKeyNames.ProjectName)
                    };

                    InternalLogger.Info(
                        $"ExplainError: selected '{entry.ErrorCode}' at '{entry.FilePath}' raw line/column {entry.Line}/{entry.Column}.");

                    result.Add(entry);
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Warn("ExplainError: cannot read the Error List selection", ex);
            }

            return result;
        }

        /// <summary>
        /// Reads a text column of a table row. Missing or unreadable values become null.
        /// </summary>
        private static string GetString(ITableEntry entry, string keyName)
        {
            try
            {
                if (!entry.TryGetValue(keyName, out object content) || content == null)
                    return null;

                return content as string ?? content.ToString();
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"ExplainError: cannot read '{keyName}'", ex);
                return null;
            }
        }

        /// <summary>
        /// Reads a numeric column of a table row. Missing or unreadable values become 0.
        /// </summary>
        private static int GetInt(ITableEntry entry, string keyName)
        {
            try
            {
                if (!entry.TryGetValue(keyName, out object content) || content == null)
                    return 0;

                return Convert.ToInt32(content, CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"ExplainError: cannot read '{keyName}'", ex);
                return 0;
            }
        }

        /// <summary>
        /// Maps the Error List severity onto a plain word; unknown values are omitted from the prompt.
        /// </summary>
        private static string MapSeverity(ITableEntry entry)
        {
            object content;
            try
            {
                if (!entry.TryGetValue(StandardTableKeyNames.ErrorSeverity, out content) || content == null)
                    return null;
            }
            catch (Exception ex)
            {
                InternalLogger.Warn("ExplainError: cannot read the severity", ex);
                return null;
            }

            string text = content as string;
            if (!string.IsNullOrEmpty(text))
            {
                string normalized = text.Trim().ToLowerInvariant();
                return normalized == "error" || normalized == "warning" || normalized == "message"
                    ? normalized
                    : null;
            }

            try
            {
                // __VSERRORCATEGORY: EC_ERROR = 0, EC_WARNING = 1, EC_MESSAGE = 2.
                switch (Convert.ToInt32(content, CultureInfo.InvariantCulture))
                {
                    case 0: return "error";
                    case 1: return "warning";
                    case 2: return "message";
                    default: return null;
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"ExplainError: unexpected severity value '{content}'", ex);
                return null;
            }
        }
    }
}
