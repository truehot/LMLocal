using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using EnvDTE;
using EnvDTE80;
using LMLocal.Application.ChatSession;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.DependencyInjection;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace LMLocal.Commands
{
    /// <summary>
    /// Command handler for "Send to LM Local" context menu items in the Solution Explorer.
    /// </summary>
    internal sealed class SendToLMFromSolutionExplorerCommand
    {
        public const int CommandIdItem = 0x0208;
        public const int CommandIdFolder = 0x0209;
        public const int CommandIdProject = 0x020A;
        public const int CommandIdSolution = 0x020B;
        public static readonly Guid CommandSet = new Guid("c29700c4-7786-468f-bf99-0ecb9d69343f");

        private const int MaxFilesWithContent = 10;
        private const int MaxTotalContentBytes = 200 * 1024; // 200 KB
        private const int MaxFolderFlatFiles = 20;

        private static readonly string SolutionFolderKind = "{66A26720-8FB5-11D2-AA7E-00C04F688DDE}";

        private static readonly HashSet<string> ExcludedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bin", "obj", ".vs", ".git", "CopilotBaseline", "node_modules", "packages"
        };

        private readonly AsyncPackage _package;
        private readonly ISessionManager _sessionManager;

        private SendToLMFromSolutionExplorerCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            _sessionManager = ServiceConfiguration.GetService<ISessionManager>();

            RegisterCommand(commandService, CommandIdItem, Execute);
            RegisterCommand(commandService, CommandIdFolder, Execute);
            RegisterCommand(commandService, CommandIdProject, Execute);
            RegisterCommand(commandService, CommandIdSolution, Execute);
        }

        private void RegisterCommand(OleMenuCommandService commandService, int commandId, EventHandler executeHandler)
        {
            var menuCommandID = new CommandID(CommandSet, commandId);
            var menuItem = new OleMenuCommand(executeHandler, menuCommandID);
            menuItem.BeforeQueryStatus += OnBeforeQueryStatus;
            commandService.AddCommand(menuItem);
        }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            if (await package.GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService commandService)
            {
                new SendToLMFromSolutionExplorerCommand(package, commandService);
            }
        }

        private void OnBeforeQueryStatus(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (sender is OleMenuCommand menuCommand)
            {
                bool isBusy = _sessionManager?.IsSessionRunning ?? false;
                menuCommand.Enabled = !isBusy;

                // For Item and Folder commands (both in SolutionExplorerItemGroup), show only the relevant button for the current selection
                if (menuCommand.CommandID.ID == CommandIdItem || menuCommand.CommandID.ID == CommandIdFolder)
                {
                    menuCommand.Visible = IsSelectionMatchCommand(menuCommand.CommandID.ID);
                }
                else
                {
                    menuCommand.Visible = true;
                }
            }
        }

        private static bool IsSelectionMatchCommand(int commandId)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var dte = Package.GetGlobalService(typeof(DTE)) as DTE2;
            object[] rawItems = dte?.ToolWindows?.SolutionExplorer?.SelectedItems as object[];
            if (rawItems == null || rawItems.Length == 0)
                return false;

            var items = rawItems.Cast<UIHierarchyItem>().ToArray();

            bool hasFile = false;
            bool hasFolder = false;

            foreach (var item in items)
            {
                switch (item.Object)
                {
                    case ProjectItem pi:
                        bool isFolder = pi.FileCount == 0 || (pi.ProjectItems != null && pi.ProjectItems.Count > 0);
                        if (isFolder)
                            hasFolder = true;
                        else
                            hasFile = true;
                        break;

                    case Project proj when string.Equals(proj.Kind, SolutionFolderKind, StringComparison.OrdinalIgnoreCase):
                        hasFolder = true;
                        break;

                    default:
                        hasFile = true;
                        break;
                }
            }

            return commandId == CommandIdItem
                ? hasFile && !hasFolder   // only files, no folders
                : hasFolder;              // at least one folder
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var menuCommand = (OleMenuCommand)sender;
            int commandId = menuCommand.CommandID.ID;

            var dte = Package.GetGlobalService(typeof(DTE)) as DTE2;
            object[] rawItems = dte?.ToolWindows?.SolutionExplorer?.SelectedItems as object[];
            if (rawItems == null || rawItems.Length == 0)
            {
                InternalLogger.Warn("SendToLMFromSE: no items selected in Solution Explorer.");
                return;
            }

            var uiItems = rawItems.Cast<UIHierarchyItem>().ToArray();
            var entries = CollectEntries(uiItems, commandId);

            if (entries.Count == 0)
            {
                InternalLogger.Warn("SendToLMFromSE: no file entries collected.");
                return;
            }

            string markdown = BuildMultiFileMarkdown(entries);

            if (string.IsNullOrWhiteSpace(markdown))
                return;

            _ = _package.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    await CodeCommandHelper.InjectIntoChatAsync(_package, markdown);
                }
                catch (Exception ex)
                {
                    InternalLogger.Error($"SendToLMFromSE: injection failed: {ex.Message}");
                }
            });
        }

        internal class FileEntry
        {
            public string Path { get; set; }
            public string Content { get; set; }
            public bool IsTruncated { get; set; }
            public bool IsTree { get; set; }
            public string TreeText { get; set; }
        }

        private List<FileEntry> CollectEntries(UIHierarchyItem[] items, int commandId)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var result = new List<FileEntry>();
            int totalBytes = 0;

            foreach (var item in items)
            {
                if (result.Count >= MaxFilesWithContent)
                    break;

                switch (item.Object)
                {
                    case ProjectItem pi:
                        bool isFolder = pi.FileCount == 0 || (pi.ProjectItems != null && pi.ProjectItems.Count > 0);
                        if (commandId == CommandIdFolder && isFolder)
                            CollectFromProjectItem(pi, result, ref totalBytes);
                        else if (commandId == CommandIdItem && !isFolder)
                            CollectFromProjectItem(pi, result, ref totalBytes);
                        break;

                    case Project proj:
                        bool isSolutionFolder = string.Equals(proj.Kind, SolutionFolderKind, StringComparison.OrdinalIgnoreCase);
                        if (commandId == CommandIdFolder && isSolutionFolder)
                            CollectFromSolutionFolder(proj, result, ref totalBytes);
                        else if (commandId == CommandIdProject && !isSolutionFolder)
                            result.Add(BuildProjectTree(proj));
                        break;

                    default:
                        InternalLogger.Warn($"SendToLMFromSE: unexpected item type: {item.Object?.GetType().Name}");
                        break;
                }
            }

            return result;
        }

        private void CollectFromProjectItem(ProjectItem pi, List<FileEntry> result, ref int totalBytes)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                bool isFolder = pi.FileCount == 0 || (pi.ProjectItems != null && pi.ProjectItems.Count > 0);

                if (isFolder)
                {
                    CollectFolderEntries(pi, result, ref totalBytes);
                }
                else
                {
                    string fullPath = GetProjectItemFullPath(pi);
                    if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
                        return;

                    if (ShouldExclude(fullPath))
                        return;

                    AddFileEntry(fullPath, result, ref totalBytes);
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"SendToLMFromSE: failed to process ProjectItem '{pi.Name}': {ex.Message}");
            }
        }

        private void CollectFolderEntries(ProjectItem folderPi, List<FileEntry> result, ref int totalBytes)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var allFiles = new List<string>();
            CollectAllFilesRecursive(folderPi.ProjectItems, allFiles);

            if (allFiles.Count <= MaxFolderFlatFiles)
            {
                foreach (string filePath in allFiles)
                {
                    if (result.Count >= MaxFilesWithContent)
                        break;

                    AddFileEntry(filePath, result, ref totalBytes);
                }
            }
            else
            {
                result.Add(BuildFolderTree(folderPi));
            }
        }

        private void CollectFromSolutionFolder(Project solutionFolder, List<FileEntry> result, ref int totalBytes)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                foreach (var rawItem in solutionFolder.ProjectItems)
                {
                    if (result.Count >= MaxFilesWithContent)
                        break;

                    if (rawItem is ProjectItem pi)
                    {
                        CollectFromProjectItem(pi, result, ref totalBytes);
                    }
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"SendToLMFromSE: failed to process Solution Folder '{solutionFolder.Name}': {ex.Message}");
            }
        }

        private void CollectAllFilesRecursive(ProjectItems items, List<string> allFiles)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (items == null)
                return;

            foreach (ProjectItem child in items)
            {
                try
                {
                    bool isFolder = child.ProjectItems != null && child.ProjectItems.Count > 0;
                    if (isFolder)
                    {
                        CollectAllFilesRecursive(child.ProjectItems, allFiles);
                    }
                    else
                    {
                        string fullPath = GetProjectItemFullPath(child);
                        if (!string.IsNullOrEmpty(fullPath) && File.Exists(fullPath) && !ShouldExclude(fullPath))
                        {
                            allFiles.Add(fullPath);
                        }
                    }
                }
                catch
                {
                    // Skip items we can't read (e.g. disposed, virtual, or malformed)
                }
            }
        }

        private void AddFileEntry(string fullPath, List<FileEntry> result, ref int totalBytes)
        {
            try
            {
                long fileLength = new FileInfo(fullPath).Length;

                if (totalBytes + fileLength > MaxTotalContentBytes)
                {
                    result.Add(new FileEntry
                    {
                        Path = fullPath,
                        Content = null,
                        IsTruncated = true
                    });
                    return;
                }

                string content = File.ReadAllText(fullPath);
                result.Add(new FileEntry
                {
                    Path = fullPath,
                    Content = content,
                    IsTruncated = false
                });
                totalBytes += content.Length;
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"SendToLMFromSE: failed to read file '{fullPath}': {ex.Message}");
                result.Add(new FileEntry
                {
                    Path = fullPath,
                    Content = null,
                    IsTruncated = true
                });
            }
        }

        private FileEntry BuildProjectTree(Project project)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var lines = new List<string> { $"// Project: {project.Name}" };
            AppendProjectItemsTree(project.ProjectItems, lines, 1);

            return new FileEntry
            {
                Path = project.FullName,
                IsTree = true,
                TreeText = string.Join("\n", lines)
            };
        }

        private FileEntry BuildFolderTree(ProjectItem folderPi)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var lines = new List<string> { $"// Folder: {folderPi.Name}/" };
            AppendProjectItemsTree(folderPi.ProjectItems, lines, 1);

            return new FileEntry
            {
                Path = GetProjectItemFullPath(folderPi),
                IsTree = true,
                TreeText = string.Join("\n", lines)
            };
        }

        private void AppendProjectItemsTree(ProjectItems items, List<string> lines, int indentLevel)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (items == null)
                return;

            string indent = new string(' ', indentLevel * 2);

            foreach (ProjectItem item in items)
            {
                try
                {
                    bool isFolder = item.ProjectItems != null && item.ProjectItems.Count > 0;
                    if (isFolder)
                    {
                        lines.Add($"{indent}{item.Name}/");
                        AppendProjectItemsTree(item.ProjectItems, lines, indentLevel + 1);
                    }
                    else
                    {
                        lines.Add($"{indent}{item.Name}");
                    }
                }
                catch
                {
                    // Skip items we can't read (e.g. disposed, virtual, or malformed)
                }
            }
        }

        private string GetProjectItemFullPath(ProjectItem pi)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                if (pi.Properties != null)
                {
                    var prop = pi.Properties.Item("FullPath");
                    if (prop?.Value != null)
                        return prop.Value.ToString();
                }

                if (pi.FileCount > 0)
                {
                    return pi.FileNames[0];
                }
            }
            catch
            {
                // Occurs for solution folders or virtual items
            }

            return null;
        }

        internal static bool ShouldExclude(string path)
        {
            foreach (var dir in ExcludedDirectories)
            {
                if (path.IndexOf(Path.DirectorySeparatorChar + dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                if (path.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            string ext = Path.GetExtension(path)?.ToLowerInvariant();
            if (ext == ".exe" || ext == ".dll" || ext == ".pdb" || ext == ".png" || ext == ".jpg" ||
                ext == ".jpeg" || ext == ".gif" || ext == ".bmp" || ext == ".ico" || ext == ".svg")
                return true;

            return false;
        }

        /// <summary>
        /// Builds a combined markdown string from all collected file entries.
        /// </summary>
        internal static string BuildMultiFileMarkdown(List<FileEntry> entries)
        {
            var parts = new List<string>();

            foreach (var entry in entries)
            {
                if (entry.IsTree)
                {
                    parts.Add(entry.TreeText);
                    continue;
                }

                if (entry.Content == null)
                {
                    string lang = MarkdownLanguageHelper.GetLanguageFromExtension(entry.Path);
                    string langTag = !string.IsNullOrEmpty(lang) ? lang : "";
                    parts.Add($"```{langTag}");
                    parts.Add($"// file: {entry.Path} (content truncated, file too large)");
                    parts.Add("```");
                    continue;
                }

                string language = MarkdownLanguageHelper.GetLanguageFromExtension(entry.Path);
                string languageTag = !string.IsNullOrEmpty(language) ? language : "";

                parts.Add($"```{languageTag}");
                parts.Add($"// file: {entry.Path}");
                parts.Add(entry.Content);
                parts.Add("```");
            }

            return string.Join("\n\n", parts);
        }
    }
}
