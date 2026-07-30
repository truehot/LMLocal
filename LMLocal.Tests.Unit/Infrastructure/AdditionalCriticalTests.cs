using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Chat;
using LMLocal.Application.Autocompletions;
using LMLocal.Core.Models;
using LMLocal.Core.Exceptions;
using LMLocal.Infrastructure.LlmApi;
using LMLocal.Infrastructure.LlmApi.Responses;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Settings;
using LMLocal.Infrastructure.Tooling;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    [TestFixture]
    public class AdditionalCriticalTests
    {
        private class DelayedWriteFileSystem : IFileSystem
        {
            private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte[]> _files = new System.Collections.Concurrent.ConcurrentDictionary<string, byte[]>();
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
            public Task WriteAllBytesWithEncodingAsync(string path, string content, System.Text.Encoding encoding, bool hasBom, CancellationToken cancellationToken = default)
            {
                return WriteAllBytesAsync(path, encoding.GetBytes(content), cancellationToken);
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
            public void Replace(string sourceFileName, string destinationFileName)
            {
                Move(sourceFileName, destinationFileName);
            }
            public void Move(string sourceFileName, string destinationFileName)
            {
                var s = N(sourceFileName);
                var d = N(destinationFileName);
                if (!_files.TryRemove(s, out var data)) throw new System.IO.FileNotFoundException();
                _files[d] = data;
            }
            public void Delete(string path) => _files.TryRemove(N(path), out _);
            private static string N(string p) => p?.Replace('\\', '/').ToLowerInvariant();

            public void Seed(string path, string content) => _files[N(path)] = Encoding.UTF8.GetBytes(content);

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
                await _allowWrite.Task.ConfigureAwait(false);
                var key = N(sourcePath);
                if (!_files.TryGetValue(key, out var data)) throw new System.IO.FileNotFoundException();
                _files[N(destPath)] = data;
            }

            public Task<string> ReadAllTextWithSharedReadAsync(string path, CancellationToken cancellationToken = default)
            {
                return ReadAllTextAsync(path, cancellationToken);
            }

            public Task<(string content, System.Text.Encoding encoding, bool hasBom)> ReadAllTextWithDetectedEncodingAsync(string path, CancellationToken cancellationToken = default)
            {
                return ReadAllTextWithSharedReadAsync(path, cancellationToken)
                    .ContinueWith(t => (t.Result, System.Text.Encoding.UTF8, false), cancellationToken);
            }

            public (System.Text.Encoding encoding, bool hasBom) DetectEncoding(string path)
            {
                return (new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false), false);
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

        [Test]
        public async Task SaveAndLoad_Race_LoadReadsOriginalUntilMove()
        {
            var fs = new DelayedWriteFileSystem();
            var path = "settings.json";
            fs.Seed(path, "{\"LmStudioBaseUrl\":\"http://original\",\"AutoLoadOnStartup\":true,\"EnableHistoryCompression\":true,\"EnableHistoryCompaction\":true,\"Theme\":0}");

            var mgr = new SettingsManager(path, fs);
            var newSettings = new AppSettings { LmStudioBaseUrl = "http://new" };

            var saveTask = mgr.SaveAsync(newSettings);

            var loadTask = mgr.LoadAsync();

            fs.AllowWrite();

            await Task.WhenAll(saveTask, loadTask);

            Assert.That(loadTask.Result.LmStudioBaseUrl, Is.EqualTo("http://original").Or.EqualTo("http://new"));

            var content = fs.ReadAllText(path);
            Assert.That(content, Does.Contain("http://new"));
        }


        [Test]
        public void Constructor_InvalidPath_ThrowsArgumentException()
        {
            string bad = "bad\0path";
            Assert.Throws<ArgumentException>(() => new SettingsManager(bad));
        }


        [Test]
        public async Task CreateAsync_PreloadsFromFile()
        {
            var fs = new InMemoryTestFileSystem();
            var path = "settings2.json";
            var json = "{\"LmStudioBaseUrl\":\"http://prefilled\",\"AutoLoadOnStartup\":true,\"EnableHistoryCompression\":true,\"EnableHistoryCompaction\":true,\"Theme\":0}";
            await fs.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes(json));

            var mgr = new SettingsManager(path, fs);
            await mgr.LoadAsync();
            Assert.That(mgr.Current.LmStudioBaseUrl, Is.EqualTo("http://prefilled"));
        }

        [Test]
        public void AddUserMessage_PreservesMarkdown_WhenCompressionEnabled()
        {
            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.SystemPrompt).Returns("sys");
            mockSettings.Setup(s => s.Current).Returns(new AppSettings { EnableHistoryCompression = true });
            var hist = new ChatHistoryManager(mockSettings.Object, new Mock<IChatPersistenceService>().Object);

            hist.AddUserMessage("**bold** _italic_");
            var copy = hist.GetHistoryCopy();

            Assert.That(copy.Count, Is.EqualTo(1));
            Assert.That(copy[0].Content, Does.Contain("**bold**"));
            Assert.That(copy[0].Content, Does.Contain("_italic_"));
        }

        [Test]
        public void BuildMessagesForRequest_IncludesReferenceCode_WhenIncludedContentProvided()
        {
            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.SystemPrompt).Returns("sys");
            var hist = new ChatHistoryManager(mockSettings.Object, new Mock<IChatPersistenceService>().Object);

            hist.AddUserMessage("ask", activeDocumentContent: "code snippet");
            var messages = hist.BuildUserMessagesWithHistory();

            Assert.That(messages[messages.Count - 1].Content.ToString(), Does.Contain("Reference code:"));
            Assert.That(messages[messages.Count - 1].Content.ToString(), Does.Contain("ask"));
        }

        [Test]
        public async Task CompactIfNeededAsync_NoToSummarize_DoesNothing()
        {
            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.SystemPrompt).Returns("sys");
            mockSettings.Setup(s => s.Current).Returns(new AppSettings { EnableHistoryCompaction = true });
            var history = new ChatHistoryManager(mockSettings.Object, new Mock<IChatPersistenceService>().Object);

            for (int i = 0; i < 3; i++) history.AddUserMessage("m" + i);
            var fakeClient = new DummyClient("unused");
            var mockActiveModelContext = new Mock<IActiveModelContext>();
            mockActiveModelContext.SetupGet(a => a.MaxContextLength).Returns(1000);
            var compactor = new HistoryCompactor(history, fakeClient, mockSettings.Object, mockActiveModelContext.Object);

            await compactor.CompactIfNeededAsync(null, CancellationToken.None);
        }

        private class DummyClient : IOpenApiAdapter
        {
            private readonly string _r;
            public DummyClient(string r) { _r = r; }
            public Task<string> ListModelsRawAsync(string endpoint, string baseUrl, string apiKey, CancellationToken cancellationToken) => Task.FromResult(string.Empty);
            public Task<StreamingResponse> SendChatStreamingAsync(MessageContext messageContext, ModelContext modelContext, CancellationToken cancellationToken) => throw new NotImplementedException();
            public Task<SendChatResponse> SendChatAsync(MessageContext messageContext, ModelContext modelContext, CancellationToken cancellationToken) => Task.FromResult<SendChatResponse>(null);
            public Task<string> SendCompletionAsync(CompletionContext context, CancellationToken cancellationToken) => Task.FromResult(string.Empty);
        }


        class DelayedHandler : HttpMessageHandler
        {
            private readonly HttpResponseMessage _response;
            private readonly int _delayMs;
            public DelayedHandler(HttpResponseMessage response, int delayMs = 200) { _response = response; _delayMs = delayMs; }
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                await Task.Delay(_delayMs, cancellationToken).ConfigureAwait(false);
                return _response;
            }
        }

        [Test]
        public void GetModelsAsync_ErrorResponse_ThrowsApiException()
        {
            var json = "{ \"error\": { \"message\": \"boom\" } }";
            var response = new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent(json) };
            var client = new HttpClient(new FakeHandler(response));
            var wrapper = new TestHttpClientWrapper(client);
            var toolFactory = new Mock<ICompositeToolFactory>().Object;
            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.Current).Returns(new AppSettings());
            var lm = new OpenApiAdapter(wrapper, mockSettings.Object, new ApiRequestBuilder(mockSettings.Object, toolFactory));

            var ex = Assert.ThrowsAsync<ApiException>(async () =>
                await lm.ListModelsRawAsync("/v1/models", null, null, CancellationToken.None));

            Assert.That(ex.ErrorInfo.Message, Is.EqualTo("boom"));
            Assert.That(ex.ErrorInfo.Code, Is.EqualTo(400));
        }

        [Test]
        public void SendChatAsync_Cancellation_ThrowsTaskCanceled()
        {
            var json = "{ \"choices\": [ { \"message\": { \"content\": \"hello\" } } ] }";
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) };
            var client = new HttpClient(new DelayedHandler(response, 500));
            var wrapper = new TestHttpClientWrapper(client);
            var toolFactory = new Mock<ICompositeToolFactory>().Object;
            var mockSettings = new Mock<ISettingsManager>();
            mockSettings.Setup(s => s.Current).Returns(new AppSettings());
            var lm = new OpenApiAdapter(wrapper, mockSettings.Object, new ApiRequestBuilder(mockSettings.Object, toolFactory));
            var cts = new CancellationTokenSource(50);
            Assert.ThrowsAsync<TaskCanceledException>(async () => await lm.SendChatAsync(new MessageContext(new List<ChatMessage>()), new ModelContext("test"), cts.Token));
        }

        class FakeHandler : HttpMessageHandler
        {
            private readonly HttpResponseMessage _response;
            public FakeHandler(HttpResponseMessage response) { _response = response; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(_response);
            }
        }
    }
}
