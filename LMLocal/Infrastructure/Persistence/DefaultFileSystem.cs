using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;
namespace LMLocal.Infrastructure.Persistence
{
    /// <summary>
    /// Default filesystem implementation that delegates to the .NET
    /// </summary>
    public interface IFileSystem
    {
        void CreateDirectory(string path);
        bool FileExists(string path);
        (long Length, DateTime LastWriteTimeUtc) GetFileInfo(string path);
        string ReadAllText(string path);
        Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default);
        Task WriteAllBytesAsync(string path, byte[] data, CancellationToken cancellationToken = default);
        Task AppendAllBytesAsync(string path, byte[] data, CancellationToken cancellationToken = default);
        void EnsureDirectoryExistsForFile(string filePath);
        void ValidateFilePath(string filePath);
        void Replace(string sourceFileName, string destinationFileName);
        void Move(string sourceFileName, string destinationFileName);
        void Delete(string path);
        Task CopyFileAsync(string sourcePath, string destPath, CancellationToken cancellationToken = default);
        Task<string> ReadAllTextWithSharedReadAsync(string path, CancellationToken cancellationToken = default);
        Task<List<string>> ReadLinesRangeAsync(string path, int startLine, int endLine, CancellationToken cancellationToken = default);
        Task ReadLinesAsync(string path, Action<int, string> lineHandler, CancellationToken cancellationToken = default);
        void ReplaceOrCreate(string sourceFileName, string destinationFileName);

        /// <summary>
        /// Returns the names of files (including their paths) that match the specified
        /// search pattern in the given directory.
        /// </summary>
        string[] GetFiles(string path, string searchPattern);
        string GetFileExtension(string filePath);

        /// <summary>
        /// Returns true if the directory exists at the given path.
        /// </summary>
        bool DirectoryExists(string path);

