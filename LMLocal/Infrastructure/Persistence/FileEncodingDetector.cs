using System;
using System.IO;
using System.Text;
using LMLocal.Core.Common;

namespace LMLocal.Infrastructure.Persistence
{
    /// <summary>
    /// How a file's encoding was established. Internal on purpose: no public signature exposes it
    /// (the tools decide the encoding themselves and never accept it as input), and the names are not
    /// part of any JSON contract.
    /// </summary>
    internal enum EncodingSource
    {
        /// <summary>A byte order mark identified the encoding.</summary>
        Bom,

        /// <summary>The probe buffer is valid UTF-8, so the file is treated as UTF-8 without a BOM.</summary>
        Utf8Probe,

        /// <summary>The file was not identified and the system ANSI encoding was used. A guess.</summary>
        Fallback
    }

    /// <summary>
    /// Result of <see cref="FileEncodingDetector.Detect"/>.
    /// </summary>
    internal struct EncodingDetection
    {
        /// <summary>The encoding to decode the file with.</summary>
        public Encoding Encoding;

        /// <summary>Length of the preamble that was skipped. The stream is already positioned after it.</summary>
        public int PreambleLength;

        /// <summary>True when the file actually started with a byte order mark.</summary>
        public bool HasBom;

        /// <summary>How <see cref="Encoding"/> was chosen.</summary>
        public EncodingSource Source;
    }

    /// <summary>
    /// The single place where a file's encoding is decided.
    /// </summary>
    internal static class FileEncodingDetector
    {
        /// <summary>
        /// Largest prefix inspected for the UTF-8 probe.
        /// </summary>
        private const int ProbeSize = 8 * 1024;

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        private static readonly Encoding Utf32Be = new UTF32Encoding(bigEndian: true, byteOrderMark: true);

        /// <summary>
        /// Detects the encoding of the stream's content and leaves the stream positioned after the preamble, ready for a decoder that does not detect byte order marks on its own.
        /// </summary>
        public static EncodingDetection Detect(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanSeek) throw new ArgumentException("Stream must be seekable.", nameof(stream));

            long origin = stream.Position;
            byte[] buffer = new byte[ProbeSize];
            int read = 0;
            bool reachedEnd = false;
            while (read < buffer.Length)
            {
                int chunk = stream.Read(buffer, read, buffer.Length - read);
                if (chunk <= 0)
                {
                    reachedEnd = true;
                    break;
                }
                read += chunk;
            }

            // A short read already means EOF.
            if (!reachedEnd)
                reachedEnd = IsAtEnd(stream, origin, read);

            EncodingDetection detection;

            EncodingDetection? bom = DetectBom(buffer, read);
            if (bom.HasValue)
            {
                InternalLogger.Debug($"Encoding probe: BOM {bom.Value.Encoding.WebName}, {bom.Value.PreambleLength} preamble bytes.");
                detection = bom.Value;
            }
            else if (IsValidUtf8(buffer, read, reachedEnd))
            {
                InternalLogger.Debug($"Encoding probe: UTF-8 without BOM over the first {read} bytes.");
                detection = new EncodingDetection
                {
                    Encoding = Utf8NoBom,
                    PreambleLength = 0,
                    HasBom = false,
                    Source = EncodingSource.Utf8Probe
                };
            }
            else
            {
                InternalLogger.Warn($"Encoding probe could not identify the file as UTF-8; falling back to Encoding.Default ({Encoding.Default.WebName}).");
                detection = new EncodingDetection
                {
                    Encoding = Encoding.Default,
                    PreambleLength = 0,
                    HasBom = false,
                    Source = EncodingSource.Fallback
                };
            }

            stream.Position = origin + detection.PreambleLength;
            return detection;
        }

        private static EncodingDetection? DetectBom(byte[] buffer, int count)
        {
            if (count >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF)
                return Bom(Encoding.UTF8, 3);

            if (count >= 4 && buffer[0] == 0xFF && buffer[1] == 0xFE && buffer[2] == 0x00 && buffer[3] == 0x00)
                return Bom(Encoding.UTF32, 4);

            if (count >= 4 && buffer[0] == 0x00 && buffer[1] == 0x00 && buffer[2] == 0xFE && buffer[3] == 0xFF)
                return Bom(Utf32Be, 4);

            if (count >= 2 && buffer[0] == 0xFF && buffer[1] == 0xFE)
                return Bom(Encoding.Unicode, 2);

            if (count >= 2 && buffer[0] == 0xFE && buffer[1] == 0xFF)
                return Bom(Encoding.BigEndianUnicode, 2);

            return null;
        }

        private static EncodingDetection Bom(Encoding encoding, int preambleLength)
        {
            return new EncodingDetection
            {
                Encoding = encoding,
                PreambleLength = preambleLength,
                HasBom = true,
                Source = EncodingSource.Bom
            };
        }

        private static bool IsValidUtf8(byte[] buffer, int count, bool wholeFile)
        {
            if (count == 0) return true;

            int length = wholeFile ? count : CompletePrefixLength(buffer, count);

            try
            {
                StrictUtf8.GetCharCount(buffer, 0, length);
                return true;
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
        }

        private static bool IsAtEnd(Stream stream, long origin, int read)
        {
            try
            {
                return origin + read >= stream.Length;
            }
            catch (NotSupportedException)
            {
                return false;
            }
        }

        /// <summary>
        /// When the buffer is a prefix of a longer file it almost always ends mid-sequence, which is not a verdict on the encoding.
        /// </summary>
        private static int CompletePrefixLength(byte[] buffer, int count)
        {
            int max = Math.Min(4, count);
            for (int tail = 1; tail <= max; tail++)
            {
                byte b = buffer[count - tail];
                if ((b & 0xC0) == 0x80) continue;

                int needed = 1;
                if ((b & 0xE0) == 0xC0) needed = 2;
                else if ((b & 0xF0) == 0xE0) needed = 3;
                else if ((b & 0xF8) == 0xF0) needed = 4;

                return needed > tail ? count - tail : count;
            }
            return count;
        }
    }
}
