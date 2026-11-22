namespace LMLocal.Core.Models
{
    /// <summary>
    /// One level of the exception chain of a debugger stop: type and message only, no stack trace.
    /// </summary>
    public sealed class DebugExceptionInfo
    {
        /// <summary>Full type name, for example <c>System.NullReferenceException</c>. Null when unknown.</summary>
        public string Type { get; set; }

        /// <summary>Message of the exception. Null when the debugger did not expose one.</summary>
        public string Message { get; set; }
    }
}
