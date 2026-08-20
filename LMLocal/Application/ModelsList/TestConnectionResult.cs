namespace LMLocal.Application.ModelsList
{
    /// <summary>
    /// Result of a Test Connection probe against a provider backend.
    /// </summary>
    internal sealed class TestConnectionResult
    {
        public bool Success { get; set; }
        public string Error { get; set; }

        public static TestConnectionResult Ok() => new TestConnectionResult { Success = true };
        public static TestConnectionResult Fail(string error) => new TestConnectionResult { Success = false, Error = error };
    }
}
