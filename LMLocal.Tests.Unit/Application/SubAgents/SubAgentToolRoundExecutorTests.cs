using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.SubAgents;
using LMLocal.Application.SubAgents.ReadRefs;
using LMLocal.Application.Tool;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Application.SubAgents
{
    [TestFixture]
    public class SubAgentToolRoundExecutorTests
    {
        private Mock<IToolExecutionManager> _manager;
        private List<ToolActivityEvent> _events;
        private SubAgentRunEvidence _evidence;

        [SetUp]
        public void SetUp()
        {
            _manager = new Mock<IToolExecutionManager>();
            _events = new List<ToolActivityEvent>();
            _evidence = new SubAgentRunEvidence();
        }

        private static ToolQueue Queue(params string[] toolNames)
            => ToolQueue.ForSubAgent("code_reader_subagent", toolNames.Select(n => new ToolDefinition { Name = n }).ToList());

        private static ToolCallRecord Call(string name, string id)
            => new ToolCallRecord { CallId = id, FunctionName = name, ArgumentsJson = "{}" };

        private SubAgentToolRoundExecutor CreateExecutor(ToolQueue queue)
            => new SubAgentToolRoundExecutor(() => _manager.Object, queue, new SyncProgress(_events), _evidence);

        private void Returns(string toolName, object result, string error = null)
        {
            _manager
                .Setup(m => m.ExecuteToolAsync(It.Is<ToolCallRecord>(c => string.Equals(c.FunctionName, toolName, StringComparison.OrdinalIgnoreCase)), It.IsAny<CancellationToken>(), It.IsAny<ToolQueue>()))
                .ReturnsAsync(new ToolExecutionResult { ToolName = toolName, Result = result, Error = error });
        }

        private static ReadFileLines.FileLinesResponse ReadResponse()
        {
            return new ReadFileLines.FileLinesResponse
            {
                Success = true,
                FilePath = @"src\Program.cs",
                AbsolutePath = @"C:\sol\src\Program.cs",
                Content = new List<ReadFileLines.FileLineEntry> { new ReadFileLines.FileLineEntry { LineNumber = 1, Text = "line 1" } }
            };
        }

        [Test]
        public async Task NotAllowedTool_IsNotRun_AndGetsError()
        {
            var executor = CreateExecutor(Queue("read_file_lines"));

            var results = await executor.ExecuteAsync(new[] { Call("delete_file", "c1") }, CancellationToken.None);

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].ToolCallId, Is.EqualTo("c1"));
            Assert.That(results[0].Result, Is.EqualTo("Error: tool 'delete_file' is not allowed in SubAgent context."));
            Assert.That(executor.UsedTools, Is.Empty);
            Assert.That(executor.Steps, Is.EqualTo(0));
            Assert.That(_events, Is.Empty);
            _manager.Verify(m => m.ExecuteToolAsync(It.IsAny<ToolCallRecord>(), It.IsAny<CancellationToken>(), It.IsAny<ToolQueue>()), Times.Never);
        }

        [Test]
        public async Task AllowedTools_ReportSteps_AndKeepCallOrder()
        {
            Returns("find_files", "found");
            Returns("read_file_lines", ReadResponse());
            var executor = CreateExecutor(Queue("find_files", "read_file_lines"));

            var results = await executor.ExecuteAsync(new[] { Call("find_files", "c1"), Call("read_file_lines", "c2") }, CancellationToken.None);

            Assert.That(results.Select(r => r.ToolCallId), Is.EqualTo(new[] { "c1", "c2" }));
            Assert.That(results[0].Result, Is.EqualTo("found"));
            Assert.That(_events.Select(e => e.Step), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(_events.Select(e => e.ToolName), Is.EqualTo(new[] { "find_files", "read_file_lines" }));
            Assert.That(executor.Steps, Is.EqualTo(2));
            Assert.That(executor.UsedTools, Is.EquivalentTo(new[] { "find_files", "read_file_lines" }));
        }

        [Test]
        public async Task Steps_ContinueAcrossRounds()
        {
            Returns("find_files", "found");
            var executor = CreateExecutor(Queue("find_files"));

            await executor.ExecuteAsync(new[] { Call("find_files", "c1") }, CancellationToken.None);
            await executor.ExecuteAsync(new[] { Call("FIND_FILES", "c2") }, CancellationToken.None);

            Assert.That(executor.Steps, Is.EqualTo(2));
            Assert.That(executor.UsedTools, Has.Count.EqualTo(1), "used tools are a case-insensitive set");
        }

        [Test]
        public async Task SuccessfulRead_IsRecordedAsEvidence()
        {
            Returns("read_file_lines", ReadResponse());
            var executor = CreateExecutor(Queue("read_file_lines"));

            await executor.ExecuteAsync(new[] { Call("read_file_lines", "c1") }, CancellationToken.None);

            Assert.That(_evidence.IsEmpty, Is.False);
        }

        [Test]
        public async Task ToolError_GoesToResultAndError_AndIsNotRecorded()
        {
            Returns("read_file_lines", ReadResponse(), error: "File not found");
            var executor = CreateExecutor(Queue("read_file_lines"));

            var results = await executor.ExecuteAsync(new[] { Call("read_file_lines", "c1") }, CancellationToken.None);

            Assert.That(results[0].Result, Is.EqualTo("File not found"));
            Assert.That(results[0].Error, Is.EqualTo("File not found"));
            Assert.That(_evidence.IsEmpty, Is.True);
            Assert.That(executor.Steps, Is.EqualTo(1), "a failed allowed call still counts as a step");
        }

        [Test]
        public void CancelledBeforeCall_Throws_AndRunsNothing()
        {
            var executor = CreateExecutor(Queue("find_files"));
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();

                Assert.ThrowsAsync<OperationCanceledException>(
                    () => executor.ExecuteAsync(new[] { Call("find_files", "c1") }, cts.Token));
            }

            _manager.Verify(m => m.ExecuteToolAsync(It.IsAny<ToolCallRecord>(), It.IsAny<CancellationToken>(), It.IsAny<ToolQueue>()), Times.Never);
        }

        [Test]
        public async Task NullCalls_ReturnEmptyList()
        {
            var results = await CreateExecutor(Queue("find_files")).ExecuteAsync(null, CancellationToken.None);

            Assert.That(results, Is.Empty);
        }

        /// <summary>Reports synchronously; Progress&lt;T&gt; would post to a synchronization context.</summary>
        private sealed class SyncProgress : IProgress<ToolActivityEvent>
        {
            private readonly List<ToolActivityEvent> _events;

            public SyncProgress(List<ToolActivityEvent> events)
            {
                _events = events;
            }

            public void Report(ToolActivityEvent value) => _events.Add(value);
        }
    }
}
