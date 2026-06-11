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
        void Error(string message, Exception ex = null);
    }

    /// <summary>
    /// Dummy logger that discards all messages. Useful for tests or when logging must be disabled.
    /// </summary>

    internal class DummyLogger : IInternalLogger
    {
        public void Debug(string message) { }

        public void Info(string message) { }

        public void Warn(string message) { }

        public void Error(string message, Exception ex = null) { }
    }

    /// <summary>
    /// Writes log messages directly to Debug output (synchronous, no buffering).
    /// </summary>
    internal class DebugLogger : IInternalLogger
    {
        public void Debug(string message) => Write("DEBUG", message);
        public void Info(string message) => Write("INFO", message);
        public void Warn(string message) => Write("WARN", message);
        public void Error(string message, Exception ex = null)
        {
            string errorMsg = message;
            if (ex != null)
                errorMsg += $" | {ex}";
            Write("ERROR", errorMsg);
        }

        private void Write(string level, string message)
        {
            string safeMessage = message ?? "";
            string line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {safeMessage}";
            System.Diagnostics.Debug.WriteLine(line);
        }
    }

    /// <summary>
    /// Static entry point for logging. All methods are removed in Release builds ([Conditional("DEBUG")]).
    /// </summary>
    internal static class InternalLogger
    {
#if DEBUG
        private static IInternalLogger _instance = new DebugLogger();
#else
        private static IInternalLogger _instance = new DummyLogger();
#endif
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
        public static void Error(string message, Exception ex = null) => SafeLog(l => l.Error(message, ex));

        private static void SafeLog(Action<IInternalLogger> action)
        {
            try
            {
                action(_instance);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[InternalLogger] {ex.Message}");
            }
        }
    }
}
