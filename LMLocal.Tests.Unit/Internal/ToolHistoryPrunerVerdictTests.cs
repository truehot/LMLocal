using LMLocal.Application.Chat;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    /// <summary>
    /// Trace verdicts: any success=false result is shown as FAILED with its error message, a successful one as ok.
    /// </summary>
    [TestFixture]
    public class ToolHistoryPrunerVerdictTests
    {
        private const string Args = "{\"file_path\":\"A.cs\",\"start_line\":3}";

        [Test]
        public void SuccessFalse_IsFailedWithErrorMessage()
        {
            string trace = ToolHistoryPruner.DescribeTrace("replace_file_lines", Args,
                "{\"success\":false,\"error_message\":\"No solution is currently open.\"}");

            Assert.That(trace, Does.Contain("FAILED: No solution is currently open."));
        }

        [Test]
        public void MatchFailure_IsFailedWithFilePathInResult()
        {
            string trace = ToolHistoryPruner.DescribeTrace("replace_file_lines", Args,
                "{\"success\":false,\"file_path\":\"A.cs\",\"error_message\":\"Old content not found in file.\",\"lines_replaced\":0}");

            Assert.That(trace, Does.Contain("FAILED: Old content not found in file."));
        }

        [Test]
        public void Success_IsOk()
        {
            string trace = ToolHistoryPruner.DescribeTrace("replace_file_lines", Args,
                "{\"success\":true,\"file_path\":\"A.cs\",\"lines_replaced\":2}");

            Assert.That(trace, Does.Contain("ok,"));
            Assert.That(trace, Does.Not.Contain("FAILED"));
        }
    }
}
