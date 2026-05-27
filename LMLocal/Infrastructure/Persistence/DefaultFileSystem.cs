using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LMLocal.Infrastructure
{
    /// <summary>
    /// Default filesystem implementation that delegates to the .NET
    /// `System.IO` APIs. This implementation performs real disk I/O and
    /// is intended for production usage. Tests should inject a custom
    /// `IFileSystem` (in-memory or mock) when needed.
    /// </summary>
    public interface IFileSystem
    {
        void CreateDirectory(string path);
        bool FileExists(string path);
        string ReadAllText(string path);
        Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default);
        Task WriteAllBytesAsync(string path, byte[] data, CancellationToken cancellationToken = default);
        Task AppendAllBytesAsync(string path, byte[] data, CancellationToken cancellationToken = default);
        void EnsureDirectoryExistsForFile(string filePath);
        void ValidateFilePath(string filePath);
        void Replace(string sourceFileName, string destinationFileName);
        void Move(string sourceFileName, string destinationFileName);
        void Delete(string path);
    }

    internal class DefaultFileSystem : IFileSystem
    {
        public void CreateDirectory(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            Directory.CreateDirectory(path);
        }

        public bool FileExists(string path)
        {
            return File.Exists(path);
        }

        public string ReadAllText(string path)
        {
            return File.ReadAllText(path);
        }

        public async Task<string> ReadAllTextAsync(string path, System.Threading.CancellationToken cancellationToken = default)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true))
            {
                var sb = new StringBuilder();
                var buffer = new byte[4096];
                int read;
                while ((read = await fs.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, read));
                }
                return sb.ToString();
            }
        }

        public async Task WriteAllBytesAsync(string path, byte[] data, System.Threading.CancellationToken cancellationToken = default)
        {
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await fs.WriteAsync(data, 0, data.Length, cancellationToken).ConfigureAwait(false);
                await fs.FlushAsync().ConfigureAwait(false);
            }
        }

        public async Task AppendAllBytesAsync(string path, byte[] data, System.Threading.CancellationToken cancellationToken = default)
        {
            using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await fs.WriteAsync(data, 0, data.Length, cancellationToken).ConfigureAwait(false);
                await fs.FlushAsync().ConfigureAwait(false);
            }
        }

        public void Replace(string sourceFileName, string destinationFileName)
        {
            File.Replace(sourceFileName, destinationFileName, null);
        }

        public void Move(string sourceFileName, string destinationFileName)
        {
            File.Move(sourceFileName, destinationFileName);
        }

        public void Delete(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }

        public void ValidateFilePath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentNullException(nameof(filePath));

            char[] invalidPath = Path.GetInvalidPathChars();
            foreach (var c in invalidPath)
            {
                if (filePath.IndexOf(c) >= 0) throw new ArgumentException("File path contains invalid characters.", nameof(filePath));
            }

            string fileName = Path.GetFileName(filePath);
            if (string.IsNullOrEmpty(fileName)) throw new ArgumentException("File path must contain a file name.", nameof(filePath));
            char[] invalidFile = Path.GetInvalidFileNameChars();
            foreach (var c in invalidFile)
            {
                if (fileName.IndexOf(c) >= 0) throw new ArgumentException("File name contains invalid characters.", nameof(filePath));
            }
        }

        public void EnsureDirectoryExistsForFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;

            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        }
    }
}
