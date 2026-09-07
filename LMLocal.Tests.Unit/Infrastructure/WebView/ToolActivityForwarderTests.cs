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
    }
}
