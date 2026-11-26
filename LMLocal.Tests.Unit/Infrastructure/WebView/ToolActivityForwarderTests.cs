using System.Collections.Generic;
using System.Threading.Tasks;
using LMLocal.Application.Tool;
using LMLocal.Infrastructure.WebView;
using LMLocal.Infrastructure.WebView.Messaging;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.WebView
{
    [TestFixture]
    public class ToolActivityForwarderTests
    {
        private static List<WebView2ToolCallMessage> Forward(params ToolActivityEvent[] events)
        {
            var sent = new List<WebView2ToolCallMessage>();
            var forwarder = new ToolActivityForwarder("call-1", m =>
            {
                sent.Add((WebView2ToolCallMessage)m);
                return Task.CompletedTask;
            });

            foreach (var e in events)
                forwarder.Report(e);

            return sent;
        }

        private static List<WebView2ScriptMessage> ForwardAll(params ToolActivityEvent[] events)
        {
            var sent = new List<WebView2ScriptMessage>();
            var forwarder = new ToolActivityForwarder("call-1", m =>
            {
                sent.Add(m);
                return Task.CompletedTask;
            });

            foreach (var e in events)
                forwarder.Report(e);

            return sent;
        }

        [Test]
        public void Report_ToolNameOnly_FallsBackMessageToToolName()
        {
            var sent = Forward(new ToolActivityEvent { ToolName = "build_solution", Step = 3 });

            Assert.That(sent.Count, Is.EqualTo(1));
            Assert.That(sent[0].Type, Is.EqualTo(WebView2MessageType.StreamToolStep));
            Assert.That(sent[0].CallId, Is.EqualTo("call-1"));
            Assert.That(sent[0].Message, Is.EqualTo("build_solution"));
            Assert.That(sent[0].Step, Is.EqualTo(3));
        }

        [Test]
        public void Report_MessageOnly_SendsCustomStatusWithoutToolName()
        {
            var sent = Forward(new ToolActivityEvent { Message = "thinking", Step = 4 });

            Assert.That(sent.Count, Is.EqualTo(1));
            Assert.That(sent[0].Message, Is.EqualTo("thinking"));
            Assert.That(sent[0].Step, Is.EqualTo(4));
            Assert.That(sent[0].FunctionName, Is.Null.Or.Empty);
        }

        [Test]
        public void Report_MessageOverridesToolName()
        {
            var sent = Forward(new ToolActivityEvent { ToolName = "build_solution", Message = "thinking", Step = 5 });

            Assert.That(sent.Count, Is.EqualTo(1));
            Assert.That(sent[0].Message, Is.EqualTo("thinking"));
            Assert.That(sent[0].FunctionName, Is.EqualTo("build_solution"));
        }

        [Test]
        public void Report_EmptyEvent_IsDropped()
        {
            var sent = Forward(new ToolActivityEvent());

            Assert.That(sent, Is.Empty);
        }

        [Test]
        public void Report_TokenEvent_SendsStreamToolTokensInsteadOfAStep()
        {
            var sent = ForwardAll(new ToolActivityEvent { Tokens = 1520, TokensPerSecond = 95.3, AgentName = "Plan critic" });

            Assert.That(sent.Count, Is.EqualTo(1));

            var tokens = sent[0] as WebView2ToolTokensMessage;
            Assert.That(tokens, Is.Not.Null, "A token event must not become a StreamToolStep, which would rewrite the step line");
            Assert.That(tokens.Type, Is.EqualTo(WebView2MessageType.StreamToolTokens));
            Assert.That(tokens.CallId, Is.EqualTo("call-1"));
            Assert.That(tokens.AgentName, Is.EqualTo("Plan critic"));
            Assert.That(tokens.Tokens, Is.EqualTo(1520));
            Assert.That(tokens.TokensPerSecond, Is.EqualTo(95.3));
        }

        [Test]
        public void Report_TokenEventWithoutSpeed_SendsZeroSpeed()
        {
            var sent = ForwardAll(new ToolActivityEvent { Tokens = 40 });

            var tokens = sent[0] as WebView2ToolTokensMessage;
            Assert.That(tokens, Is.Not.Null);
            Assert.That(tokens.Tokens, Is.EqualTo(40));
            Assert.That(tokens.TokensPerSecond, Is.EqualTo(0));
        }
    }
}
