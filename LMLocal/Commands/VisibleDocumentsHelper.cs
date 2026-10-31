using System;
using System.Collections.Generic;
using LMLocal.Core.Common;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace LMLocal.Commands
{
    /// <summary>
    /// Enumerates document windows that are currently visible on screen (one per tab group).
    /// </summary>
    internal static class VisibleDocumentsHelper
    {
        /// <summary>
        /// Returns the full paths of the documents currently on screen, as a case-insensitive set.
        /// </summary>
        public static HashSet<string> GetVisibleDocumentPathSet()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (!(Package.GetGlobalService(typeof(SVsUIShell)) is IVsUIShell uiShell))
                {
                    InternalLogger.Warn("VisibleDocuments: SVsUIShell is not available — no documents collected.");
                    return paths;
                }

                uiShell.GetDocumentWindowEnum(out IEnumWindowFrames enumFrames);
                if (enumFrames == null)
                    return paths;

                var frames = new IVsWindowFrame[1];

                while (enumFrames.Next(1, frames, out uint fetched) == 0 && fetched == 1)
                {
                    IVsWindowFrame frame = frames[0];

                    frame.IsOnScreen(out int onScreen);
                    if (onScreen == 0)
                        continue;

                    frame.GetProperty((int)__VSFPROPID.VSFPROPID_pszMkDocument, out object pathObj);
                    if (pathObj is string filePath && !string.IsNullOrEmpty(filePath))
                        paths.Add(filePath);
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"VisibleDocuments: enumeration failed: {ex.Message}");
            }

            return paths;
        }
    }
}
