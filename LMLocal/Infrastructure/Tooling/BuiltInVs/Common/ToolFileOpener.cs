using System;
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

    /// <summary>
    /// Opt-in: only results implementing <see cref="IToolFileOpenResult"/> can open a file, and only when they report that the file was written.
    /// </summary>
    internal class ToolFileOpener : IToolFileOpener
    {
        private readonly ISettingsManager _settingsManager;
        private readonly IVsDependencies _vsDependencies;
        private readonly IPathResolver _pathResolver;
        private readonly IFileSystem _fileSystem;
        private readonly Func<string, Task> _openFile;

        public ToolFileOpener(
            ISettingsManager settingsManager,
            IVsDependencies vsDependencies,
            IPathResolver pathResolver,
            IFileSystem fileSystem)
            : this(settingsManager, vsDependencies, pathResolver, fileSystem, FileViewer.OpenFileAsync)
        {
        }

        /// <summary>
        /// Test seam: <paramref name="openFile"/> replaces <see cref="FileViewer.OpenFileAsync"/>.
        /// </summary>
        internal ToolFileOpener(
            ISettingsManager settingsManager,
            IVsDependencies vsDependencies,
            IPathResolver pathResolver,
            IFileSystem fileSystem,
            Func<string, Task> openFile)
        {
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _vsDependencies = vsDependencies ?? throw new ArgumentNullException(nameof(vsDependencies));
            _pathResolver = pathResolver ?? throw new ArgumentNullException(nameof(pathResolver));
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            _openFile = openFile ?? throw new ArgumentNullException(nameof(openFile));
        }

        public async Task TryOpenEditedFileAsync(string toolName, object result)
        {
            try
            {
                if (!(result is IToolFileOpenResult openResult) || !openResult.WroteFile)
                    return;

                string filePath = openResult.FileToOpen;
                if (string.IsNullOrEmpty(filePath))
                    return;

                if (!IsOpenInEditorEnabled())
                    return;

                string solutionDir = _vsDependencies.GetSolutionDirectory();
                if (string.IsNullOrEmpty(solutionDir))
                    return;

                if (!_pathResolver.TryResolveFilePath(filePath, solutionDir, out string absolutePath))
                    return;

                if (!_fileSystem.FileExists(absolutePath))
                    return;

                await _openFile(absolutePath).ConfigureAwait(false);
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
    }
}
