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

        private static SubAgentRecentCall BuildCall(
            string toolName,
            string callId,
            int? round = null,
            int? step = null,
            string argumentsJson = null,
            string resultText = null,
            string error = null) => new SubAgentRecentCall
            {
                ToolName = toolName,
                CallId = callId,
                Round = round,
                Step = step,
                ArgumentsJson = argumentsJson,
                ResultText = resultText,
                Error = error
            };

        private static JToken RecentCallsOf(JObject line) => line["recentCalls"];

        [Test]
        public async Task RecentCalls_AppearInSerializedLine_WithRoundAndStep()
        {
            var calls = new List<SubAgentRecentCall>
            {
                BuildCall("read_file_lines", "call_1", 1, 4, "{\"path\":\"src/a.cs\"}", "using System;"),
                BuildCall("search_file_content", "call_2", 2, 5, "{\"pattern\":\"FailureLogger\"}", "src/b.cs:12: x")
            };

            await _logger.LogFromRequestAsync(BuildRequest("prompt"), "Final", "boom", recentCalls: calls);

            Assert.That(ReadLines(), Has.Count.EqualTo(1), "The attempt list must not break the one-line-per-failure rule.");

            var recent = (JArray)RecentCallsOf(JObject.Parse(ReadLines()[0]));
            Assert.That(recent, Is.Not.Null);
            Assert.That(recent, Has.Count.EqualTo(2));

            Assert.That(recent[0].Value<string>("toolName"), Is.EqualTo("read_file_lines"));
            Assert.That(recent[0].Value<string>("callId"), Is.EqualTo("call_1"));
            Assert.That(recent[0].Value<int>("round"), Is.EqualTo(1));
            Assert.That(recent[0].Value<int>("step"), Is.EqualTo(4));
            Assert.That(recent[0].Value<string>("resultText"), Is.EqualTo("using System;"));

            Assert.That(recent[1].Value<string>("toolName"), Is.EqualTo("search_file_content"));
            Assert.That(recent[1].Value<int>("round"), Is.EqualTo(2));
            Assert.That(recent[1].Value<int>("step"), Is.EqualTo(5));
        }

        [Test]
        public async Task RecentCalls_OnlyLastThreeAreKept()
        {
            var calls = Enumerable.Range(1, 5)
                .Select(i => BuildCall("tool_" + i, "call_" + i, i, i, "{\"i\":" + i + "}"))
                .ToList();

            await _logger.LogFromRequestAsync(BuildRequest("prompt"), "Final", "boom", recentCalls: calls);

            var recent = (JArray)RecentCallsOf(JObject.Parse(ReadLines()[0]));

            Assert.That(recent, Has.Count.EqualTo(SubAgentFailureLogger.MaxRecentCalls));
            Assert.That(
                recent.Select(c => c.Value<string>("callId")),
                Is.EqualTo(new[] { "call_3", "call_4", "call_5" }),
                "Only the tail of the call chain matters for diagnosing the failure.");
        }

        [Test]
        public async Task FailingCall_ArgumentsNotTruncated()
        {
            var arguments = "{\"path\":\"" + new string('a', 5000) + "\"}";

            await _logger.LogFromRequestAsync(
                BuildRequest("prompt"),
                "Final",
                "boom",
                recentCalls: new List<SubAgentRecentCall> { BuildCall("read_file_lines", "call_1", 1, 1, arguments) });

            var call = ((JArray)RecentCallsOf(JObject.Parse(ReadLines()[0])))[0];

            Assert.That(call.Value<string>("argumentsJson"), Is.EqualTo(arguments),
                "The failing call keeps its arguments in full: they are needed for a replay.");
            Assert.That(call.Value<int>("argumentsChars"), Is.EqualTo(arguments.Length));
            Assert.That(call.Value<bool?>("argumentsTruncated"), Is.False);
        }

        [Test]
        public async Task PreviousCall_ArgumentsTruncated_WithOriginalChars()
        {
            var arguments = "{\"path\":\"" + new string('a', 5000) + "\"}";

            var calls = new List<SubAgentRecentCall>
            {
                BuildCall("read_file_lines", "call_1", 1, 1, arguments),
                BuildCall("read_file_lines", "call_2", 1, 2, "{\"path\":\"b.cs\"}")
            };

            await _logger.LogFromRequestAsync(BuildRequest("prompt"), "Final", "boom", recentCalls: calls);

            var recent = (JArray)RecentCallsOf(JObject.Parse(ReadLines()[0]));

            Assert.That(recent[0].Value<string>("argumentsJson").Length,
                Is.EqualTo(SubAgentFailureLogger.MaxRecentCallArgumentsChars));
            Assert.That(recent[0].Value<bool>("argumentsTruncated"), Is.True);
            Assert.That(recent[0].Value<int>("argumentsChars"), Is.EqualTo(arguments.Length),
                "Chars must carry the original length, not the truncated one.");
        }

        [Test]
        public async Task Result_TruncatedAtThreshold_KeepsOriginalChars()
        {
            var result = new string('r', 9000);

            await _logger.LogFromRequestAsync(
                BuildRequest("prompt"),
                "Final",
                "boom",
                recentCalls: new List<SubAgentRecentCall> { BuildCall("read_file_lines", "call_1", 1, 1, null, result) });

            var call = ((JArray)RecentCallsOf(JObject.Parse(ReadLines()[0])))[0];

            Assert.That(call.Value<string>("resultText").Length,
                Is.EqualTo(SubAgentFailureLogger.MaxRecentCallResultChars));
            Assert.That(call.Value<bool>("resultTruncated"), Is.True);
            Assert.That(call.Value<int>("resultChars"), Is.EqualTo(result.Length));
        }

        [Test]
        public async Task RejectedToolCall_IsRecorded_WithoutResult_WithError()
        {
            await _logger.LogFromRequestAsync(
                BuildRequest("prompt"),
                "Final",
                "boom",
                recentCalls: new List<SubAgentRecentCall>
                {
                    BuildCall(
                        "write_file",
                        "call_1",
                        1,
                        null,
                        "{\"path\":\"a.cs\"}",
                        error: "Tool 'write_file' is not allowed in SubAgent context.")
                });

            var call = ((JArray)RecentCallsOf(JObject.Parse(ReadLines()[0])))[0];

            Assert.That(call.Value<string>("error"), Does.Contain("not allowed"));
            Assert.That(call["resultText"], Is.Null, "A rejected call has no result.");
            Assert.That(call["resultChars"], Is.Null);
        }

        [Test]
        public async Task NoRecentCalls_FieldOmittedFromLine()
        {
            await _logger.LogFromRequestAsync(BuildRequest("prompt"), "Final", "boom");

            Assert.That(RecentCallsOf(JObject.Parse(ReadLines()[0])), Is.Null,
                "An empty attempt list must not add a field to the line.");

            var call = new ToolCallRecord
            {
                CallId = "call_1",
                FunctionName = "code_explorer",
                ArgumentsJson = "{\"task\":\"t\"}"
            };

            await _logger.LogFromToolCallAsync(call, "Parallel", "boom");

            Assert.That(RecentCallsOf(JObject.Parse(ReadLines()[1])), Is.Null,
                "stage=Parallel only has the current call, so it never reports recentCalls.");
        }

        [Test]
        public async Task SerializedLine_WithRecentCalls_StillRoundTripsIntoRunRequest()
        {
            await _logger.LogFromRequestAsync(
                BuildRequest("Посмотри как работают сабагенты"),
                "Final",
                "boom",
                recentCalls: new List<SubAgentRecentCall>
                {
                    BuildCall("read_file_lines", "call_1", 1, 1, "{\"path\":\"a.cs\"}", "text")
                });

            var restored = JsonConvert.DeserializeObject<SubAgentRunRequest>(ReadLines()[0]);

            Assert.That(restored, Is.Not.Null);
            Assert.That(restored.Prompt, Is.EqualTo("Посмотри как работают сабагенты"),
                "Extra fields must not break the replay path.");
            Assert.That(restored.Model, Is.EqualTo("qwen2.5-coder-32b"));
            Assert.That(restored.ApiKey, Is.Null);
        }
    }
}
#endif
