using System;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace LMLocal.Core.Common
{
    /// <summary>Daily error file of a Debug build: Logs\lmlocal-YYYY-MM-DD.log (see <see cref="DailyLogFile"/>).</summary>
    internal static class ErrorLogFile
    {
        private const string FileName = "lmlocal";

        /// <summary>Appends one record (time, level, thread, message, ex.ToString()).</summary>
        [Conditional("DEBUG")]
        public static void Write(string message, Exception ex)
        {
            DateTime now = DateTime.Now;
            DailyLogFile.Append(FileName, now, Format(now, message, ex));
        }

        /// <summary>Header line, exception with its stacks, empty line.</summary>
        private static string Format(DateTime now, string message, Exception ex)
        {
            var record = new StringBuilder(256);
            record.Append(DailyLogFile.Timestamp(now))
                  .Append(" [ERROR] [tid:")
                  .Append(Thread.CurrentThread.ManagedThreadId)
                  .Append("] ")
                  .Append(message ?? string.Empty)
                  .AppendLine();

            if (ex != null)
                record.Append(ex.ToString()).AppendLine();

            return record.AppendLine().ToString();
        }
    }
}