        /// <summary>
        /// Enumerates files and subdirectories in the given directory.
        /// Excluded directories (by name) are filtered out before returning.
        /// Results are ordered: directories first (by name), then files (by name).
        /// </summary>
        Task<List<FileSystemEntry>> EnumerateDirectoryAsync(
            string path,
            HashSet<string> excludedDirectoryNames,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Represents a file or directory entry returned by <see cref="IFileSystem.EnumerateDirectoryAsync"/>.
    /// </summary>
    public class FileSystemEntry
    {
        public string Name { get; set; }
        public string FullPath { get; set; }
        public bool IsDirectory { get; set; }
    }

    internal class DefaultFileSystem : IFileSystem
    {
        private static readonly char[] InvalidPathChars = Path.GetInvalidPathChars();
        private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

        public void CreateDirectory(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            Directory.CreateDirectory(path);
        }

        public bool FileExists(string path)
        {
            return File.Exists(path);
        }

        public (long Length, DateTime LastWriteTimeUtc) GetFileInfo(string path)
        {
            var fi = new FileInfo(path);
            return (fi.Length, fi.LastWriteTimeUtc);
        }

        public string ReadAllText(string path)
        {
            return File.ReadAllText(path);
        }

        public async Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
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

        public async Task<string> ReadAllTextWithSharedReadAsync(string path, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, useAsync: true))
                using (var sr = new StreamReader(fs))
                {
                    var sb = new StringBuilder();
                    char[] buffer = new char[8192];
                    int charsRead;
                    while ((charsRead = await sr.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        sb.Append(buffer, 0, charsRead);
                    }
                    return sb.ToString();
                }
            }
            catch (OperationCanceledException)
            {
                InternalLogger.Warn($"File read operation for '{path}' was canceled.");
                throw;
            }
            catch (Exception ex)
            {
                InternalLogger.Error($"Error reading file '{path}': {ex.Message}");
                return string.Empty;
            }
        }
        public async Task WriteAllBytesAsync(string path, byte[] data, CancellationToken cancellationToken = default)
        {
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await fs.WriteAsync(data, 0, data.Length, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task AppendAllBytesAsync(string path, byte[] data, CancellationToken cancellationToken = default)
        {
            using (var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await fs.WriteAsync(data, 0, data.Length, cancellationToken).ConfigureAwait(false);
            }
        }

        public void Replace(string sourceFileName, string destinationFileName)
        {
            File.Replace(sourceFileName, destinationFileName, null);
        }

        public void ReplaceOrCreate(string sourceFileName, string destinationFileName)
        {
            if (File.Exists(destinationFileName))
            {
                File.Replace(sourceFileName, destinationFileName, null);
            }
            else
            {
                File.Move(sourceFileName, destinationFileName);
            }
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

            if (filePath.IndexOfAny(InvalidPathChars) >= 0)
                throw new ArgumentException("File path contains invalid characters.", nameof(filePath));

            string fileName = Path.GetFileName(filePath);
            if (string.IsNullOrEmpty(fileName))
                throw new ArgumentException("File path must contain a file name.", nameof(filePath));
            if (fileName.IndexOfAny(InvalidFileNameChars) >= 0)
                throw new ArgumentException("File name contains invalid characters.", nameof(filePath));
        }

        public void EnsureDirectoryExistsForFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;

            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        }

        public async Task CopyFileAsync(string sourcePath, string destPath, CancellationToken cancellationToken = default)
        {

            using (var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, useAsync: true))
            using (var destStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, useAsync: true))
            {
                await sourceStream.CopyToAsync(destStream, 8192, cancellationToken).ConfigureAwait(false);
            }
        }

        public async Task ReadLinesAsync(string path, Action<int, string> lineHandler, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
            if (lineHandler == null) throw new ArgumentNullException(nameof(lineHandler));

            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, useAsync: true))
            using (var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            {
                int lineNumber = 0;
                string line;
                while ((line = await sr.ReadLineAsync().ConfigureAwait(false)) != null)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    lineNumber++;
                    lineHandler(lineNumber, line);
                }
            }
        }

        public async Task<List<string>> ReadLinesRangeAsync(string path, int startLine, int endLine, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
            if (startLine < 1) throw new ArgumentOutOfRangeException(nameof(startLine), "startLine must be >= 1");
            if (endLine < startLine) throw new ArgumentOutOfRangeException(nameof(endLine), "endLine must be >= startLine");

            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, useAsync: true))
            using (var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            {
                var result = new List<string>();
                int currentLine = 0;
                string line;
                while ((line = await sr.ReadLineAsync().ConfigureAwait(false)) != null)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    currentLine++;
                    if (currentLine < startLine) continue;
                    if (currentLine > endLine) break;
                    result.Add(line);
                }
                return result;
            }
        }

        public string[] GetFiles(string path, string searchPattern)
        {
            if (string.IsNullOrEmpty(path)) return Array.Empty<string>();
            if (!Directory.Exists(path)) return Array.Empty<string>();
            return Directory.GetFiles(path, searchPattern);
        }

        public string GetFileExtension(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return string.Empty;
            return Path.GetExtension(filePath);
        }

        public bool DirectoryExists(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            return Directory.Exists(path);
        }

        public Task<List<FileSystemEntry>> EnumerateDirectoryAsync(
            string path,
            HashSet<string> excludedDirectoryNames,
            CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                var result = new List<FileSystemEntry>();

                try
                {
                    var dirInfo = new DirectoryInfo(path);
                    if (!dirInfo.Exists)
                        return result;

                    foreach (var dir in dirInfo.EnumerateDirectories()
                        .Where(d => excludedDirectoryNames == null || !excludedDirectoryNames.Contains(d.Name))
                        .OrderBy(d => d.Name))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        result.Add(new FileSystemEntry
                        {
                            Name = dir.Name,
                            FullPath = dir.FullName,
                            IsDirectory = true
                        });
                    }

                    foreach (var file in dirInfo.EnumerateFiles().OrderBy(f => f.Name))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        result.Add(new FileSystemEntry
                        {
                            Name = file.Name,
                            FullPath = file.FullName,
                            IsDirectory = false
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (UnauthorizedAccessException)
                {
                    throw;
                }
                catch (IOException)
                {
                    throw;
                }

                return result;
            }, cancellationToken);
        }
    }
}
