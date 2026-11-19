using System;
using System.Globalization;
using EnvDTE;
using EnvDTE80;
using LMLocal.Core.Common;
using Microsoft.VisualStudio.Shell;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Projects
{
    /// <summary>
    /// Configuration that a build produces and a test run must consume: the active solution configuration, the
    /// project configuration it maps to (<c>SolutionContexts</c>) and the project's raw <c>OutputPath</c>.
    /// </summary>
    internal sealed class ProjectBuildConfiguration
    {
        /// <summary>
        /// Nothing could be read: no active solution configuration at all.
        /// </summary>
        public static readonly ProjectBuildConfiguration Empty = new ProjectBuildConfiguration(
            null, null, null, null, null, "No active solution configuration is available.");

        public ProjectBuildConfiguration(
            string solutionConfigurationName,
            string solutionPlatformName,
            string projectConfigurationName,
            string projectPlatformName,
            string outputPath,
            string resolutionError)
        {
            SolutionConfigurationName = solutionConfigurationName;
            SolutionPlatformName = solutionPlatformName;
            ProjectConfigurationName = projectConfigurationName;
            ProjectPlatformName = projectPlatformName;
            OutputPath = outputPath;
            ResolutionError = resolutionError;
        }

        /// <summary>
        /// Name of the active solution configuration (<c>Debug</c>).
        /// </summary>
        public string SolutionConfigurationName { get; }

        /// <summary>
        /// Platform of the active solution configuration (<c>Any CPU</c>).
        /// </summary>
        public string SolutionPlatformName { get; }

        /// <summary>
        /// Project configuration the solution configuration maps the project to (<c>Debug</c>); null when unknown.
        /// </summary>
        public string ProjectConfigurationName { get; }

        /// <summary>
        /// Project platform (<c>Any CPU</c>, <c>x64</c>); null when unknown.
        /// </summary>
        public string ProjectPlatformName { get; }

        /// <summary>
        /// Raw <c>OutputPath</c> of the project configuration: relative to the project directory or absolute.
        /// </summary>
        public string OutputPath { get; }

        /// <summary>
        /// Why the project configuration could not be read; null when it was read or when no project is involved.
        /// </summary>
        public string ResolutionError { get; }

        /// <summary>
        /// True when the project configuration is known.
        /// </summary>
        public bool IsProjectConfigurationResolved => !string.IsNullOrEmpty(ProjectConfigurationName);

        /// <summary>
        /// <c>Name|Platform</c> of the active solution configuration: the key <c>SolutionBuild.BuildProject</c> expects.
        /// </summary>
        public string SolutionConfigurationKey
        {
            get
            {
                if (string.IsNullOrEmpty(SolutionConfigurationName))
                    return null;

                return string.IsNullOrEmpty(SolutionPlatformName)
                    ? SolutionConfigurationName
                    : SolutionConfigurationName + "|" + SolutionPlatformName;
            }
        }
    }

    /// <summary>
    /// Reads <see cref="ProjectBuildConfiguration"/> straight from DTE. Every member must be called on the UI thread.
    /// </summary>
    internal static class ProjectConfigurationReader
    {
        /// <summary>
        /// Configuration the active solution configuration builds <paramref name="project"/> in, plus the project's
        /// <c>OutputPath</c>. Pass a null project to read only the solution configuration.
        /// </summary>
        public static ProjectBuildConfiguration ReadActive(DTE2 dte, Project project)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (dte == null)
                return ProjectBuildConfiguration.Empty;

            SolutionConfiguration activeConfiguration = null;
            try
            {
                activeConfiguration = dte.Solution?.SolutionBuild?.ActiveConfiguration;
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"ProjectConfigurationReader: cannot read the active solution configuration: {ex.Message}");
            }

            if (activeConfiguration == null || string.IsNullOrEmpty(activeConfiguration.Name))
                return ProjectBuildConfiguration.Empty;

            string solutionPlatformName = null;
            if (activeConfiguration is SolutionConfiguration2 activeConfiguration2)
                solutionPlatformName = activeConfiguration2.PlatformName;

            string projectConfigurationName = null;
            string projectPlatformName = null;
            string outputPath = null;
            string resolutionError = null;

            if (project != null)
            {
                SolutionContext context = FindSolutionContext(activeConfiguration, project);
                if (context != null)
                {
                    projectConfigurationName = context.ConfigurationName;
                    projectPlatformName = context.PlatformName;
                }

                if (string.IsNullOrEmpty(projectConfigurationName))
                {
                    ReadActiveProjectConfiguration(project, out projectConfigurationName, out projectPlatformName);
                }

                outputPath = TryReadOutputPath(project, projectConfigurationName, projectPlatformName);

                if (string.IsNullOrEmpty(projectConfigurationName))
                    resolutionError = "the project is not mapped in the active solution configuration and has no active configuration of its own";
            }

            return new ProjectBuildConfiguration(
                activeConfiguration.Name,
                solutionPlatformName,
                projectConfigurationName,
                projectPlatformName,
                outputPath,
                resolutionError);
        }

        /// <summary>
        /// Solution context of the project in the given solution configuration: the solution configuration → project
        /// configuration mapping.
        /// </summary>
        private static SolutionContext FindSolutionContext(SolutionConfiguration activeConfiguration, Project project)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            string projectName = ProjectFinder.SafeName(project, ProjectField.Name);
            string projectUniqueName = ProjectFinder.SafeName(project, ProjectField.UniqueName);
            if (string.IsNullOrEmpty(projectName) && string.IsNullOrEmpty(projectUniqueName))
                return null;

            string normalizedName = ProjectFinder.NormalizeProjectName(projectName);
            string normalizedUniqueName = ProjectFinder.NormalizeProjectName(projectUniqueName);

            try
            {
                SolutionContexts contexts = activeConfiguration.SolutionContexts;
                if (contexts == null)
                    return null;

                int count = contexts.Count;
                for (int i = 1; i <= count; i++)
                {
                    SolutionContext context = null;
                    try
                    {
                        context = contexts.Item(i);
                    }
                    catch (Exception ex)
                    {
                        InternalLogger.Debug($"ProjectConfigurationReader: cannot read solution context {i}: {ex.Message}");
                        continue;
                    }

                    if (context == null)
                        continue;

                    string contextProjectName;
                    try
                    {
                        contextProjectName = context.ProjectName;
                    }
                    catch (Exception ex)
                    {
                        InternalLogger.Debug($"ProjectConfigurationReader: cannot read the project name of solution context {i}: {ex.Message}");
                        continue;
                    }

                    if (ProjectFinder.IsNameMatch(contextProjectName, normalizedUniqueName) ||
                        ProjectFinder.IsNameMatch(contextProjectName, normalizedName))
                    {
                        return context;
                    }
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"ProjectConfigurationReader: cannot read the solution contexts: {ex.Message}");
            }

            return null;
        }

        private static void ReadActiveProjectConfiguration(Project project, out string configurationName, out string platformName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            configurationName = null;
            platformName = null;

            try
            {
                Configuration activeConfiguration = project.ConfigurationManager?.ActiveConfiguration;
                if (activeConfiguration == null)
                    return;

                configurationName = activeConfiguration.ConfigurationName;
                platformName = activeConfiguration.PlatformName;
            }
            catch (Exception ex)
            {
                InternalLogger.Debug($"ProjectConfigurationReader: cannot read the project's active configuration: {ex.Message}");
            }
        }

        /// <summary>
        /// Raw <c>OutputPath</c> of the project configuration — the only trustworthy source of the build output path.
        /// </summary>
        private static string TryReadOutputPath(Project project, string configurationName, string platformName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                ConfigurationManager configurationManager = project.ConfigurationManager;
                if (configurationManager == null)
                    return null;

                Configuration configuration = null;
                if (!string.IsNullOrEmpty(configurationName))
                {
                    try
                    {
                        configuration = configurationManager.Item(configurationName, platformName ?? string.Empty);
                    }
                    catch (Exception ex)
                    {
                        InternalLogger.Debug($"ProjectConfigurationReader: no project configuration '{configurationName}|{platformName}': {ex.Message}");
                    }
                }

                if (configuration == null)
                    configuration = configurationManager.ActiveConfiguration;

                if (configuration?.Properties == null)
                    return null;

                Property property = configuration.Properties.Item("OutputPath");
                string value = Convert.ToString(property?.Value, CultureInfo.InvariantCulture);
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
            catch (Exception ex)
            {
                InternalLogger.Warn($"ProjectConfigurationReader: cannot read 'OutputPath' of the project: {ex.Message}");
                return null;
            }
        }
    }
}
