using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace LMLocal.Infrastructure.Streaming
{
    /// <summary>
    /// Wraps a stream and invokes an activity callback whenever data is actually read.
    /// </summary>
    internal sealed class ActivityReportingStream : Stream
    {
        private readonly Stream _inner;
        private readonly Action _onActivity;

        public ActivityReportingStream(Stream inner, Action onActivity)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _onActivity = onActivity ?? throw new ArgumentNullException(nameof(onActivity));
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override bool CanTimeout => _inner.CanTimeout;
        public override long Length => _inner.Length;
        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override int ReadTimeout
        {
            get => _inner.ReadTimeout;
            set => _inner.ReadTimeout = value;
        }

        public override int WriteTimeout
        {
            get => _inner.WriteTimeout;
            set => _inner.WriteTimeout = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int bytesRead = _inner.Read(buffer, offset, count);
            if (bytesRead > 0)
            {
                _onActivity();
            }
            return bytesRead;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            int bytesRead = await _inner.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
            if (bytesRead > 0)
            {
                _onActivity();
            }
            return bytesRead;
        }

        public override void Flush() => _inner.Flush();

        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);

        public override void SetLength(long value) => _inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await _inner.WriteAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
