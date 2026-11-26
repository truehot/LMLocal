using System.Threading;

namespace LMLocal.Application.SubAgents
{
    /// <summary>
    /// Shared state of the owner of the cancellation token of a run.
    /// </summary>
    public sealed class SubAgentRunCancellationOwner
    {
        private int _stoppedByCaller;

        /// <summary>
        /// True when the owner cancelled the run on purpose (for example a sibling branch failed), not the user.
        /// </summary>
        public bool StoppedByCaller
        {
            get { return Volatile.Read(ref _stoppedByCaller) == 1; }
        }

        /// <summary>
        /// Marks all runs that share the token as cancelled by their caller. Called before the token is cancelled.
        /// </summary>
        public void MarkStoppedByCaller()
        {
            Interlocked.Exchange(ref _stoppedByCaller, 1);
        }
    }
}
