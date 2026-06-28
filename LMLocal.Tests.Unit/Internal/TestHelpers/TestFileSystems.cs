using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Persistence;

namespace LMLocal.Tests.Unit.Infrastructure
{
    public class InMemoryFileSystem : IFileSystem
    {
        private readonly ConcurrentDictionary<string, byte[]> _files = new ConcurrentDictionary<string, byte[]>();

        public void CreateDirectory(string path) { }
        public bool FileExists(string path) => _files.ContainsKey(Normalize(path));
        public (long Length, DateTime LastWriteTimeUtc) GetFileInfo(string path) => (ReadAllText(path).Length, DateTime.MinValue);
        public string ReadAllText(string path)
        {
            var key = Normalize(path);
            if (!_files.TryGetValue(key, out var bytes)) throw new System.IO.FileNotFoundException();
            return Encoding.UTF8.GetString(bytes);
        }
        public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ReadAllText(path));
        }
        public Task WriteAllBytesAsync(string path, byte[] data, CancellationToken cancellationToken = default)
        {
            _files[Normalize(path)] = data;
            return Task.CompletedTask;
        }
        public Task AppendAllBytesAsync(string path, byte[] data, CancellationToken cancellationToken = default)
        {
            var key = Normalize(path);
            if (_files.TryGetValue(key, out var existing))
            {
                var combined = new byte[existing.Length + data.Length];
                Array.Copy(existing, combined, existing.Length);
                Array.Copy(data, 0, combined, existing.Length, data.Length);
                _files[key] = combined;
            }
            else
            {
                _files[key] = data;
            }
            return Task.CompletedTask;
        }
        public void Replace(string sourceFileName, string destinationFileName)
        {
            var src = Normalize(sourceFileName);
            var dst = Normalize(destinationFileName);
            if (!_files.ContainsKey(src)) throw new System.IO.FileNotFoundException();
            _files[dst] = _files[src];
            _files.TryRemove(src, out _);
        }
        public void Move(string sourceFileName, string destinationFileName)
        {
            var src = Normalize(sourceFileName);
            var dst = Normalize(destinationFileName);
            if (!_files.TryRemove(src, out var data)) throw new System.IO.FileNotFoundException();
            _files[dst] = data;
        }
        public void Delete(string path) => _files.TryRemove(Normalize(path), out _);

        public System.Collections.Generic.IEnumerable<string> GetAllFiles() => _files.Keys;

        private string Normalize(string path) => path?.Replace("\\", "/").ToLowerInvariant();

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
        }

        public async Task CopyFileAsync(string sourcePath, string destPath, CancellationToken cancellationToken)
        {
            var key = Normalize(sourcePath);
            if (!_files.TryGetValue(key, out var data)) throw new System.IO.FileNotFoundException();
            _files[Normalize(destPath)] = data;
            await Task.CompletedTask;
        }

        public Task<string> ReadAllTextWithSharedReadAsync(string path, CancellationToken cancellationToken = default)
        {
            return ReadAllTextAsync(path, cancellationToken);
        }

        public Task<System.Collections.Generic.List<string>> ReadLinesRangeAsync(string path, int startLine, int endLine, CancellationToken cancellationToken = default)
        {
            if (startLine < 1) throw new ArgumentOutOfRangeException(nameof(startLine), "startLine must be >= 1");
            if (endLine < startLine) throw new ArgumentOutOfRangeException(nameof(endLine), "endLine must be >= startLine");

            var text = ReadAllText(path);
            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, System.StringSplitOptions.None);
            var result = new System.Collections.Generic.List<string>();
            for (int i = startLine - 1; i < endLine && i < lines.Length; i++)
            {
                result.Add(lines[i]);
            }
            return Task.FromResult(result);
        }

        public Task ReadLinesAsync(string path, Action<int, string> lineHandler, CancellationToken cancellationToken = default)
        {
            if (lineHandler == null) throw new ArgumentNullException(nameof(lineHandler));
            var text = ReadAllText(path);
            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, System.StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++)
            {
                lineHandler(i + 1, lines[i]);
            }
            return Task.CompletedTask;
        }

        public void ReplaceOrCreate(string sourceFileName, string destinationFileName)
        {
        }

        public string[] GetFiles(string path, string searchPattern)
        {
            string ext = searchPattern.StartsWith("*.") ? searchPattern.Substring(1) : searchPattern;
            return _files.Keys.Where(k => k.EndsWith(ext, StringComparison.OrdinalIgnoreCase)).ToArray();
        }

        public string GetFileExtension(string filePath)
        {
            throw new NotImplementedException();
        }

        public bool DirectoryExists(string path)
        {
            throw new NotImplementedException();
        }

        public Task<List<FileSystemEntry>> EnumerateDirectoryAsync(string path, HashSet<string> excludedDirectoryNames, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }

    public class CancelableFileSystem : IFileSystem
    {
        private readonly ConcurrentDictionary<string, byte[]> _files = new ConcurrentDictionary<string, byte[]>();
        public void CreateDirectory(string path) { }
        public bool FileExists(string path) => _files.ContainsKey(N(path));
        public (long Length, DateTime LastWriteTimeUtc) GetFileInfo(string path) => (_files[N(path)].Length, DateTime.MinValue);
        public string ReadAllText(string path) => Encoding.UTF8.GetString(_files[N(path)]);
        public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(ReadAllText(path));
        public async Task WriteAllBytesAsync(string path, byte[] data, CancellationToken cancellationToken = default)
        {
            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            _files[N(path)] = data;
        }
        public async Task AppendAllBytesAsync(string path, byte[] data, CancellationToken cancellationToken = default)
        {
            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            var key = N(path);
            if (_files.TryGetValue(key, out var existing))
            {
                var combined = new byte[existing.Length + data.Length];
                Array.Copy(existing, combined, existing.Length);
                Array.Copy(data, 0, combined, existing.Length, data.Length);
                _files[key] = combined;
            }
            else
            {
                _files[key] = data;
            }
        }
        public void Replace(string sourceFileName, string destinationFileName) { Move(sourceFileName, destinationFileName); }
        public void Move(string sourceFileName, string destinationFileName)
        {
            var s = N(sourceFileName);
            var d = N(destinationFileName);
            if (!_files.TryRemove(s, out var data)) throw new System.IO.FileNotFoundException();
            _files[d] = data;
        }
        public void Delete(string path) => _files.TryRemove(N(path), out _);
        private string N(string p) => p?.Replace('\\','/').ToLowerInvariant();

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
        }

        public async Task CopyFileAsync(string sourcePath, string destPath, CancellationToken cancellationToken)
        {
            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            var key = N(sourcePath);
            if (!_files.TryGetValue(key, out var data)) throw new System.IO.FileNotFoundException();
            _files[N(destPath)] = data;
        }

        public Task<string> ReadAllTextWithSharedReadAsync(string path, CancellationToken cancellationToken = default)
        {
            return ReadAllTextAsync(path, cancellationToken);
        }

        public async Task<System.Collections.Generic.List<string>> ReadLinesRangeAsync(string path, int startLine, int endLine, CancellationToken cancellationToken = default)
        {
            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            if (startLine < 1) throw new ArgumentOutOfRangeException(nameof(startLine), "startLine must be >= 1");
            if (endLine < startLine) throw new ArgumentOutOfRangeException(nameof(endLine), "endLine must be >= startLine");

            var text = ReadAllText(path);
            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, System.StringSplitOptions.None);
            var result = new System.Collections.Generic.List<string>();
            for (int i = startLine - 1; i < endLine && i < lines.Length; i++)
            {
                result.Add(lines[i]);
            }
            return result;
        }

        public async Task ReadLinesAsync(string path, Action<int, string> lineHandler, CancellationToken cancellationToken = default)
        {
            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            if (lineHandler == null) throw new ArgumentNullException(nameof(lineHandler));
            var text = ReadAllText(path);
            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, System.StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lineHandler(i + 1, lines[i]);
            }
        }

        public void ReplaceOrCreate(string sourceFileName, string destinationFileName)
        {
        }

        public string[] GetFiles(string path, string searchPattern)
        {
            string ext = searchPattern.StartsWith("*.") ? searchPattern.Substring(1) : searchPattern;
            return _files.Keys.Where(k => k.EndsWith(ext, StringComparison.OrdinalIgnoreCase)).ToArray();
        }

        public string GetFileExtension(string filePath)
        {
            throw new NotImplementedException();
        }

        public bool DirectoryExists(string path)
        {
            throw new NotImplementedException();
        }

        public Task<List<FileSystemEntry>> EnumerateDirectoryAsync(string path, HashSet<string> excludedDirectoryNames, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }

    public class FailingMoveFileSystem : IFileSystem
    {
        private readonly ConcurrentDictionary<string, byte[]> _d = new ConcurrentDictionary<string, byte[]>();
        private readonly string _failOnMoveSrc;

        public FailingMoveFileSystem(string failOnMoveSrc = null)
        {
            _failOnMoveSrc = Normalize(failOnMoveSrc);
        }

        public void CreateDirectory(string path) { }
        public bool FileExists(string path) => _d.ContainsKey(N(path));
        public (long Length, DateTime LastWriteTimeUtc) GetFileInfo(string path) => (_d[N(path)].Length, DateTime.MinValue);
        public string ReadAllText(string path) => Encoding.UTF8.GetString(_d[N(path)]);
        public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(ReadAllText(path));
        public Task WriteAllBytesAsync(string path, byte[] data, CancellationToken cancellationToken = default)
        {
            _d[N(path)] = data;
            return Task.CompletedTask;
        }
        public Task AppendAllBytesAsync(string path, byte[] data, CancellationToken cancellationToken = default)
        {
            var key = N(path);
            if (_d.TryGetValue(key, out var existing))
            {
                var combined = new byte[existing.Length + data.Length];
                Array.Copy(existing, combined, existing.Length);
                Array.Copy(data, 0, combined, existing.Length, data.Length);
                _d[key] = combined;
            }
            else
            {
                _d[key] = data;
            }
            return Task.CompletedTask;
        }
        public void Replace(string sourceFileName, string destinationFileName)
        {
            Move(sourceFileName, destinationFileName);
        }
        public void Move(string sourceFileName, string destinationFileName)
        {
            var src = Normalize(sourceFileName);
            var dst = Normalize(destinationFileName);
            if (src == _failOnMoveSrc)
                throw new InvalidOperationException("Simulated move failure");
            if (!_d.TryRemove(src, out var data)) throw new System.IO.FileNotFoundException();
            _d[dst] = data;
        }
        public void Delete(string path) => _d.TryRemove(N(path), out _);
        private static string Normalize(string p) => p?.Replace('\\','/').ToLowerInvariant();
        private string N(string p) => Normalize(p);

        // helper to seed file
        public void Seed(string path, string content) => _d[Normalize(path)] = Encoding.UTF8.GetBytes(content);

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
        }

        public async Task CopyFileAsync(string sourcePath, string destPath, CancellationToken cancellationToken)
        {
            var src = Normalize(sourcePath);
            var dst = Normalize(destPath);
            if (!_d.TryGetValue(src, out var data)) throw new System.IO.FileNotFoundException();
            _d[dst] = data;
            await Task.CompletedTask;
        }

        public Task<string> ReadAllTextWithSharedReadAsync(string path, CancellationToken cancellationToken = default)
        {
            return ReadAllTextAsync(path, cancellationToken);
        }

        public Task<System.Collections.Generic.List<string>> ReadLinesRangeAsync(string path, int startLine, int endLine, CancellationToken cancellationToken = default)
        {
            if (startLine < 1) throw new ArgumentOutOfRangeException(nameof(startLine), "startLine must be >= 1");
            if (endLine < startLine) throw new ArgumentOutOfRangeException(nameof(endLine), "endLine must be >= startLine");

            var text = ReadAllText(path);
            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, System.StringSplitOptions.None);
            var result = new System.Collections.Generic.List<string>();
            for (int i = startLine - 1; i < endLine && i < lines.Length; i++)
            {
                result.Add(lines[i]);
            }
            return Task.FromResult(result);
        }

        public Task ReadLinesAsync(string path, Action<int, string> lineHandler, CancellationToken cancellationToken = default)
        {
            if (lineHandler == null) throw new ArgumentNullException(nameof(lineHandler));
            var text = ReadAllText(path);
            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, System.StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++)
            {
                lineHandler(i + 1, lines[i]);
            }
            return Task.CompletedTask;
        }

        public void ReplaceOrCreate(string sourceFileName, string destinationFileName)
        {
        }

        public string[] GetFiles(string path, string searchPattern)
        {
            string ext = searchPattern.StartsWith("*.") ? searchPattern.Substring(1) : searchPattern;
            return _d.Keys.Where(k => k.EndsWith(ext, StringComparison.OrdinalIgnoreCase)).ToArray();
        }

        public string GetFileExtension(string filePath)
        {
            throw new NotImplementedException();
        }

        public bool DirectoryExists(string path)
        {
            throw new NotImplementedException();
        }

        public Task<List<FileSystemEntry>> EnumerateDirectoryAsync(string path, HashSet<string> excludedDirectoryNames, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }

    public class DelayedWriteFileSystem : IFileSystem
    {
        private readonly ConcurrentDictionary<string, byte[]> _files = new ConcurrentDictionary<string, byte[]>();
        private readonly TaskCompletionSource<bool> _allowWrite = new TaskCompletionSource<bool>();

        public void AllowWrite() => _allowWrite.TrySetResult(true);

        public void CreateDirectory(string path) { }
        public bool FileExists(string path) => _files.ContainsKey(N(path));
        public (long Length, DateTime LastWriteTimeUtc) GetFileInfo(string path) => (_files[N(path)].Length, DateTime.MinValue);
        public string ReadAllText(string path) => Encoding.UTF8.GetString(_files[N(path)]);
        public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(ReadAllText(path));
        public async Task WriteAllBytesAsync(string path, byte[] data, CancellationToken cancellationToken = default)
        {
            await _allowWrite.Task.ConfigureAwait(false);
            _files[N(path)] = data;
        }
        public async Task AppendAllBytesAsync(string path, byte[] data, CancellationToken cancellationToken = default)
        {
            await _allowWrite.Task.ConfigureAwait(false);
            var key = N(path);
            if (_files.TryGetValue(key, out var existing))
            {
                var combined = new byte[existing.Length + data.Length];
                Array.Copy(existing, combined, existing.Length);
                Array.Copy(data, 0, combined, existing.Length, data.Length);
                _files[key] = combined;
            }
            else
            {
                _files[key] = data;
            }
        }
        public void Replace(string sourceFileName, string destinationFileName) { Move(sourceFileName, destinationFileName); }
        public void Move(string sourceFileName, string destinationFileName)
        {
            var s = N(sourceFileName);
            var d = N(destinationFileName);
            if (!_files.TryRemove(s, out var data)) throw new System.IO.FileNotFoundException();
            _files[d] = data;
        }
        public void Delete(string path) => _files.TryRemove(N(path), out _);
        private string N(string p) => p?.Replace('\\','/').ToLowerInvariant();

        // helper to seed file
        public void Seed(string path, string content) => _files[N(path)] = Encoding.UTF8.GetBytes(content);

        public void EnsureDirectoryExistsForFile(string filePath)
        {
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

        public async Task CopyFileAsync(string sourcePath, string destPath, CancellationToken cancellationToken)
        {
            await _allowWrite.Task.ConfigureAwait(false);
            var key = N(sourcePath);
            if (!_files.TryGetValue(key, out var data)) throw new System.IO.FileNotFoundException();
            _files[N(destPath)] = data;
        }

        public Task<string> ReadAllTextWithSharedReadAsync(string path, CancellationToken cancellationToken = default)
        {
            return ReadAllTextAsync(path, cancellationToken);
        }

        public async Task<System.Collections.Generic.List<string>> ReadLinesRangeAsync(string path, int startLine, int endLine, CancellationToken cancellationToken = default)
        {
            await _allowWrite.Task.ConfigureAwait(false);
            if (startLine < 1) throw new ArgumentOutOfRangeException(nameof(startLine), "startLine must be >= 1");
            if (endLine < startLine) throw new ArgumentOutOfRangeException(nameof(endLine), "endLine must be >= startLine");

            var text = ReadAllText(path);
            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, System.StringSplitOptions.None);
            var result = new System.Collections.Generic.List<string>();
            for (int i = startLine - 1; i < endLine && i < lines.Length; i++)
            {
                result.Add(lines[i]);
            }
            return result;
        }

        public async Task ReadLinesAsync(string path, Action<int, string> lineHandler, CancellationToken cancellationToken = default)
        {
            await _allowWrite.Task.ConfigureAwait(false);
            if (lineHandler == null) throw new ArgumentNullException(nameof(lineHandler));
            var text = ReadAllText(path);
            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, System.StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                lineHandler(i + 1, lines[i]);
            }
        }

        public void ReplaceOrCreate(string sourceFileName, string destinationFileName)
        {
        }

        public string[] GetFiles(string path, string searchPattern)
        {
            string ext = searchPattern.StartsWith("*.") ? searchPattern.Substring(1) : searchPattern;
            return _files.Keys.Where(k => k.EndsWith(ext, StringComparison.OrdinalIgnoreCase)).ToArray();
        }

        public string GetFileExtension(string filePath)
        {
            throw new NotImplementedException();
        }

        public bool DirectoryExists(string path)
        {
            throw new NotImplementedException();
        }

        public Task<List<FileSystemEntry>> EnumerateDirectoryAsync(string path, HashSet<string> excludedDirectoryNames, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }

    public class InMemoryTestFileSystem : IFileSystem
    {
        private readonly ConcurrentDictionary<string, byte[]> _files = new ConcurrentDictionary<string, byte[]>();
        public void CreateDirectory(string path) { }
        public bool FileExists(string path) => _files.ContainsKey(N(path));
        public (long Length, DateTime LastWriteTimeUtc) GetFileInfo(string path) => (_files[N(path)].Length, DateTime.MinValue);
        public string ReadAllText(string path) => Encoding.UTF8.GetString(_files[N(path)]);
        public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(ReadAllText(path));
        public Task WriteAllBytesAsync(string path, byte[] data, CancellationToken cancellationToken = default)
        {
            _files[N(path)] = data;
            return Task.CompletedTask;
        }
        public Task AppendAllBytesAsync(string path, byte[] data, CancellationToken cancellationToken = default)
        {
            var key = N(path);
            if (_files.TryGetValue(key, out var existing))
            {
                var combined = new byte[existing.Length + data.Length];
                Array.Copy(existing, combined, existing.Length);
                Array.Copy(data, 0, combined, existing.Length, data.Length);
                _files[key] = combined;
            }
            else
            {
                _files[key] = data;
            }
            return Task.CompletedTask;
        }
        public void Replace(string sourceFileName, string destinationFileName) { Move(sourceFileName, destinationFileName); }
        public void Move(string sourceFileName, string destinationFileName)
        {
            var s = N(sourceFileName);
            var d = N(destinationFileName);
            if (!_files.TryRemove(s, out var data)) throw new System.IO.FileNotFoundException();
            _files[d] = data;
        }
        public void Delete(string path) => _files.TryRemove(N(path), out _);
        private string N(string p) => p?.Replace('\\','/').ToLowerInvariant();
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
        }

        public async Task CopyFileAsync(string sourcePath, string destPath, CancellationToken cancellationToken)
        {
            var key = N(sourcePath);
            if (!_files.TryGetValue(key, out var data)) throw new System.IO.FileNotFoundException();
            _files[N(destPath)] = data;
            await Task.CompletedTask;
        }

        public Task<string> ReadAllTextWithSharedReadAsync(string path, CancellationToken cancellationToken = default)
        {
            return ReadAllTextAsync(path, cancellationToken);
        }

        public Task<System.Collections.Generic.List<string>> ReadLinesRangeAsync(string path, int startLine, int endLine, CancellationToken cancellationToken = default)
        {
            if (startLine < 1) throw new ArgumentOutOfRangeException(nameof(startLine), "startLine must be >= 1");
            if (endLine < startLine) throw new ArgumentOutOfRangeException(nameof(endLine), "endLine must be >= startLine");

            var text = ReadAllText(path);
            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, System.StringSplitOptions.None);
            var result = new System.Collections.Generic.List<string>();
            for (int i = startLine - 1; i < endLine && i < lines.Length; i++)
            {
                result.Add(lines[i]);
            }
            return Task.FromResult(result);
        }

        public Task ReadLinesAsync(string path, Action<int, string> lineHandler, CancellationToken cancellationToken = default)
        {
            if (lineHandler == null) throw new ArgumentNullException(nameof(lineHandler));
            var text = ReadAllText(path);
            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, System.StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++)
            {
                lineHandler(i + 1, lines[i]);
            }
            return Task.CompletedTask;
        }

        public void ReplaceOrCreate(string sourceFileName, string destinationFileName)
        {
        }

        public string[] GetFiles(string path, string searchPattern)
        {
            string ext = searchPattern.StartsWith("*.") ? searchPattern.Substring(1) : searchPattern;
            return _files.Keys.Where(k => k.EndsWith(ext, StringComparison.OrdinalIgnoreCase)).ToArray();
        }

        public string GetFileExtension(string filePath)
        {
            throw new NotImplementedException();
        }

        public bool DirectoryExists(string path)
        {
            throw new NotImplementedException();
        }

        public Task<List<FileSystemEntry>> EnumerateDirectoryAsync(string path, HashSet<string> excludedDirectoryNames, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}
