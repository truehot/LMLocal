using System;
using System.Collections.Generic;
using System.Linq;
using LMLocal.Application.Tool;
using LMLocal.Core.Models;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Internal
{
    /// <summary>
    /// Unit tests for <see cref="ToolCallLoopDetector"/>.
    /// Pure logic — no mocks, no dependencies.
    /// </summary>
    [TestFixture]
    public class ToolCallLoopDetectorTests
    {
        private readonly ToolCallLoopDetector _detector = new ToolCallLoopDetector();

        /// <summary>
        /// Helper to create a ToolCallRecord with the given function name and arguments JSON.
        /// </summary>
        private static ToolCallRecord T(string functionName, string argumentsJson = "{}")
        {
            return new ToolCallRecord
            {
                Index = 0,
                CallId = "call_0",
                FunctionName = functionName,
                ArgumentsJson = argumentsJson
            };
        }

        // --- Null / empty edge cases ---

        [Test]
        public void BothListsNull_ReturnsFalse()
        {
            Assert.That(_detector.AreSameToolCalls(null, null), Is.False);
        }

        [Test]
        public void CurrentNull_ReturnsFalse()
        {
            Assert.That(_detector.AreSameToolCalls(null, new List<ToolCallRecord>()), Is.False);
        }

        [Test]
        public void PreviousNull_ReturnsFalse()
        {
            Assert.That(_detector.AreSameToolCalls(new List<ToolCallRecord>(), null), Is.False);
        }

        [Test]
        public void BothEmpty_ReturnsTrue()
        {
            Assert.That(
                _detector.AreSameToolCalls(Array.Empty<ToolCallRecord>(), Array.Empty<ToolCallRecord>()),
                Is.True);
        }

        // --- Count mismatch ---

        [Test]
        public void DifferentCount_ReturnsFalse()
        {
            Assert.That(
                _detector.AreSameToolCalls(new[] { T("a") }, new[] { T("a"), T("b") }),
                Is.False);
        }

        // --- Function name matching ---

        [Test]
        public void DifferentFunctionName_ReturnsFalse()
        {
            Assert.That(
                _detector.AreSameToolCalls(new[] { T("read_file") }, new[] { T("write_file") }),
                Is.False);
        }

        [Test]
        public void FunctionNameCaseInsensitive_ReturnsTrue()
        {
            Assert.That(
                _detector.AreSameToolCalls(new[] { T("ReadFileLines") }, new[] { T("readfilelines") }),
                Is.True);
        }

        // --- Arguments JSON matching ---

        [Test]
        public void DifferentArguments_ReturnsFalse()
        {
            Assert.That(
                _detector.AreSameToolCalls(
                    new[] { T("read", "{\"file\":\"a.txt\"}") },
                    new[] { T("read", "{\"file\":\"b.txt\"}") }),
                Is.False);
        }

        [Test]
        public void SameFunctionNameAndArguments_ReturnsTrue()
        {
            Assert.That(
                _detector.AreSameToolCalls(
                    new[] { T("read", "{\"file\":\"a.txt\"}") },
                    new[] { T("read", "{\"file\":\"a.txt\"}") }),
                Is.True);
        }

        [Test]
        public void ArgumentsJson_OrdinalComparison_WhitespaceDifference_ReturnsFalse()
        {
            // Ordinal comparison means whitespace matters — intentional design choice.
            Assert.That(
                _detector.AreSameToolCalls(
                    new[] { T("read", "{\"a\":1}") },
                    new[] { T("read", "{\"a\": 1}") }),
                Is.False);
        }

        // --- Multiple tools (parallel calls) ---

        [Test]
        public void MultipleTools_AllMatch_ReturnsTrue()
        {
            Assert.That(
                _detector.AreSameToolCalls(
                    new[] { T("a", "{}"), T("b", "{\"x\":1}") },
                    new[] { T("a", "{}"), T("b", "{\"x\":1}") }),
                Is.True);
        }

        [Test]
        public void MultipleTools_SameCountDifferentOrder_ReturnsFalse()
        {
            Assert.That(
                _detector.AreSameToolCalls(
                    new[] { T("a", "{}"), T("b", "{}") },
                    new[] { T("b", "{}"), T("a", "{}") }),
                Is.False);
        }

        [Test]
        public void MultipleTools_OneDiffers_ReturnsFalse()
        {
            Assert.That(
                _detector.AreSameToolCalls(
                    new[] { T("a", "{\"v\":1}"), T("b", "{}") },
                    new[] { T("a", "{\"v\":2}"), T("b", "{}") }),
                Is.False);
        }
    }
}
