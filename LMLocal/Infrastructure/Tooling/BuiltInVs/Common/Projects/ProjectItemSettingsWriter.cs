using System;
using System.Collections.Generic;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using LMLocal.Core.Common;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Projects
{
    /// <summary>
    /// Writes <see cref="FileProjectSettings"/> to a project item: the item type through the DTE <c>ItemType</c> property, the metadata
    /// (<c>CopyToOutputDirectory</c>, <c>IncludeInVSIX</c>) through <see cref="IVsBuildPropertyStorage.SetItemAttribute"/>, which
    /// creates the metadata element when it does not exist.
    /// </summary>
    internal static class ProjectItemSettingsWriter
    {
        internal const string CopyToOutputMetadata = "CopyToOutputDirectory";
        internal const string IncludeInVsixMetadata = "IncludeInVSIX";

        /// <summary>
        /// Applies <paramref name="settings"/> to <paramref name="item"/>. Returns the failures, one text each; empty when everything was written.
        /// </summary>
        public static List<string> Apply(IVsSolution solution, Project project, ProjectItem item, string absolutePath, FileProjectSettings settings)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var failures = new List<string>();
            if (settings?.HasAny != true)
                return failures;

            if (settings.BuildAction != null)
            {
                try
                {
                    item.Properties.Item("ItemType").Value = settings.BuildAction;
                }
                catch (Exception ex)
                {
                    InternalLogger.Warn($"ProjectItemSettingsWriter: could not set the item type of '{absolutePath}'", ex);
                    failures.Add($"item type {settings.BuildAction}: {ex.Message}");
                }
            }

            if (settings.CopyToOutput == null && !settings.IncludeInVsix.HasValue)
                return failures;

            IVsBuildPropertyStorage storage;
            uint itemId;
            try
            {
                if (solution == null)
                    throw new InvalidOperationException("the solution service is not available");

                if (ErrorHandler.Failed(solution.GetProjectOfUniqueName(project.UniqueName, out IVsHierarchy hierarchy)) || hierarchy == null)
                    throw new InvalidOperationException("the project is not part of the open solution");

                storage = hierarchy as IVsBuildPropertyStorage;
                if (storage == null)
                    throw new InvalidOperationException("the project does not support item metadata");

                if (ErrorHandler.Failed(hierarchy.ParseCanonicalName(absolutePath, out itemId)) || itemId == VSConstants.VSITEMID_NIL)
                    throw new InvalidOperationException("the file is not an item of the project");
            }
            catch (Exception ex)
            {
                if (settings.CopyToOutput != null) failures.Add($"{CopyToOutputMetadata}={settings.CopyToOutput}: {ex.Message}");
                if (settings.IncludeInVsix.HasValue) failures.Add($"{IncludeInVsixMetadata}: {ex.Message}");
                return failures;
            }

            if (settings.CopyToOutput != null)
                SetMetadata(storage, itemId, CopyToOutputMetadata, settings.CopyToOutput, failures);
            if (settings.IncludeInVsix.HasValue)
                SetMetadata(storage, itemId, IncludeInVsixMetadata, settings.IncludeInVsix.Value ? "true" : "false", failures);

            return failures;
        }

        private static void SetMetadata(IVsBuildPropertyStorage storage, uint itemId, string name, string value, List<string> failures)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                int hr = storage.SetItemAttribute(itemId, name, value);
                if (ErrorHandler.Failed(hr))
                    failures.Add($"{name}={value}: HRESULT 0x{hr:X8}");
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"ProjectItemSettingsWriter: could not write the item attribute {name}={value}", ex);
                failures.Add($"{name}={value}: {ex.Message}");
            }
        }
    }
}
