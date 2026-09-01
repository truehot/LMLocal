using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot.Infrastructure
{
    /// <summary>
    /// Creates <see cref="SnapshotPaths"/> for the currently open solution.
    /// Paths are derived from <see cref="ISettingsManager"/> settings and a hash
    /// of the solution directory, guaranteeing stable isolation per solution.
    /// </summary>
    internal interface ISnapshotPathsFactory
    {
        SnapshotPaths Create();
        string ComputeSolutionHash();
    }

    internal sealed class SnapshotPathsFactory : ISnapshotPathsFactory
    {
        private readonly IVsDependencies _vsDependencies;
        private readonly ISettingsManager _settingsManager;

        public SnapshotPathsFactory(IVsDependencies vsDependencies, ISettingsManager settingsManager)
        {
            _vsDependencies = vsDependencies ?? throw new ArgumentNullException(nameof(vsDependencies));
            _settingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
        }

        public SnapshotPaths Create()
        {
            if (!_vsDependencies.IsSolutionOpen)
                return null;

            string solutionHash = ComputeSolutionHash();
            if (string.IsNullOrEmpty(solutionHash))
                return null;

            string appDataFolder = _settingsManager.LocalAppDataFolder ?? "LMLocal";
            string snapshotFolder = _settingsManager.SnapshotFolder ?? "Snapshots";
            string manifestFileName = _settingsManager.LocalSnapshotsFileName ?? "manifest.json";

            string snapshotDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                appDataFolder,
                snapshotFolder,
                solutionHash);

            string filesDirectory = Path.Combine(snapshotDir, "files");
            string tmpDirectory = Path.Combine(snapshotDir, "tmp");
            string manifestPath = Path.Combine(snapshotDir, manifestFileName);

            return new SnapshotPaths(snapshotDir, filesDirectory, tmpDirectory, manifestPath);
        }

        public string ComputeSolutionHash()
        {
            if (!_vsDependencies.IsSolutionOpen)
                return string.Empty;

            using (var sha = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(_vsDependencies.GetSolutionDirectory());
                byte[] hash = sha.ComputeHash(bytes);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
