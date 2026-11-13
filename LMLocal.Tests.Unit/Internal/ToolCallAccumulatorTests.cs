using System.Collections.Generic;
using System.Text;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Infrastructure.Streaming;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    [TestFixture]
    public class ToolCallAccumulatorTests
    {
        /// <summary>
        /// Rejects the arguments as soon as they contain the marker; records what it was given.
        /// </summary>
        private sealed class MarkerGuard : IToolCallArgumentsGuard
        {
            private readonly string _marker;

            public MarkerGuard(string marker)
            {
                _marker = marker;
            }

            public List<(string FunctionName, string Arguments, string Fragment)> Calls { get; } = new List<(string, string, string)>();

            public string Check(string functionName, StringBuilder arguments, string fragment)
            {
                Calls.Add((functionName, arguments.ToString(), fragment));
                return arguments.ToString().Contains(_marker) ? "marker found" : null;
            }
        }

        [Test]
        public void Build_MetadataAndArguments_OneValidCall()
        {
            var accumulator = new ToolCallAccumulator();

            Assert.That(accumulator.SetMetadata(0, "call0", "fn0", null, null), Is.True);
            Assert.That(accumulator.AppendArguments(0, "{\"a\":"), Is.True);
            Assert.That(accumulator.AppendArguments(0, "1}"), Is.True);

            var calls = accumulator.Build();

            Assert.That(calls.Count, Is.EqualTo(1));
            Assert.That(calls[0].CallId, Is.EqualTo("call0"));
            Assert.That(calls[0].FunctionName, Is.EqualTo("fn0"));
            Assert.That(calls[0].ArgumentsJson, Is.EqualTo("{\"a\":1}"));
            Assert.That(calls[0].IsInvalid, Is.False);
            Assert.That(accumulator.AbortedIndex, Is.Null);
        }

        [Test]
        public void Build_InitialArgumentsFromMetadata_AreKept()
        {
            var accumulator = new ToolCallAccumulator();

            accumulator.SetMetadata(0, "call0", "fn0", null, "{\"a\":");
            accumulator.AppendArguments(0, "1}");

            Assert.That(accumulator.Build()[0].ArgumentsJson, Is.EqualTo("{\"a\":1}"));
        }

        [Test]
        public void Build_ArgumentsBeforeMetadata_StillAttached()
        {
            var accumulator = new ToolCallAccumulator();

            accumulator.AppendArguments(0, "{\"a\":1}");
            accumulator.SetMetadata(0, "call0", "fn0", null, null);

            var calls = accumulator.Build();

            Assert.That(calls.Count, Is.EqualTo(1));
            Assert.That(calls[0].ArgumentsJson, Is.EqualTo("{\"a\":1}"));
        }

        [Test]
        public void Build_RepeatedMetadata_LastWins()
        {
            var accumulator = new ToolCallAccumulator();

            accumulator.SetMetadata(0, "call0", "fn0", "{\"x\":\"OLD\"}", null);
            accumulator.SetMetadata(0, "call1", "fn1", "{\"x\":\"NEW\"}", null);

            var call = accumulator.Build()[0];

            Assert.That(call.CallId, Is.EqualTo("call1"));
            Assert.That(call.FunctionName, Is.EqualTo("fn1"));
            Assert.That(call.ExtraContentJson, Is.EqualTo("{\"x\":\"NEW\"}"));
        }

        [Test]
        public void Build_InvalidJson_SanitizedAndMarkedInvalid()
        {
            var accumulator = new ToolCallAccumulator();

            accumulator.SetMetadata(0, "call0", "fn0", null, null);
            accumulator.AppendArguments(0, "{\"a\":");

            var call = accumulator.Build()[0];

            Assert.That(call.IsInvalid, Is.True);
            Assert.That(call.ArgumentsJson, Is.EqualTo("{}"));
        }

        [Test]
        public void Build_NoArguments_KeptEmptyAndValid()
        {
            var accumulator = new ToolCallAccumulator();

            accumulator.SetMetadata(0, "call0", "fn0", null, null);

            var call = accumulator.Build()[0];

            Assert.That(call.IsInvalid, Is.False);
            Assert.That(call.ArgumentsJson, Is.EqualTo(string.Empty));
        }

        [Test]
        public void Build_ArgumentsWithoutMetadata_Dropped()
        {
            var accumulator = new ToolCallAccumulator();

            accumulator.AppendArguments(0, "{\"a\":1}");

            Assert.That(accumulator.Build(), Is.Empty);
        }

        [Test]
        public void Build_OrdersCallsByIndex()
        {
            var accumulator = new ToolCallAccumulator();

            accumulator.SetMetadata(1, "call1", "fn1", null, "{}");
            accumulator.SetMetadata(0, "call0", "fn0", null, "{}");

            var calls = accumulator.Build();

            Assert.That(calls[0].Index, Is.EqualTo(0));
            Assert.That(calls[1].Index, Is.EqualTo(1));
        }

        [Test]
        public void AppendArguments_GuardRejects_StopsAndCallBecomesInvalid()
        {
            var accumulator = new ToolCallAccumulator(new MarkerGuard("BAD"));

            accumulator.SetMetadata(0, "call0", "fn0", null, null);
            Assert.That(accumulator.AppendArguments(0, "{\"a\":\"B"), Is.True);
            Assert.That(accumulator.AppendArguments(0, "AD"), Is.False);

            Assert.That(accumulator.AbortedIndex, Is.EqualTo(0));
            Assert.That(accumulator.AbortReason, Is.EqualTo("marker found"));

            var call = accumulator.Build()[0];
            Assert.That(call.IsInvalid, Is.True);
            Assert.That(call.ArgumentsJson, Is.EqualTo("{}"));
        }

        [Test]
        public void AppendArguments_ValidJsonRejectedByGuard_StillInvalid()
        {
            // A call with valid JSON but leaked tokens (the 10.11 case) must not run.
            var accumulator = new ToolCallAccumulator(new MarkerGuard("BAD"));

            accumulator.SetMetadata(0, "call0", "fn0", null, null);
            accumulator.AppendArguments(0, "{\"text\":\"BAD\"}");

            Assert.That(accumulator.Build()[0].IsInvalid, Is.True);
        }

        [Test]
        public void SetMetadata_GuardRejectsInitialArguments_ReturnsFalse()
        {
            var accumulator = new ToolCallAccumulator(new MarkerGuard("BAD"));

            Assert.That(accumulator.SetMetadata(0, "call0", "fn0", null, "{\"a\":\"BAD\"}"), Is.False);
            Assert.That(accumulator.AbortedIndex, Is.EqualTo(0));
        }

        [Test]
        public void AppendArguments_GuardGetsFunctionNameAndFragmentAsTail()
        {
            var guard = new MarkerGuard("BAD");
            var accumulator = new ToolCallAccumulator(guard);

            accumulator.AppendArguments(0, "{\"a\":");
            accumulator.SetMetadata(0, "call0", "find_files", null, null);
            accumulator.AppendArguments(0, "1}");

            Assert.That(guard.Calls.Count, Is.EqualTo(2));
            Assert.That(guard.Calls[0].FunctionName, Is.Null, "metadata has not arrived yet");
            Assert.That(guard.Calls[1].FunctionName, Is.EqualTo("find_files"));
            Assert.That(guard.Calls[1].Arguments, Is.EqualTo("{\"a\":1}"));
            Assert.That(guard.Calls[1].Fragment, Is.EqualTo("1}"));
        }

        [Test]
        public void Build_OtherCallOfTheStream_StaysValid()
        {
            var accumulator = new ToolCallAccumulator(new MarkerGuard("BAD"));

            accumulator.SetMetadata(0, "call0", "fn0", null, "{\"a\":1}");
            accumulator.SetMetadata(1, "call1", "fn1", null, null);
            accumulator.AppendArguments(1, "{\"b\":\"BAD");

            var calls = accumulator.Build();

            Assert.That(calls[0].IsInvalid, Is.False);
            Assert.That(calls[0].ArgumentsJson, Is.EqualTo("{\"a\":1}"));
            Assert.That(calls[1].IsInvalid, Is.True);
        }

        [Test]
        public void GetArgumentsPreview_CutsLongArguments()
        {
            var accumulator = new ToolCallAccumulator();

            accumulator.AppendArguments(0, "0123456789");

            Assert.That(accumulator.GetArgumentsPreview(0, 4), Is.EqualTo("0123..."));
            Assert.That(accumulator.GetArgumentsPreview(0, 20), Is.EqualTo("0123456789"));
            Assert.That(accumulator.GetArgumentsPreview(5, 4), Is.EqualTo(string.Empty));
        }
    }
}
