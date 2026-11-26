using System;
using System.Diagnostics;

namespace LMLocal.Core.Common
{
    /// <summary>
    /// Lightweight internal logger. Calls are conditional on DEBUG so they are omitted in Release builds.
    /// </summary>
    internal interface IInternalLogger
    {
        void Debug(string message);
        void Info(string message);
        void Warn(string message);
        void Warn(string message, Exception ex);
        void Error(string message, Exception ex = null);
    }

    /// <summary>Writes to Debug output; errors also go to the daily <see cref="ErrorLogFile"/>.</summary>
    internal class DebugLogger : IInternalLogger
    {
        public void Debug(string message) => Write("DEBUG", message);
        public void Info(string message) => Write("INFO", message);
        public void Warn(string message) => Write("WARN", message);
        public void Warn(string message, Exception ex) => Write("WARN", message, ex);

        public void Error(string message, Exception ex = null)
        {
            Write("ERROR", message, ex);
            ErrorLogFile.Write(message, ex);
        }

        private static void Write(string level, string message) => Write(level, message, null);

        private static void Write(string level, string message, Exception ex)
        {
            string safeMessage = message ?? "";
            if (ex != null)
                safeMessage += $" | {ex}";

            string line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {safeMessage}";
            System.Diagnostics.Debug.WriteLine(line);
        }
    }

    /// <summary>
    /// Static entry point for logging. All methods are removed in Release builds ([Conditional("DEBUG")]).
    /// </summary>
    internal static class InternalLogger
    {
        private static IInternalLogger _instance = new DebugLogger();
        private static readonly object _lock = new object();

        public static void SetLogger(IInternalLogger logger)
        {
            lock (_lock)
                _instance = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        [Conditional("DEBUG")]
        public static void Debug(string message) => SafeLog(l => l.Debug(message));

        [Conditional("DEBUG")]
        public static void Info(string message) => SafeLog(l => l.Info(message));

        [Conditional("DEBUG")]
        public static void Warn(string message) => SafeLog(l => l.Warn(message));

        [Conditional("DEBUG")]
        public static void Warn(string message, Exception ex) => SafeLog(l => l.Warn(message, ex));

        [Conditional("DEBUG")]
        public static void Error(string message, Exception ex = null) => SafeLog(l => l.Error(message, ex));

        private static void SafeLog(Action<IInternalLogger> action)
        {
            try
            {
                action(_instance);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[InternalLogger] {ex}");
            }
        }
    }
}
