using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Application.Tool;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Tooling;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Services.Tool
{
    [TestFixture]
    public class ToolExecutionManagerTests
    {
        private Mock<IToolRouter> _toolRouterMock;
        private Mock<IToolQueueProvider> _toolQueueProviderMock;

        [SetUp]
        public void SetUp()
        {
            _toolRouterMock = new Mock<IToolRouter>();
            _toolQueueProviderMock = new Mock<IToolQueueProvider>();
            _toolQueueProviderMock
                .Setup(p => p.GetMainQueue())
                .Returns(ToolQueue.Main(new List<ToolDefinition>()));
        }

        private ToolExecutionManager CreateManager()
        {
            return new ToolExecutionManager(_toolRouterMock.Object, _toolQueueProviderMock.Object);
        }

        private void AllowToolsInMainQueue(params string[] names)
        {
            var defs = new List<ToolDefinition>();
            foreach (var n in names)
            {
                defs.Add(new ToolDefinition { Name = n });
            }
            _toolQueueProviderMock
                .Setup(p => p.GetMainQueue())
                .Returns(ToolQueue.Main(defs));
        }

        [Test]
        public async Task ExecuteToolAsync_NullToolCall_ReturnsError()
        {
            var mgr = CreateManager();
            var res = await mgr.ExecuteToolAsync(null, CancellationToken.None);
            Assert.That(res, Is.Not.Null);
            Assert.That(res.Error, Is.EqualTo("Tool call is null"));
        }

        [Test]
        public async Task ExecuteToolAsync_ToolNotFound_ReturnsNotFoundError()
        {
            var call = new ToolCallRecord { CallId = "id1", FunctionName = "nonexist", ArgumentsJson = null };

            var mgr = CreateManager();
            var res = await mgr.ExecuteToolAsync(call, CancellationToken.None);

            Assert.That(res.ToolId, Is.EqualTo("id1"));
            Assert.That(res.ToolName, Is.EqualTo("nonexist"));
            Assert.That(res.Error, Does.Contain("not found"));
            Assert.That(res.IsSuccess, Is.False);
        }

        [Test]
        public async Task ExecuteToolAsync_NotAllowedByQueue_ReturnsNotFoundError()
        {
            // Tool registered in the router but absent from the queue → blocked by the queue guard.
            var call = new ToolCallRecord { CallId = "id1b", FunctionName = "hidden_tool", ArgumentsJson = null };
            _toolRouterMock.Setup(f => f.ToolExists("hidden_tool")).Returns(true);

            var mgr = CreateManager();
            var res = await mgr.ExecuteToolAsync(call, CancellationToken.None);

            Assert.That(res.Error, Does.Contain("not allowed"));
            _toolRouterMock.Verify(
                f => f.ExecuteAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task ExecuteToolAsync_Success_ParsesArgumentsAndReturnsResult()
        {
            var call = new ToolCallRecord { CallId = "id2", FunctionName = "mytool", ArgumentsJson = "{\"a\":1}" };
            AllowToolsInMainQueue("mytool");

            var expectedResult = new { Value = 123 };
            _toolRouterMock.Setup(f => f.ExecuteAsync("mytool", It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>())).ReturnsAsync(expectedResult);
            _toolRouterMock.Setup(f => f.GetCompletionMessage("mytool", expectedResult)).Returns("done");

            var mgr = CreateManager();
            var res = await mgr.ExecuteToolAsync(call, CancellationToken.None);

            Assert.That(res.ToolId, Is.EqualTo("id2"));
            Assert.That(res.ToolName, Is.EqualTo("mytool"));
            Assert.That(res.Result, Is.SameAs(expectedResult));
            Assert.That(res.CompletionMessage, Is.EqualTo("done"));
            Assert.That(res.IsSuccess, Is.True);
        }

        [Test]
        public async Task ExecuteToolAsync_OperationCanceledException_ReturnsCancelledError()
        {
            var call = new ToolCallRecord { CallId = "id3", FunctionName = "cancel", ArgumentsJson = null };
            AllowToolsInMainQueue("cancel");
            _toolRouterMock.Setup(f => f.ExecuteAsync("cancel", It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException());

            var mgr = CreateManager();
            var res = await mgr.ExecuteToolAsync(call, CancellationToken.None);

            Assert.That(res.Error, Does.Contain("cancelled"));
            Assert.That(res.IsSuccess, Is.False);
        }

        [Test]
        public async Task ExecuteToolAsync_ArgumentException_ReturnsInvalidParametersError()
        {
            var call = new ToolCallRecord { CallId = "id4", FunctionName = "arg", ArgumentsJson = null };
            AllowToolsInMainQueue("arg");
            _toolRouterMock.Setup(f => f.ExecuteAsync("arg", It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>())).ThrowsAsync(new ArgumentException("bad"));

            var mgr = CreateManager();
            var res = await mgr.ExecuteToolAsync(call, CancellationToken.None);

            Assert.That(res.Error, Does.Contain("Invalid parameters"));
            Assert.That(res.IsSuccess, Is.False);
        }

        [Test]
        public async Task ExecuteToolAsync_GenericException_ReturnsExecutionError()
        {
            var call = new ToolCallRecord { CallId = "id5", FunctionName = "boom", ArgumentsJson = null };
            AllowToolsInMainQueue("boom");
            _toolRouterMock.Setup(f => f.ExecuteAsync("boom", It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>())).ThrowsAsync(new Exception("boom"));

            var mgr = CreateManager();
            var res = await mgr.ExecuteToolAsync(call, CancellationToken.None);

            Assert.That(res.Error, Does.Contain("Execution error"));
            Assert.That(res.IsSuccess, Is.False);
        }

        [Test]
        public async Task ExecuteToolAsync_IsInvalid_ReturnsErrorWithoutExecutingTool()
        {
            var call = new ToolCallRecord { CallId = "id6", FunctionName = "invalid", ArgumentsJson = "{}", IsInvalid = true };
            AllowToolsInMainQueue("invalid");

            var mgr = CreateManager();
            var res = await mgr.ExecuteToolAsync(call, CancellationToken.None);

            Assert.That(res.ToolId, Is.EqualTo("id6"));
            Assert.That(res.ToolName, Is.EqualTo("invalid"));
            Assert.That(res.Error, Does.Contain("not valid JSON"));
            Assert.That(res.IsSuccess, Is.False);
            _toolRouterMock.Verify(
                f => f.ExecuteAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public async Task ExecuteToolAsync_ExplicitQueue_AllowsOnlyQueueTools()
        {
            var call = new ToolCallRecord { CallId = "id7", FunctionName = "allowed", ArgumentsJson = null };
            var queue = ToolQueue.ForSubAgent("research", new List<ToolDefinition>
            {
                new ToolDefinition { Name = "allowed" }
            });

            var expectedResult = "subagent_result";
            _toolRouterMock.Setup(f => f.ExecuteAsync("allowed", It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>())).ReturnsAsync(expectedResult);
            _toolRouterMock.Setup(f => f.GetCompletionMessage("allowed", expectedResult)).Returns("done");

            var mgr = CreateManager();
            var res = await mgr.ExecuteToolAsync(call, CancellationToken.None, queue);

            Assert.That(res.IsSuccess, Is.True);
            Assert.That(res.Result, Is.EqualTo(expectedResult));

            // queue for a subagent is passed explicitly; main queue must not be consulted
            _toolQueueProviderMock.Verify(p => p.GetMainQueue(), Times.Never);
        }

        [Test]
        public void GetProcessingMessage_IsInvalid_ReturnsDefault()
        {
            var mgr = CreateManager();
            var call = new ToolCallRecord { FunctionName = "f", ArgumentsJson = "{}", IsInvalid = true };
            Assert.That(mgr.GetProcessingMessage(call), Is.EqualTo("Invalid tool arguments"));
        }

        [Test]
        public void GetProcessingMessage_NullOrInvalidJson_ReturnsDefault()
        {
            var mgr = CreateManager();
            Assert.That(mgr.GetProcessingMessage(null), Is.EqualTo("Processing..."));

            var callBad = new ToolCallRecord { FunctionName = "f", ArgumentsJson = "{bad" };
            Assert.That(mgr.GetProcessingMessage(callBad), Is.EqualTo("Processing..."));
        }

        [Test]
        public void GetProcessingMessage_ValidJson_UsesRouter()
        {
            var call = new ToolCallRecord { FunctionName = "pf", ArgumentsJson = "{\"x\":1}" };
            _toolRouterMock.Setup(f => f.GetProcessingMessage("pf", It.IsAny<Dictionary<string, object>>())).Returns("working");

            var mgr = CreateManager();
            var msg = mgr.GetProcessingMessage(call);

            Assert.That(msg, Is.EqualTo("working"));
        }

        [Test]
        public void ToolExecutionResult_IsSuccess_Property_WorksAsExpected()
        {
            var ok = new ToolExecutionResult { Error = null, Result = new object() };
            Assert.That(ok.IsSuccess, Is.True);

            var noResult = new ToolExecutionResult { Error = null, Result = null };
            Assert.That(noResult.IsSuccess, Is.False);

            var error = new ToolExecutionResult { Error = "err", Result = new object() };
            Assert.That(error.IsSuccess, Is.False);
        }
    }
}
