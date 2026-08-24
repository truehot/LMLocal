using System;
using System.Collections.Generic;
using System.IO;
using EnvDTE;
using LMLocal.Core.Common;
using Microsoft.VisualStudio.Shell;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Projects
{
    /// <summary>
    /// Fields read from a DTE <see cref="Project"/> via <see cref="ProjectFinder.SafeName"/>.
    /// </summary>
    internal enum ProjectField
    {
        Name,
        UniqueName,
        FullName,
        Kind
    }

    /// <summary>
    /// Locates projects in the open solution and provides name normalization/matching helpers used by multiple tools. 
    /// </summary>
    internal static class ProjectFinder
    {
        internal const string SolutionFolderKind = "{66A26720-8FB5-11D2-AA7E-00C04F688DDE}";

        public static string NormalizeProjectName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            return name.Trim().Trim('"').Replace('/', '\\').TrimEnd('\\');
        }

        public static bool IsProjectNameMatch(string candidateName, string candidateUniqueName, string candidateFullName, string searchName)
        {
            string normalizedSearch = NormalizeProjectName(searchName);
            if (IsNameMatch(candidateName, normalizedSearch)) return true;
            if (IsNameMatch(candidateUniqueName, normalizedSearch)) return true;
            if (!string.IsNullOrEmpty(candidateFullName) &&
                IsNameMatch(Path.GetFileName(candidateFullName), normalizedSearch))
                return true;
            return false;
        }

        public static bool IsNameMatch(string candidate, string normalizedSearch)
        {
            if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(normalizedSearch)) return false;
            return string.Equals(NormalizeProjectName(candidate), normalizedSearch, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Finds all projects whose name, unique name or file name matches <paramref name="searchName"/>.
        /// </summary>
        public static List<Project> FindByName(Solution solution, string searchName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var results = new List<Project>();
            if (solution == null || string.IsNullOrWhiteSpace(searchName)) return results;

            foreach (Project p in solution.Projects)
            {
                if (p == null) continue;
                CollectByName(p, searchName, results);
            }
            return results;
        }

        /// <summary>
        /// Finds a single project whose full project file path equals projectPath.
        /// </summary>
        public static Project FindByPath(Solution solution, string projectPath)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (solution == null || string.IsNullOrEmpty(projectPath)) return null;

            string target = Path.GetFullPath(projectPath);
            foreach (Project p in solution.Projects)
            {
                if (p == null) continue;
                var found = FindInTree(p, target);
                if (found != null) return found;
            }
            return null;
        }

        public static bool IsSolutionFolder(Project project)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                return string.Equals(project.Kind, SolutionFolderKind, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                InternalLogger.Debug($"ProjectFinder: Could not read project kind: {ex.Message}");
                return false;
            }
        }

        public static string SafeName(Project project, ProjectField field)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                switch (field)
                {
                    case ProjectField.Name: return project.Name;
                    case ProjectField.UniqueName: return project.UniqueName;
                    case ProjectField.FullName: return project.FullName;
                    case ProjectField.Kind: return project.Kind;
                    default: return null;
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Debug($"ProjectFinder: Could not read project field {field}: {ex.Message}");
                return null;
            }
        }

        private static void CollectByName(Project project, string searchName, List<Project> results)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (project == null) return;

            if (IsSolutionFolder(project))
            {
                try
                {
                    foreach (ProjectItem item in project.ProjectItems)
                    {
                        if (item?.SubProject == null) continue;
                        CollectByName(item.SubProject, searchName, results);
                    }
                }
                catch (Exception ex)
                {
                    InternalLogger.Debug($"ProjectFinder: Could not enumerate solution folder contents: {ex.Message}");
                }
                return;
            }

            if (IsProjectNameMatch(
                    SafeName(project, ProjectField.Name),
                    SafeName(project, ProjectField.UniqueName),
                    SafeName(project, ProjectField.FullName),
                    searchName))
            {
                results.Add(project);
            }
        }

        private static Project FindInTree(Project project, string targetFullPath)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (project == null) return null;

            string fullName = SafeName(project, ProjectField.FullName);
            if (!string.IsNullOrEmpty(fullName) &&
                string.Equals(Path.GetFullPath(fullName), targetFullPath, StringComparison.OrdinalIgnoreCase))
                return project;

            if (!IsSolutionFolder(project))
                return null;

            try
            {
                foreach (ProjectItem item in project.ProjectItems)
                {
                    if (item?.SubProject == null) continue;
                    var found = FindInTree(item.SubProject, targetFullPath);
                    if (found != null) return found;
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Debug($"ProjectFinder: Could not enumerate solution folder contents: {ex.Message}");
            }
            return null;
        }
    }
}
