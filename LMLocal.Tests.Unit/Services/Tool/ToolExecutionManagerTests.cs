using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Tooling;
using LMLocal.Services.Tool;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Services.Tool
{
    [TestFixture]
    public class ToolExecutionManagerTests
    {
        private Mock<ICompositeToolFactory> _vsToolFactory;

        [SetUp]
        public void SetUp()
        {
            _vsToolFactory = new Mock<ICompositeToolFactory>();
        }

        [Test]
        public async Task ExecuteToolAsync_NullToolCall_ReturnsError()
        {
            var mgr = new ToolExecutionManager(_vsToolFactory.Object);
            var res = await mgr.ExecuteToolAsync(null, CancellationToken.None);
            Assert.That(res, Is.Not.Null);
            Assert.That(res.Error, Is.EqualTo("Tool call is null"));
        }

        [Test]
        public async Task ExecuteToolAsync_ToolNotFound_ReturnsNotFoundError()
        {
            var call = new ToolCallRecord { CallId = "id1", FunctionName = "nonexist", ArgumentsJson = null };
            _vsToolFactory.Setup(f => f.ToolExists("nonexist")).Returns(false);

            var mgr = new ToolExecutionManager(_vsToolFactory.Object);
            var res = await mgr.ExecuteToolAsync(call, CancellationToken.None);

            Assert.That(res.ToolId, Is.EqualTo("id1"));
            Assert.That(res.ToolName, Is.EqualTo("nonexist"));
            Assert.That(res.Error, Does.Contain("not found"));
            Assert.That(res.IsSuccess, Is.False);
        }

        [Test]
        public async Task ExecuteToolAsync_Success_ParsesArgumentsAndReturnsResult()
        {
            var call = new ToolCallRecord { CallId = "id2", FunctionName = "mytool", ArgumentsJson = "{\"a\":1}" };
            _vsToolFactory.Setup(f => f.ToolExists("mytool")).Returns(true);

            var expectedResult = new { Value = 123 };
            _vsToolFactory.Setup(f => f.ExecuteAsync("mytool", It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>())).ReturnsAsync(expectedResult);
            _vsToolFactory.Setup(f => f.GetCompletionMessage("mytool", expectedResult)).Returns("done");

            var mgr = new ToolExecutionManager(_vsToolFactory.Object);
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
            _vsToolFactory.Setup(f => f.ToolExists("cancel")).Returns(true);
            _vsToolFactory.Setup(f => f.ExecuteAsync("cancel", It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>())).ThrowsAsync(new OperationCanceledException());

            var mgr = new ToolExecutionManager(_vsToolFactory.Object);
            var res = await mgr.ExecuteToolAsync(call, CancellationToken.None);

            Assert.That(res.Error, Does.Contain("cancelled"));
            Assert.That(res.IsSuccess, Is.False);
        }

        [Test]
        public async Task ExecuteToolAsync_ArgumentException_ReturnsInvalidParametersError()
        {
            var call = new ToolCallRecord { CallId = "id4", FunctionName = "arg", ArgumentsJson = null };
            _vsToolFactory.Setup(f => f.ToolExists("arg")).Returns(true);
            _vsToolFactory.Setup(f => f.ExecuteAsync("arg", It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>())).ThrowsAsync(new ArgumentException("bad"));

            var mgr = new ToolExecutionManager(_vsToolFactory.Object);
            var res = await mgr.ExecuteToolAsync(call, CancellationToken.None);

            Assert.That(res.Error, Does.Contain("Invalid parameters"));
            Assert.That(res.IsSuccess, Is.False);
        }

        [Test]
        public async Task ExecuteToolAsync_GenericException_ReturnsExecutionError()
        {
            var call = new ToolCallRecord { CallId = "id5", FunctionName = "boom", ArgumentsJson = null };
            _vsToolFactory.Setup(f => f.ToolExists("boom")).Returns(true);
            _vsToolFactory.Setup(f => f.ExecuteAsync("boom", It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>())).ThrowsAsync(new Exception("boom"));

            var mgr = new ToolExecutionManager(_vsToolFactory.Object);
            var res = await mgr.ExecuteToolAsync(call, CancellationToken.None);

            Assert.That(res.Error, Does.Contain("Execution error"));
            Assert.That(res.IsSuccess, Is.False);
        }

        [Test]
        public async Task ExecuteToolAsync_IsInvalid_ReturnsErrorWithoutExecutingTool()
        {
            var call = new ToolCallRecord { CallId = "id6", FunctionName = "invalid", ArgumentsJson = "{}", IsInvalid = true };
            _vsToolFactory.Setup(f => f.ToolExists("invalid")).Returns(true);

            var mgr = new ToolExecutionManager(_vsToolFactory.Object);
            var res = await mgr.ExecuteToolAsync(call, CancellationToken.None);

            Assert.That(res.ToolId, Is.EqualTo("id6"));
            Assert.That(res.ToolName, Is.EqualTo("invalid"));
            Assert.That(res.Error, Does.Contain("not valid JSON"));
            Assert.That(res.IsSuccess, Is.False);
            _vsToolFactory.Verify(
                f => f.ExecuteAsync(It.IsAny<string>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [Test]
        public void GetProcessingMessage_IsInvalid_ReturnsDefault()
        {
            var mgr = new ToolExecutionManager(_vsToolFactory.Object);
            var call = new ToolCallRecord { FunctionName = "f", ArgumentsJson = "{}", IsInvalid = true };
            Assert.That(mgr.GetProcessingMessage(call), Is.EqualTo("Invalid tool arguments"));
        }

        [Test]
        public void GetProcessingMessage_NullOrInvalidJson_ReturnsDefault()
        {
            var mgr = new ToolExecutionManager(_vsToolFactory.Object);
            Assert.That(mgr.GetProcessingMessage(null), Is.EqualTo("Processing..."));

            var callBad = new ToolCallRecord { FunctionName = "f", ArgumentsJson = "{bad" };
            Assert.That(mgr.GetProcessingMessage(callBad), Is.EqualTo("Processing..."));
        }

        [Test]
        public void GetProcessingMessage_ValidJson_UsesFactory()
        {
            var call = new ToolCallRecord { FunctionName = "pf", ArgumentsJson = "{\"x\":1}" };
            _vsToolFactory.Setup(f => f.GetProcessingMessage("pf", It.IsAny<Dictionary<string, object>>())).Returns("working");

            var mgr = new ToolExecutionManager(_vsToolFactory.Object);
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
