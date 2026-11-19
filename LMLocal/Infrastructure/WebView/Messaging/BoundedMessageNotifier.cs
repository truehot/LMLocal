using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Common;

namespace LMLocal.Infrastructure.WebView.Messaging
{
    /// <summary>
    /// Fire-and-forget delivery channel for low-volume notifications that must never stall generation.
    /// </summary>
    internal sealed class BoundedMessageNotifier
    {
        /// <summary>Two outstanding notifications is all a single wait can produce (Begin then End).</summary>
        private const int Capacity = 2;

        private static readonly TimeSpan PostTimeout = TimeSpan.FromMilliseconds(1500);

        private readonly Func<WebView2ScriptMessage, Task> _post;
        private readonly BlockingCollection<WebView2ScriptMessage> _queue =
            new BlockingCollection<WebView2ScriptMessage>(new ConcurrentQueue<WebView2ScriptMessage>(), Capacity);
        private readonly CancellationTokenSource _consumerCts = new CancellationTokenSource();

        private int _completed;

        public BoundedMessageNotifier(Func<WebView2ScriptMessage, Task> post)
        {
            _post = post ?? throw new ArgumentNullException(nameof(post));
        }

        /// <summary>
        /// Runs the delivery loop until <see cref="Complete"/> has been called and the queue is empty.
        /// </summary>
        public async Task RunAsync()
        {
            try
            {
                foreach (var message in _queue.GetConsumingEnumerable(_consumerCts.Token))
                {
                    await PostWithTimeoutAsync(message, _consumerCts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                _queue.Dispose();
                _consumerCts.Dispose();
            }
        }

        /// <summary>
        /// Enqueues a notification.
        /// </summary>
        public void Notify(WebView2ScriptMessage message)
        {
            if (message == null)
                return;

            if (Volatile.Read(ref _completed) != 0)
                return;

            try
            {
                if (!_queue.TryAdd(message))
                {
                    InternalLogger.Warn("BoundedMessageNotifier: queue is full, dropping " + message.Type);
                }
            }
            catch (InvalidOperationException)
            {
                // Complete() ran concurrently (possibly after the queue was disposed): the session is over, so there
                // is nothing left to deliver.
            }
        }

        /// <summary>Signals the end of the session; queued notifications are still drained by the consumer.</summary>
        public void Complete()
        {
            if (Interlocked.Exchange(ref _completed, 1) != 0)
                return;

            try
            {
                _queue.CompleteAdding();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private async Task PostWithTimeoutAsync(WebView2ScriptMessage message, CancellationToken cancellationToken)
        {
            Task posted;
            try
            {
                posted = _post(message);
            }
            catch (Exception ex)
            {
                InternalLogger.Warn("BoundedMessageNotifier: post threw for " + message.Type + ": " + ex.Message);
                return;
            }

            if (posted == null)
                return;

            TaskObserver.Observe(posted, "BoundedMessageNotifier: post " + message.Type);

            Task finished = await Task.WhenAny(posted, Task.Delay(PostTimeout, cancellationToken)).ConfigureAwait(false);

            if (finished != posted && !cancellationToken.IsCancellationRequested)
            {
                InternalLogger.Warn("BoundedMessageNotifier: post timed out for " + message.Type);
            }
        }
    }
}
