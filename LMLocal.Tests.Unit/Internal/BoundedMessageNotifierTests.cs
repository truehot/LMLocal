using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using LMLocal.Infrastructure.WebView;
using LMLocal.Infrastructure.WebView.Messaging;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Internal
{
    /// <summary>
    /// Guarantees of <see cref="BoundedMessageNotifier"/>: it must deliver in order, and it must never block or throw
    /// on the producer side, because the producer is the retry loop of a generation round.
    /// </summary>
    [TestFixture]
    public class BoundedMessageNotifierTests
    {
        private static WebView2ScriptMessage Message(string payload)
        {
            return new WebView2ScriptMessage { Type = WebView2MessageType.RateLimitWait, Payload = payload };
        }

        /// <summary>Starts the loop the same way the orchestrator does: as background work of the caller.</summary>
        private static Task StartLoop(BoundedMessageNotifier notifier)
        {
            return Task.Run(() => notifier.RunAsync());
        }

        private static async Task<bool> CompletesWithinAsync(Task task, int milliseconds)
        {
            return await Task.WhenAny(task, Task.Delay(milliseconds)).ConfigureAwait(false) == task;
        }

        [Test]
        public async Task Run_DeliversEveryMessageInOrder()
        {
            var delivered = new List<string>();
            var notifier = new BoundedMessageNotifier(message =>
            {
                lock (delivered)
                {
                    delivered.Add(message.Payload);
                }

                return Task.CompletedTask;
            });

            var delivery = StartLoop(notifier);
            notifier.Notify(Message("a"));
            notifier.Notify(Message("b"));
            notifier.Complete();

            Assert.That(await CompletesWithinAsync(delivery, 5000), Is.True);
            Assert.That(delivered, Is.EqualTo(new[] { "a", "b" }));
        }

        [Test]
        public async Task Notify_HungPost_DoesNotBlockTheProducer()
        {
            var neverCompletes = new TaskCompletionSource<bool>();
            var notifier = new BoundedMessageNotifier(message => neverCompletes.Task);
            var delivery = StartLoop(notifier);

            var producer = Task.Run(() =>
            {
                notifier.Notify(Message("a"));
                notifier.Notify(Message("b"));
            });

            Assert.That(await CompletesWithinAsync(producer, 2000), Is.True,
                "Notify must enqueue and return, never wait for the post");

            notifier.Complete();

            Assert.That(await CompletesWithinAsync(delivery, 8000), Is.True,
                "the consumer must give up on a hung post instead of hanging the session");
        }

        [Test]
        public async Task Notify_ThrowingPost_KeepsTheConsumerAlive()
        {
            var delivered = new List<string>();
            var notifier = new BoundedMessageNotifier(message =>
            {
                if (message.Payload == "boom")
                    throw new InvalidOperationException("WebView is gone");

                lock (delivered)
                {
                    delivered.Add(message.Payload);
                }

                return Task.CompletedTask;
            });

            var delivery = StartLoop(notifier);
            notifier.Notify(Message("boom"));
            notifier.Notify(Message("after"));
            notifier.Complete();

            Assert.That(await CompletesWithinAsync(delivery, 5000), Is.True);
            Assert.That(delivered, Is.EqualTo(new[] { "after" }), "a failed post must not stop later notifications");
        }

        [Test]
        public async Task Run_HandsTheMessageToThePost()
        {
            WebView2ScriptMessage received = null;
            var notifier = new BoundedMessageNotifier(message =>
            {
                received = message;
                return Task.CompletedTask;
            });

            var delivery = StartLoop(notifier);
            notifier.Notify(Message("payload"));
            notifier.Complete();

            Assert.That(await CompletesWithinAsync(delivery, 5000), Is.True);
            Assert.That(received, Is.Not.Null);
            Assert.That(received.Payload, Is.EqualTo("payload"));
        }

        [Test]
        public async Task Notify_AfterComplete_IsIgnored()
        {
            var delivered = new List<string>();
            var notifier = new BoundedMessageNotifier(message =>
            {
                lock (delivered)
                {
                    delivered.Add(message.Payload);
                }

                return Task.CompletedTask;
            });

            var delivery = StartLoop(notifier);
            notifier.Complete();

            Assert.DoesNotThrow(() => notifier.Notify(Message("late")),
                "a notification after shutdown must never throw at the producer");
            Assert.That(await CompletesWithinAsync(delivery, 5000), Is.True);
            Assert.That(delivered, Is.Empty);
        }

        [Test]
        public void Complete_CalledTwice_IsHarmless()
        {
            var notifier = new BoundedMessageNotifier(message => Task.CompletedTask);

            notifier.Complete();

            Assert.DoesNotThrow(() => notifier.Complete());
        }

        [Test]
        public void Notify_NullMessage_IsIgnored()
        {
            var notifier = new BoundedMessageNotifier(message => Task.CompletedTask);

            Assert.DoesNotThrow(() => notifier.Notify(null));
        }

        [Test]
        public void Constructor_WithoutPost_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new BoundedMessageNotifier(null));
        }
    }
}
