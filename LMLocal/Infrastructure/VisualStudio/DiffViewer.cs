using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace LMLocal.Infrastructure.VisualStudio
{
    public static class DiffViewer
    {
        private static readonly Dictionary<Tuple<string, string>, IVsWindowFrame> _openDiffs =
            new Dictionary<Tuple<string, string>, IVsWindowFrame>();

        /// <summary>
        /// Displays a diff comparison between two files using Visual Studio's built-in diff tool.
        /// </summary>
        public static async Task ShowDiffAsync(string originalPath, string modifiedPath, string tempRoot)
        {
            if (string.IsNullOrEmpty(originalPath) && string.IsNullOrEmpty(modifiedPath))
                return;

            if (string.IsNullOrEmpty(tempRoot))
                throw new ArgumentException("Temporary directory path cannot be null or empty.", nameof(tempRoot));

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var key = Tuple.Create(
                string.IsNullOrEmpty(originalPath) ? null : originalPath,
                string.IsNullOrEmpty(modifiedPath) ? null : modifiedPath
            );

            if (_openDiffs.TryGetValue(key, out var existingFrame))
            {
                if (IsFrameAlive(existingFrame))
                {
                    int hr = existingFrame.Show();
                    if (hr == 0)
                        return;
                }
                _openDiffs.Remove(key);
            }

            Directory.CreateDirectory(tempRoot);

            string left = string.IsNullOrEmpty(originalPath)
                ? GetTempFilePath(originalPath, "original", tempRoot)
                : originalPath;
            string right = string.IsNullOrEmpty(modifiedPath)
                ? GetTempFilePath(modifiedPath, "modified", tempRoot)
                : modifiedPath;


            uint optionsFlags = 0;
            if (string.IsNullOrEmpty(originalPath))
                optionsFlags |= (uint)__VSDIFFSERVICEOPTIONS.VSDIFFOPT_LeftFileIsTemporary;
            if (string.IsNullOrEmpty(modifiedPath))
                optionsFlags |= (uint)__VSDIFFSERVICEOPTIONS.VSDIFFOPT_RightFileIsTemporary;

            var diffService = (IVsDifferenceService)Package.GetGlobalService(typeof(SVsDifferenceService))
                ?? throw new InvalidOperationException("IVsDifferenceService not available");

            var compareCaption = "Compare: " + Path.GetFileName(modifiedPath ?? originalPath ?? "unknown");


            var frame = diffService.OpenComparisonWindow2(
                left, right,
                compareCaption, "Original", "Modified",
                null, null,
                null,
                optionsFlags
            );

            if (frame != null)
            {
                _openDiffs[key] = frame;
            }
            else
            {
                if (string.IsNullOrEmpty(originalPath) && File.Exists(left))
                    File.Delete(left);
                if (string.IsNullOrEmpty(modifiedPath) && File.Exists(right))
                    File.Delete(right);
            }
        }

        /// <summary>
        /// Gets a temporary file path for the specified original path and side.
        /// </summary>
        private static string GetTempFilePath(string originalPath, string side, string root)
        {
            string source = string.IsNullOrEmpty(originalPath) ? "empty" : originalPath;
            string hash = ComputeHash(source);
            string fileName = $"{side}_{hash}.tmp";
            string fullPath = Path.Combine(root, fileName);

            if (!File.Exists(fullPath))
            {
                using (File.Create(fullPath)) { }
            }

            return fullPath;
        }

        /// <summary>
        /// Computes the first 8 characters of the SHA1 hash of the input string as a hexadecimal string.
        /// </summary>
        private static string ComputeHash(string input)
        {
            using (var sha1 = SHA1.Create())
            {
                byte[] bytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(input));
                return BitConverter.ToString(bytes).Replace("-", "").Substring(0, 8);
            }
        }

        /// <summary>
        /// Determines whether the specified window frame is still alive (not closed).
        /// </summary>
        private static bool IsFrameAlive(IVsWindowFrame frame)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                frame.GetProperty((int)__VSFPROPID.VSFPROPID_Caption, out _);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
