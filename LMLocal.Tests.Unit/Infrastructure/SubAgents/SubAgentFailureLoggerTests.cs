#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.SubAgents;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.SubAgents;
using LMLocal.Tests.Unit.Infrastructure;
using Moq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.SubAgents
{
    /// <summary>
    /// Guards the Debug-only SubAgent failure log: one JSON line per failure, carrying the FULL prompt
    /// and the run parameters so a failing run can be replayed.
    /// </summary>
    [TestFixture]
    public class SubAgentFailureLoggerTests
    {
        private const string Folder = "C:/tests/subagent-failures";

        private InMemoryFileSystem _fileSystem;
        private SubAgentFailureLogger _logger;

        [SetUp]
        public void SetUp()
        {
            _fileSystem = new InMemoryFileSystem();
            _logger = new SubAgentFailureLogger(_fileSystem, Folder);
        }

        private string ReadLog() => _fileSystem.ReadAllText(_logger.LogFilePath);

        private List<string> ReadLines()
            => ReadLog().Split('\n').Where(line => line.Length > 0).ToList();

        private static SubAgentRunRequest BuildRequest(string prompt) => new SubAgentRunRequest
        {
            AgentName = "code_explorer",
            Prompt = prompt,
            System = "system prompt",
            Model = "qwen2.5-coder-32b",
            ProviderType = "lmstudio",
            BaseUrl = "http://localhost:1234/v1",
            Temperature = 0.2,
            ReasoningEffort = "low",
            MaxTokens = 4096,
            MaxRounds = 10,
            TimeoutSeconds = 120,
            AllowedTools = new List<string> { "read_file_lines", "search_file_content" },
            ExcludedAgentNames = new List<string> { "reviewer" },
            ApiKey = "sk-super-secret-key"
        };

        [Test]
        public async Task Line_ContainsFullPrompt_NotTruncated()
        {
            var prompt = new string('x', 200000);

            await _logger.LogFromRequestAsync(BuildRequest(prompt), "Final", "boom");

            var line = JObject.Parse(ReadLines().Single());
            Assert.That(line.Value<string>("prompt"), Is.EqualTo(prompt));
            Assert.That(line.Value<int>("promptChars"), Is.EqualTo(prompt.Length));
            Assert.That(line.Value<bool>("promptTruncated"), Is.False);
        }

        [Test]
        public async Task Line_RoundTripsIntoSubAgentRunRequest()
        {
            await _logger.LogFromRequestAsync(BuildRequest("Посмотри как работают сабагенты"), "Final", "boom");

            var restored = JsonConvert.DeserializeObject<SubAgentRunRequest>(ReadLines().Single());

            Assert.That(restored, Is.Not.Null);
            Assert.That(restored.AgentName, Is.EqualTo("code_explorer"));
            Assert.That(restored.Prompt, Is.EqualTo("Посмотри как работают сабагенты"));
            Assert.That(restored.System, Is.EqualTo("system prompt"));
            Assert.That(restored.Model, Is.EqualTo("qwen2.5-coder-32b"));
            Assert.That(restored.ProviderType, Is.EqualTo("lmstudio"));
            Assert.That(restored.BaseUrl, Is.EqualTo("http://localhost:1234/v1"));
            Assert.That(restored.Temperature, Is.EqualTo(0.2));
            Assert.That(restored.ReasoningEffort, Is.EqualTo("low"));
            Assert.That(restored.MaxTokens, Is.EqualTo(4096));
            Assert.That(restored.MaxRounds, Is.EqualTo(10));
            Assert.That(restored.TimeoutSeconds, Is.EqualTo(120));
            Assert.That(restored.AllowedTools, Is.EquivalentTo(new[] { "read_file_lines", "search_file_content" }));
            Assert.That(restored.ExcludedAgentNames, Is.EquivalentTo(new[] { "reviewer" }));
            Assert.That(restored.ApiKey, Is.Null, "Secrets must never round-trip from the failure log.");
        }

        [Test]
        public async Task SinglePhysicalLine_WhenPromptHasNewlines()
        {
            const string prompt = "line1\r\nline2\nline3";

            await _logger.LogFromRequestAsync(BuildRequest(prompt), "Final", "boom");

            Assert.That(ReadLines(), Has.Count.EqualTo(1), "A record must occupy exactly one physical line.");
            Assert.That(JObject.Parse(ReadLines()[0]).Value<string>("prompt"), Is.EqualTo(prompt));
        }

        [Test]
        public async Task Line_DoesNotContainApiKey()
        {
            await _logger.LogFromRequestAsync(BuildRequest("prompt"), "Final", "boom");

            Assert.That(ReadLog(), Does.Not.Contain("sk-super-secret-key"));
        }

        [Test]
        public async Task ConcurrentLogs_DoNotCorruptOrThrow()
        {
            const int count = 32;

            var tasks = Enumerable.Range(0, count)
                .Select(i => _logger.LogFromRequestAsync(BuildRequest("prompt-" + i), "Final", "boom-" + i))
                .ToList();

            await Task.WhenAll(tasks).ConfigureAwait(false);

            var lines = ReadLines();
            Assert.That(lines, Has.Count.EqualTo(count), "Every record must be appended exactly once.");

            var prompts = new HashSet<string>();
            foreach (var line in lines)
            {
                // Every line must stay a standalone, parseable JSON document.
                prompts.Add(JObject.Parse(line).Value<string>("prompt"));
            }

            Assert.That(prompts, Has.Count.EqualTo(count), "Records must not be interleaved or corrupted.");
        }

        [Test]
        public async Task Rotation_CreatesNewFileAndKeepsLimit()
        {
            var logger = new SubAgentFailureLogger(_fileSystem, Folder, maxFileBytes: 200);

            for (int i = 0; i < 20; i++)
            {
                await logger.LogFromRequestAsync(BuildRequest("prompt-" + i), "Final", "boom");
            }

            var rolled = Path.Combine(Folder, "SubAgentsFailures.1.jsonl");
            Assert.That(_fileSystem.FileExists(rolled), Is.True, "The log must rotate once the size limit is reached.");

            int fileCount = _fileSystem.GetAllFiles().Count(f => f.Contains("subagentsfailures"));

            Assert.That(fileCount, Is.LessThanOrEqualTo(SubAgentFailureLogger.MaxRotatedFiles + 1),
                "Rotation must keep the number of files bounded.");
        }

        [Test]
        public void IoFailure_DoesNotThrow()
        {
            var fileSystem = new Mock<IFileSystem>();
            fileSystem.Setup(f => f.FileExists(It.IsAny<string>())).Returns(false);
            fileSystem
                .Setup(f => f.AppendAllBytesAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException("file is locked"));

            var logger = new SubAgentFailureLogger(fileSystem.Object, Folder);

            Assert.DoesNotThrowAsync(() => logger.LogFromRequestAsync(BuildRequest("prompt"), "Final", "boom"));
        }

        [Test]
        public async Task ToolCall_UsesTaskArgumentAsPrompt()
        {
            var call = new ToolCallRecord
            {
                CallId = "call_1",
                FunctionName = "code_explorer",
                ArgumentsJson = "{\"task\":\"найди все вызовы SubAgents\"}"
            };

            await _logger.LogFromToolCallAsync(call, "Parallel", "boom");

            var line = JObject.Parse(ReadLines().Single());
            Assert.That(line.Value<string>("prompt"), Is.EqualTo("найди все вызовы SubAgents"));
            Assert.That(line.Value<string>("stage"), Is.EqualTo("Parallel"));
            Assert.That(line.Value<string>("callId"), Is.EqualTo("call_1"));
            Assert.That(line.Value<string>("rawArgumentsJson"), Does.Contain("task"));
        }
    }
}
#endif
