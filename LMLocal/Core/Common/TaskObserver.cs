using System.Threading;
using System.Threading.Tasks;

namespace LMLocal.Core.Common
{
    /// <summary>
    /// Fire-and-forget observation for work whose result must not be awaited and whose failure must not escape.
    /// </summary>

    internal static class TaskObserver
    {
        /// <summary>
        /// Observes the task without blocking the caller. A cancelled task is a normal ending, a fault is logged.
        /// </summary>
        public static void Observe(Task task, string context)
        {
            if (task == null)
                return;

            _ = task.ContinueWith(
                observed => LogFault(observed, context),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        /// <summary>Reads the failure so it stops being "unobserved", then logs it.
        private static void LogFault(Task task, string context)
        {
            var failure = task.Exception?.GetBaseException();
            if (failure == null)
                return;

            InternalLogger.Warn((context ?? "Task") + " failed: " + failure.Message);
        }
    }
}
