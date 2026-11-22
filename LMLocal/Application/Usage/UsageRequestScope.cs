using System.Threading;

namespace LMLocal.Application.Usage
{
    /// <summary>
    /// Carries the context of the current chat request down to the work it starts.
    /// </summary>
    internal static class UsageRequestScope
    {
        private static readonly AsyncLocal<UsageCallContext> Context = new AsyncLocal<UsageCallContext>();

        /// <summary>Context of the request that is running in this flow, or null outside a chat request.</summary>
        internal static UsageCallContext Current
        {
            get { return Context.Value; }
        }

        /// <summary>
        /// Opens the scope; dispose the result to restore the previous context.
        /// </summary>
        internal static Scope Begin(UsageCallContext context)
        {
            Scope scope = new Scope(Context.Value);
            Context.Value = context;

            return scope;
        }

        /// <summary>
        /// Restores the context that was current before <see cref="Begin"/>.
        /// </summary>
        internal sealed class Scope : System.IDisposable
        {
            private readonly UsageCallContext _previous;

            internal Scope(UsageCallContext previous)
            {
                _previous = previous;
            }

            public void Dispose()
            {
                Context.Value = _previous;
            }
        }
    }
}
