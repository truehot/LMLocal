using System;
using System.IO;
using System.Text;
using LMLocal.Infrastructure.Persistence;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    /// <summary>
    /// Unit coverage for <see cref="FileEncodingDetector"/> — the single place where a file's encoding
    /// is decided. These cases used to live in <see cref="DefaultFileSystemTests"/> and went through the
    /// removed public IFileSystem.DetectEncoding; they now assert the detector's own contract, including
    /// the stream position it guarantees to callers.
    /// </summary>
    [TestFixture]
    public class FileEncodingDetectorTests
    {
        private static MemoryStream Stream(byte[] bytes) => new MemoryStream(bytes);

        [Test]
        public void Detect_Utf8Bom_ReturnsBomSourceAndPreambleLength()
        {
            var bytes = Concat(Encoding.UTF8.GetPreamble(), Encoding.UTF8.GetBytes("alpha"));

            var detection = FileEncodingDetector.Detect(Stream(bytes));

            Assert.That(detection.Source, Is.EqualTo(EncodingSource.Bom));
            Assert.That(detection.HasBom, Is.True);
            Assert.That(detection.Encoding.WebName, Is.EqualTo("utf-8"));
            Assert.That(detection.PreambleLength, Is.EqualTo(3));
        }

        [Test]
        public void Detect_Utf16LeBom_ReturnsUtf16()
        {
            var bytes = Concat(Encoding.Unicode.GetPreamble(), Encoding.Unicode.GetBytes("alpha"));

            var detection = FileEncodingDetector.Detect(Stream(bytes));

            Assert.That(detection.Source, Is.EqualTo(EncodingSource.Bom));
            Assert.That(detection.Encoding.WebName, Is.EqualTo("utf-16"));
            Assert.That(detection.PreambleLength, Is.EqualTo(2));
        }

        [Test]
        public void Detect_Utf32BeBom_ReturnsUtf32BigEndian()
        {
            var encoding = new UTF32Encoding(bigEndian: true, byteOrderMark: true);
            var bytes = Concat(encoding.GetPreamble(), encoding.GetBytes("alpha"));

            var detection = FileEncodingDetector.Detect(Stream(bytes));

            Assert.That(detection.Source, Is.EqualTo(EncodingSource.Bom));
            Assert.That(detection.Encoding.WebName, Is.EqualTo("utf-32BE"));
            Assert.That(detection.PreambleLength, Is.EqualTo(4));
        }

        [Test]
        public void Detect_MultibyteCharAtFourthByte_IsUtf8()
        {
            // E2: the old four-byte probe ended inside the
            // three-byte sequence of '→' and classified a valid UTF-8 file as the system ANSI codepage.
            // The 8 KB probe validates the whole buffer, so the offset of the sequence no longer matters.
            var shortSequence = FileEncodingDetector.Detect(Stream(Encoding.UTF8.GetBytes("abc→\n")));
            var longSequence = FileEncodingDetector.Detect(Stream(Encoding.UTF8.GetBytes("abcd→\n")));

            Assert.That(shortSequence.Source, Is.EqualTo(EncodingSource.Utf8Probe));
            Assert.That(shortSequence.Encoding.WebName, Is.EqualTo("utf-8"));
            Assert.That(longSequence.Source, Is.EqualTo(EncodingSource.Utf8Probe));
            Assert.That(longSequence.Encoding.WebName, Is.EqualTo("utf-8"));
        }

        [Test]
        public void Detect_EmptyStream_IsUtf8()
        {
            var detection = FileEncodingDetector.Detect(Stream(Array.Empty<byte>()));

            Assert.That(detection.Source, Is.EqualTo(EncodingSource.Utf8Probe));
            Assert.That(detection.HasBom, Is.False);
            Assert.That(detection.PreambleLength, Is.Zero);
        }

        [Test]
        public void Detect_OnlyBom_IsBomWithPreambleLength()
        {
            var detection = FileEncodingDetector.Detect(Stream(Encoding.UTF8.GetPreamble()));

            Assert.That(detection.Source, Is.EqualTo(EncodingSource.Bom));
            Assert.That(detection.HasBom, Is.True);
            Assert.That(detection.PreambleLength, Is.EqualTo(3));
        }

        [Test]
        public void Detect_LeavesStreamRightAfterThePreamble()
        {
            var bytes = Concat(Encoding.UTF8.GetPreamble(), Encoding.UTF8.GetBytes("alpha"));
            var stream = Stream(bytes);

            var detection = FileEncodingDetector.Detect(stream);

            Assert.That(stream.Position, Is.EqualTo(detection.PreambleLength),
                "Callers decode from the current position, so the detector must not leave the stream elsewhere.");
            using (var reader = new StreamReader(stream, detection.Encoding, detectEncodingFromByteOrderMarks: false))
                Assert.That(reader.ReadToEnd(), Is.EqualTo("alpha"));
        }

        [Test]
        public void Detect_RespectsTheStartingPositionOfTheStream()
        {
            // The detector works relative to the current position, not to the absolute start of the
            // stream. A non-zero origin used to be a trap: the caller's absolute seek would land wrong.
            var prefix = Encoding.UTF8.GetBytes("SKIP");
            var content = Encoding.UTF8.GetBytes("alpha");
            var stream = Stream(Concat(prefix, content));
            stream.Position = prefix.Length;

            var detection = FileEncodingDetector.Detect(stream);

            Assert.That(stream.Position, Is.EqualTo(prefix.Length + detection.PreambleLength));
            using (var reader = new StreamReader(stream, detection.Encoding, detectEncodingFromByteOrderMarks: false))
                Assert.That(reader.ReadToEnd(), Is.EqualTo("alpha"));
        }

        [Test]
        public void Detect_WholeFileWithTruncatedTail_FallsBack()
        {
            if (Encoding.Default.WebName == "utf-8")
            {
                Assert.Ignore("System default encoding is UTF-8; the ANSI fallback branch is unreachable here.");
                return;
            }

            // The buffer is the whole file, so the trailing 0xC2 is a real error, not a sequence that
            // continues past the probe. This is the CP1251-style short file that used to look like UTF-8.
            var detection = FileEncodingDetector.Detect(Stream(new byte[] { 0xC2, 0x80, 0xC2 }));

            Assert.That(detection.Source, Is.EqualTo(EncodingSource.Fallback));
            Assert.That(detection.Encoding.WebName, Is.EqualTo(Encoding.Default.WebName));
            Assert.That(detection.HasBom, Is.False);
        }

        [Test]
        public void Detect_LongFileEndingInsideSequence_TrimsTheIncompleteTail()
        {
            // A buffer that is a prefix of a longer file almost always ends mid-sequence; that is not
            // a verdict on the encoding, so the incomplete tail is dropped before validation.
            var bytes = new byte[8 * 1024 + 2];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)'a';
            bytes[8 * 1024] = 0xE2;   // start of a three-byte sequence that runs past the probe window
            bytes[8 * 1024 + 1] = 0x86;

            var detection = FileEncodingDetector.Detect(Stream(bytes));

            Assert.That(detection.Source, Is.EqualTo(EncodingSource.Utf8Probe),
                "A truncated tail at the end of a longer file must not be treated as an encoding error.");
        }

        [Test]
        public void Detect_NonSeekableStream_Throws()
        {
            using (var stream = new NonSeekableStream())
                Assert.Throws<ArgumentException>(() => FileEncodingDetector.Detect(stream));
        }

        private static byte[] Concat(byte[] first, byte[] second)
        {
            var result = new byte[first.Length + second.Length];
            Buffer.BlockCopy(first, 0, result, 0, first.Length);
            Buffer.BlockCopy(second, 0, result, first.Length, second.Length);
            return result;
        }

        private sealed class NonSeekableStream : Stream
        {
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) => 0;
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
