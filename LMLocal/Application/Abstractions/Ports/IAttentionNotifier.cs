namespace LMLocal.Application.Abstractions.Ports
{
    /// <summary>
    /// Draws the user's attention to Visual Studio when long-running background work finishes.
    /// </summary>
    internal interface IAttentionNotifier
    {
        /// <summary>
        /// Requests the user's attention. Safe to call from any thread; never throws.
        /// </summary>
        void RequestAttention();
    }
}
