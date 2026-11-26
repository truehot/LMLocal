using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using LMLocal.Core.Common;

namespace LMLocal.Infrastructure.HttpWrapper
{
    /// <summary>Daily HTTP log of a Debug build (Logs\http-YYYY-MM-DD.log): one line per request, never bodies, key headers or the query string.</summary>
    internal static class HttpLogFile
    {
        private const string FileName = "http";

        /// <summary>Appends the line of one request: its response, or the exception that ended it.</summary>
        [Conditional("DEBUG")]
        public static void Write(HttpRequestMessage request, HttpResponseMessage response, TimeSpan elapsed, Exception failure)
        {
            try
            {
                DateTime now = DateTime.Now;
                DailyLogFile.Append(FileName, now, Format(now, request, response, elapsed, failure) + Environment.NewLine);
            }
            catch (Exception formatFailure)
            {
                System.Diagnostics.Debug.WriteLine($"[InternalLogger] Cannot format the HTTP log line: {formatFailure.Message}");
            }
        }

        /// <summary>"[time] [tid:N] METHOD scheme://host/path STATUS Nms", plus Retry-After and *ratelimit* headers for 429 and 5xx.</summary>
        internal static string Format(DateTime now, HttpRequestMessage request, HttpResponseMessage response, TimeSpan elapsed, Exception failure)
        {
            var line = new StringBuilder(160);
            line.Append(DailyLogFile.Timestamp(now))
                .Append(" [tid:").Append(Thread.CurrentThread.ManagedThreadId).Append("] ")
                .Append(request?.Method?.Method ?? "?").Append(' ')
                .Append(Address(request?.RequestUri)).Append(' ');

            if (failure is OperationCanceledException)
                line.Append("CANCELLED");
            else if (failure != null)
                line.Append("FAILED");
            else if (response != null)
                line.Append((int)response.StatusCode);
            else
                line.Append("NO-RESPONSE");

            line.Append(' ').Append((long)elapsed.TotalMilliseconds).Append("ms");

            if (failure != null && !(failure is OperationCanceledException))
                line.Append(' ').Append(failure.GetType().Name).Append(": ").Append(OneLine(failure.Message));

            if (response != null && ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500))
                AppendLimitHeaders(line, response);

            return line.ToString();
        }

        /// <summary>Scheme, host, port and path: no user info, no query (some providers put the key there), no fragment.</summary>
        private static string Address(Uri uri)
        {
            if (uri == null)
                return "?";

            if (!uri.IsAbsoluteUri)
                return uri.OriginalString.Split('?', '#')[0];

            return uri.Scheme + "://" + uri.Authority + uri.AbsolutePath;
        }

        private static void AppendLimitHeaders(StringBuilder line, HttpResponseMessage response)
        {
            if (response.Headers.RetryAfter != null)
                line.Append(" Retry-After=").Append(response.Headers.RetryAfter);

            IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers = response.Headers;
            if (response.Content != null)
                headers = headers.Concat(response.Content.Headers);

            foreach (var header in headers.Where(h => h.Key.IndexOf("ratelimit", StringComparison.OrdinalIgnoreCase) >= 0))
                line.Append(' ').Append(header.Key).Append('=').Append(string.Join(",", header.Value));
        }

        private static string OneLine(string text)
        {
            return (text ?? string.Empty).Replace("\r", " ").Replace("\n", " ");
        }
    }
}
