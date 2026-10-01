using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.VisualStudio;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common
{
    /// <summary>
    /// Opens files that were created or modified by a built-in tool directly in the Visual Studio editor,
    /// when the corresponding setting (OpenToolFilesInEditor) is enabled.
    /// </summary>
    internal interface IToolFileOpener
    {
        /// <summary>
        /// Tries to open the file affected by the tool result in the editor. Never throws.
        /// </summary>
        Task TryOpenEditedFileAsync(string toolName, object result);
    }

    internal class ToolFileOpener : IToolFileOpener
    {
        /// <summary>
        /// Tools that never open their file: they remove files or only change project metadata.
        /// </summary>
        private static readonly HashSet<string> IgnoredTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "delete_file",
            "set_file_project_status"
        };

        private const string FilePathPropertyName = "FilePath";

        private readonly ISettingsManager _settingsManager;
        private readonly IVsDependencies _vsDependencies;
        private readonly IPathResolver _pathResolver;
        private readonly IFileSystem _fileSystem;

        public ToolFileOpener(
            ISettingsManager settingsManager,
            IVsDependencies vsDependencies,
            IPathResolver pathResolver,
            IFileSystem fileSystem)
        {
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _vsDependencies = vsDependencies ?? throw new ArgumentNullException(nameof(vsDependencies));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        }

        public async Task TryOpenEditedFileAsync(string toolName, object result)
        {
            try
            {
                if (result == null || string.IsNullOrEmpty(toolName))
                    return;

                if (IgnoredTools.Contains(toolName))
                    return;

                if (!IsOpenInEditorEnabled())
                    return;

                string filePath = GetFilePathFromResult(result);
                if (string.IsNullOrEmpty(filePath))
                    return;

                string solutionDir = _vsDependencies?.GetSolutionDirectory();
                if (string.IsNullOrEmpty(solutionDir))
                    return;

                if (!_pathResolver.TryResolveFilePath(filePath, solutionDir, out string absolutePath))
                    return;

                if (!_fileSystem.FileExists(absolutePath))
                    return;

                await FileViewer.OpenFileAsync(absolutePath).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"Failed to open file of tool '{toolName}' in the editor: {ex.Message}");
            }
        }

        private bool IsOpenInEditorEnabled()
        {
            try
            {
                return _settingsManager?.Current?.OpenToolFilesInEditor ?? false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private static string GetFilePathFromResult(object result)
        {
            var property = result.GetType().GetProperty(FilePathPropertyName, BindingFlags.Public | BindingFlags.Instance);
            if (property == null || property.PropertyType != typeof(string))
                return null;

            return property.GetValue(result) as string;
        }
    }
}
