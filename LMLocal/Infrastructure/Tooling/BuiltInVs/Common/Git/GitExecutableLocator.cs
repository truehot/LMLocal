using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using LMLocal.Core.Common;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Git
{
    /// <summary>
    /// Finds git.exe once per VS session: PATH first (never the current or a relative directory),
    /// then the git shipped with Visual Studio. The version is cached together with the path.
    /// </summary>
    internal sealed class GitExecutableLocator
    {
        private static readonly Version MinimumVersion = new Version(2, 24);
        private static readonly Regex LeadingVersion = new Regex(@"^\s*(\d+)\.(\d+)(?:\.(\d+))?", RegexOptions.CultureInvariant);

        private static readonly Lazy<GitExecutableLocator> SharedInstance =
            new Lazy<GitExecutableLocator>(Create, LazyThreadSafetyMode.ExecutionAndPublication);

        public static GitExecutableLocator Shared => SharedInstance.Value;

        private readonly string _path;
        private readonly Version _version;

        internal GitExecutableLocator(string path, Version version)
        {
            _path = path;
            _version = version;
        }

        public string ExecutablePath => _path;
        public Version Version => _version;

        public bool TryGet(out string path, out string error)
        {
            path = _path;
            error = null;

            if (string.IsNullOrEmpty(_path) || !File.Exists(_path))
            {
                error = "git.exe was not found in PATH or in Visual Studio. Install Git for Windows and try again.";
                return false;
            }

            if (_version == null || _version < MinimumVersion)
            {
                error = $"git {MinimumVersion} or newer is required (found: {(_version == null ? "unknown version" : _version.ToString())}).";
                return false;
            }

            return true;
        }

        /// <summary>Leading numeric part of a git file version: "2.53.0.windows.3" → 2.53.0; null when there is none.</summary>
        internal static Version ParseVersion(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            Match match = LeadingVersion.Match(text);
            if (!match.Success)
                return null;

            int major = int.Parse(match.Groups[1].Value);
            int minor = int.Parse(match.Groups[2].Value);
            int build = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;
            return new Version(major, minor, build);
        }

        private static GitExecutableLocator Create()
        {
            string path = Locate();
            return new GitExecutableLocator(path, ReadVersion(path));
        }

        private static string Locate()
        {
            string fromPath = LMLocal.Infrastructure.Tooling.ExecutablePath.FindInPath("git.exe");
            if (fromPath != null)
                return fromPath;

            string vsGit = Path.Combine("CommonExtensions", "Microsoft", "TeamFoundation", "Team Explorer", "Git", "cmd", "git.exe");
            var bases = new List<string>();
            try
            {
                string devenv = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(devenv))
                    bases.Add(Path.GetDirectoryName(devenv));
            }
            catch (Exception ex) { InternalLogger.Debug($"git_read: devenv path is unavailable: {ex.Message}"); }

            bases.Add(AppDomain.CurrentDomain.BaseDirectory);

            foreach (string baseDir in bases)
            {
                if (string.IsNullOrEmpty(baseDir))
                    continue;

                string candidate = Path.Combine(baseDir, vsGit);
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        private static Version ReadVersion(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return null;

            try
            {
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
                return ParseVersion(info.FileVersion) ?? ParseVersion(info.ProductVersion);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
