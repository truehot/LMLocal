using System;
using System.Collections.Generic;
using System.IO;
using LMLocal.Core.Common;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common
{
    /// <summary>
    /// High-level solution structure inspector.
    /// Provides cached solution overview: projects, file counts, languages, test project detection (by name only).
    /// </summary>
    internal static class SolutionInspector
    {
        private static (string solutionPath, DateTime timestamp, SolutionInfo overview)? _cache;

        /// <summary>
        /// Gets a high-level overview of the solution structure (loaded projects only).
        /// Results are cached; cache invalidates when .sln file is modified.
        /// Excludes solution folders and projects without file paths.
        /// </summary>
        public static SolutionInfo GetSolutionOverview(IVsSolution solution, int maxProjects = 200)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (solution == null)
                throw new ArgumentNullException(nameof(solution));

            solution.GetSolutionInfo(out string solutionDirectory, out string solutionFile, out string _);
            if (string.IsNullOrEmpty(solutionFile))
                throw new InvalidOperationException("No solution is currently open.");

            // Check cache validity
            DateTime lastWrite = DateTime.MinValue;
            try
            {
                if (File.Exists(solutionFile))
                    lastWrite = File.GetLastWriteTime(solutionFile);
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"SolutionInspector: Failed to get last write time for {solutionFile}: {ex.Message}");
            }

            bool cacheValid = _cache.HasValue &&
                              _cache.Value.solutionPath.Equals(solutionFile, StringComparison.OrdinalIgnoreCase) &&
                              _cache.Value.timestamp >= lastWrite;

            if (cacheValid)
            {
                return _cache.Value.overview;
            }
            else
            {
                ProjectMetadataProvider.ClearAll(); // Clear project metadata cache when solution changes
            }

            var projectsList = new List<ProjectInfo>();
            var solutionFolders = new List<string>();

            int totalFilesCount = 0;
            int projectsAddedCount = 0;

            bool limitExceeded = false;

            uint flags = (uint)__VSENUMPROJFLAGS.EPF_LOADEDINSOLUTION;
            Guid guid = Guid.Empty;
            solution.GetProjectEnum(flags, ref guid, out IEnumHierarchies enumHierarchies);

            var hierarchy = new IVsHierarchy[1];
            while (enumHierarchies.Next(1, hierarchy, out uint fetched) == VSConstants.S_OK && fetched == 1)
            {
                IVsHierarchy projectHierarchy = hierarchy[0];

                try
                {
                    if (IsSolutionFolder(projectHierarchy))
                    {
                        string folderName = GetProjectName(projectHierarchy);
                        if (!string.IsNullOrEmpty(folderName))
                            solutionFolders.Add(folderName);
                        continue;
                    }

                    string projectName = GetProjectName(projectHierarchy);
                    string projectFilePath = GetProjectFilePath(projectHierarchy);
                    if (string.IsNullOrEmpty(projectFilePath))
                    {
                        InternalLogger.Warn($"SolutionInspector: Project '{projectName}' has no file path, skipping.");
                        continue;
                    }

                    if (projectsAddedCount >= maxProjects)
                    {
                        limitExceeded = true;
                        continue;
                    }

                    string relativePath = GetRelativePath(solutionDirectory, projectFilePath);
                    int fileCount = CountSourceFilesInProject(projectHierarchy);
                    bool isTest = IsTestProject(projectName);
                    (string language, string framework) = ProjectMetadataProvider.GetMetadata(projectFilePath);

                    projectsList.Add(new ProjectInfo(projectName, language, relativePath, fileCount, isTest, framework));
                    totalFilesCount += fileCount;
                    projectsAddedCount++;
                }
                catch (Exception ex)
                {
                    InternalLogger.Warn($"SolutionInspector: Failed to process project: {ex.Message}");
                    continue;
                }
            }

            bool truncated = limitExceeded;

            var result = new SolutionInfo(
                Path.GetFileName(solutionFile),
                solutionFile,
                projectsAddedCount,
                totalFilesCount,
                projectsList,
                truncated,
                solutionFolders
            );

            _cache = (solutionFile, DateTime.Now, result);
            return result;
        }

        /// <summary>
        /// Checks if a hierarchy represents a solution folder (virtual container).
        /// </summary>
        private static bool IsSolutionFolder(IVsHierarchy hierarchy)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            const string solutionFolderGuid = "{66A26720-8FB5-11D2-AA7E-00C04F688DDE}";
            if (hierarchy.GetProperty(VSConstants.VSITEMID_ROOT, (int)__VSHPROPID.VSHPROPID_TypeGuid, out object typeGuidObj) == VSConstants.S_OK)
            {
                if (typeGuidObj is string typeGuid && typeGuid.Equals(solutionFolderGuid, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Gets the project name from hierarchy.
        /// </summary>
        private static string GetProjectName(IVsHierarchy hierarchy)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            string name = null;
            if (hierarchy.GetProperty(VSConstants.VSITEMID_ROOT, (int)__VSHPROPID.VSHPROPID_Name, out object nameObj) == VSConstants.S_OK)
                name = nameObj as string;
            if (string.IsNullOrEmpty(name))
            {
                if (hierarchy.GetProperty(VSConstants.VSITEMID_ROOT, (int)__VSHPROPID.VSHPROPID_Caption, out object captionObj) == VSConstants.S_OK)
                    name = captionObj as string;
            }
            return name ?? "Unknown";
        }

        /// <summary>
        /// Gets the project file path from hierarchy.
        /// </summary>
        private static string GetProjectFilePath(IVsHierarchy hierarchy)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (hierarchy is IVsProject project)
            {
                try
                {
                    if (project.GetMkDocument(VSConstants.VSITEMID_ROOT, out string projectFilePath) == VSConstants.S_OK)
                    {
                        if (!string.IsNullOrEmpty(projectFilePath))
                            return projectFilePath;
                    }
                }
                catch (Exception ex)
                {
                    InternalLogger.Warn($"SolutionInspector: Failed to get project file via GetMkDocument: {ex.Message}");
                }
            }

            if (hierarchy.GetProperty(VSConstants.VSITEMID_ROOT, (int)__VSHPROPID.VSHPROPID_ProjectDir, out object pathObj) == VSConstants.S_OK)
                return pathObj as string;

            return null;
        }

        /// <summary>
        /// Converts an absolute path to a relative path from the given base directory.
        /// </summary>
        private static string GetRelativePath(string basePath, string fullPath)
        {
            if (string.IsNullOrEmpty(basePath)) return fullPath;
            string baseDir = basePath.TrimEnd('\\', '/');
            string full = fullPath.TrimEnd('\\', '/');
            if (!full.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                return full;
            if (full.Length == baseDir.Length) return ".";
            string rel = full.Substring(baseDir.Length + 1);
            return rel.Replace('\\', Path.DirectorySeparatorChar);
        }

        /// <summary>
        /// Counts source files recursively in a project hierarchy.
        /// </summary>
        private static int CountSourceFilesInProject(IVsHierarchy hierarchy)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            int count = 0;
            CountFilesRecursive(hierarchy, VSConstants.VSITEMID_ROOT, ref count);
            return count;
        }

        private static void CountFilesRecursive(IVsHierarchy hierarchy, uint itemId, ref int count)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (hierarchy.GetProperty(itemId, (int)__VSHPROPID.VSHPROPID_FirstChild, out object childObj) != VSConstants.S_OK)
                return;
            if (!TryGetItemId(childObj, out uint childId))
                return;

            while (childId != VSConstants.VSITEMID_NIL)
            {
                bool hasChildren = hierarchy.GetProperty(childId, (int)__VSHPROPID.VSHPROPID_FirstChild, out object maybeChildObj) == VSConstants.S_OK
                                   && TryGetItemId(maybeChildObj, out uint _);
                if (hasChildren)
                {
                    CountFilesRecursive(hierarchy, childId, ref count);
                }
                else
                {
                    if (hierarchy.GetProperty(childId, (int)__VSHPROPID.VSHPROPID_Name, out object nameObj) == VSConstants.S_OK)
                    {
                        string fileName = nameObj as string;
                        if (!string.IsNullOrEmpty(fileName))
                        {
                            string ext = Path.GetExtension(fileName).ToLower();
                            if (IsSourceFileExtension(ext))
                                count++;
                        }
                    }
                }

                if (hierarchy.GetProperty(childId, (int)__VSHPROPID.VSHPROPID_NextSibling, out object nextObj) != VSConstants.S_OK)
                    break;
                if (!TryGetItemId(nextObj, out childId))
                    break;
            }
        }

        private static bool IsSourceFileExtension(string ext)
        {
            switch (ext)
            {
                case ".cs":
                case ".vb":
                case ".fs":
                case ".xaml":
                case ".resx":
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryGetItemId(object value, out uint id)
        {
            id = VSConstants.VSITEMID_NIL;
            if (value == null) return false;
            if (value is uint v)
            {
                id = v;
                return true;
            }
            if (value is int i)
            {
                if (i >= 0)
                {
                    id = (uint)i;
                    return true;
                }
            }
            if (value is long l)
            {
                if (l >= 0)
                {
                    id = (uint)l;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Determines if a project is a test project based on naming convention.
        /// May include false positives (e.g., "TestUtils" project).
        /// </summary>
        private static bool IsTestProject(string projectName)
        {
            return projectName.IndexOf("test", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   projectName.IndexOf("tests", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Project information for solution overview.
        /// </summary>
        public class ProjectInfo
        {
            public string Name { get; set; }
            public string Language { get; set; }
            public string Path { get; set; }
            public int FileCount { get; set; }
            public bool IsTestProject { get; set; }
            public string TargetFramework { get; set; }

            public ProjectInfo(string name, string language, string path, int fileCount, bool isTestProject, string targetFramework)
            {
                Name = name;
                Language = language;
                Path = path;
                FileCount = fileCount;
                IsTestProject = isTestProject;
                TargetFramework = targetFramework;
            }
        }

        /// <summary>
        /// Solution structure summary.
        /// </summary>
        public class SolutionInfo
        {
            public string SolutionName { get; set; }
            public string SolutionPath { get; set; }
            public int TotalProjects { get; set; }
            public int TotalFiles { get; set; }
            public List<ProjectInfo> Projects { get; set; }
            public bool Truncated { get; set; }
            public List<string> SolutionFolders { get; set; }

            public SolutionInfo(string solutionName, string solutionPath, int totalProjects, int totalFiles,
                List<ProjectInfo> projects, bool truncated, List<string> solutionFolders)
            {
                SolutionName = solutionName;
                SolutionPath = solutionPath;
                TotalProjects = totalProjects;
                TotalFiles = totalFiles;
                Projects = projects;
                Truncated = truncated;
                SolutionFolders = solutionFolders;
            }
        }

    }
}
