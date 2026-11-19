namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations
{
    /// <summary>
    /// Outcome of a single test run: statistics, the text shown to the model and an optional explicit error.
    /// </summary>
    internal sealed class TestRunSummary
    {
        public TestRunSummary(
            bool success,
            string output,
            int total,
            int passed,
            int failed,
            int skipped,
            string errorText)
        {
            Success = success;
            Output = output;
            Total = total;
            Passed = passed;
            Failed = failed;
            Skipped = skipped;
            ErrorText = errorText;
        }

        /// <summary>
        /// True when the run finished and all executed tests passed.
        /// </summary>
        public bool Success { get; }

        /// <summary>
        /// Text returned to the model: summary, failure details or the full log.
        /// </summary>
        public string Output { get; }

        public int Total { get; }

        public int Passed { get; }

        public int Failed { get; }

        public int Skipped { get; }

        /// <summary>
        /// Error text that must reach the model as is; null when the generic message derived from the
        /// statistics is accurate enough.
        /// </summary>
        public string ErrorText { get; }

        /// <summary>
        /// Failure that only carries its output text: the caller derives the error message from the statistics.
        /// </summary>
        public static TestRunSummary Failure(string output)
        {
            return new TestRunSummary(false, output, 0, 0, 0, 0, null);
        }

        /// <summary>
        /// Failure with an explicit error text, for cases where the statistics cannot explain the cause
        /// (for example the run hit its own timeout).
        /// </summary>
        public static TestRunSummary FailureWithError(string output, string errorText)
        {
            return new TestRunSummary(false, output, 0, 0, 0, 0, errorText);
        }
    }
}
