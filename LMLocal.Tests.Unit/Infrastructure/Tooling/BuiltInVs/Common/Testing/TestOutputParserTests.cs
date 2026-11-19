using System.Text;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Testing;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Common.Testing
{
    [TestFixture]
    public class TestOutputParserTests
    {
        [Test]
        public void ParseStatisticsUniversal_Empty_ReturnsZeros()
        {
            var (total, passed, failed, skipped) = TestOutputParser.ParseStatisticsUniversal(string.Empty);

            Assert.That(total, Is.EqualTo(0));
            Assert.That(passed, Is.EqualTo(0));
            Assert.That(failed, Is.EqualTo(0));
            Assert.That(skipped, Is.EqualTo(0));
        }

        [Test]
        public void ParseStatisticsUniversal_NUnitSummary_Parses()
        {
            var output = "Passed!  - Failed:     0, Passed:    1082, Skipped:     0, Total:    1082";

            var (total, passed, failed, skipped) = TestOutputParser.ParseStatisticsUniversal(output);

            Assert.That(total, Is.EqualTo(1082));
            Assert.That(passed, Is.EqualTo(1082));
            Assert.That(failed, Is.EqualTo(0));
            Assert.That(skipped, Is.EqualTo(0));
        }

        [Test]
        public void ParseStatisticsUniversal_MSTestSummary_Parses()
        {
            var output = "Passed!  - Failed: 1, Passed: 5, Skipped: 0, Total: 6";

            var (total, passed, failed, skipped) = TestOutputParser.ParseStatisticsUniversal(output);

            Assert.That(total, Is.EqualTo(6));
            Assert.That(passed, Is.EqualTo(5));
            Assert.That(failed, Is.EqualTo(1));
            Assert.That(skipped, Is.EqualTo(0));
        }

        [Test]
        public void ParseStatisticsUniversal_TotalsFallback_WhenTotalMissing()
        {
            var output = "Passed!  - Failed: 2, Passed: 3, Skipped: 1";

            var (total, passed, failed, skipped) = TestOutputParser.ParseStatisticsUniversal(output);

            Assert.That(total, Is.EqualTo(6)); // 3 + 2 + 1
            Assert.That(passed, Is.EqualTo(3));
            Assert.That(failed, Is.EqualTo(2));
            Assert.That(skipped, Is.EqualTo(1));
        }

        [Test]
        public void ParseStatisticsUniversal_BuildHeader_NotCountedAsStatistic()
        {
            var output = "Build FAILED.\r\nTotal tests: 0";

            var (total, passed, failed, skipped) = TestOutputParser.ParseStatisticsUniversal(output);

            Assert.That(total, Is.EqualTo(0));
            Assert.That(passed, Is.EqualTo(0));
            Assert.That(failed, Is.EqualTo(0));
            Assert.That(skipped, Is.EqualTo(0));
        }

        [Test]
        public void ExtractFailedDetails_NullOrEmpty_ReturnsNull()
        {
            Assert.That(TestOutputParser.ExtractFailedDetails(null), Is.Null);
            Assert.That(TestOutputParser.ExtractFailedDetails(string.Empty), Is.Null);
        }

        [Test]
        public void ExtractFailedDetails_NoFailures_ReturnsNull()
        {
            var output = "Passed!  - Failed: 0, Passed: 5, Skipped: 0, Total: 5";

            Assert.That(TestOutputParser.ExtractFailedDetails(output), Is.Null);
        }

        [Test]
        public void ExtractFailedDetails_SingleFailedBlock_ReturnsBlock()
        {
            var output = "  Failed MyTests.One [1 ms]\r\n" +
                         "  Error Message:\r\n" +
                         "   Assert.AreEqual failed.\r\n" +
                         "  Stack Trace:\r\n" +
                         "   at MyTests.One()\r\n" +
                         "Passed!  - Failed: 1, Passed: 0, Skipped: 0, Total: 1";

            var result = TestOutputParser.ExtractFailedDetails(output);

            Assert.That(result, Is.Not.Null);
            Assert.That(result, Does.Contain("Failed MyTests.One"));
            Assert.That(result, Does.Contain("Assert.AreEqual failed"));
        }

        [Test]
        public void ExtractFailedDetails_MultipleFailedBlocks_Joined()
        {
            var output = "  Failed A.Test1\r\n" +
                         "   detail one\r\n" +
                         "  Failed B.Test2\r\n" +
                         "   detail two\r\n" +
                         "Passed!  - Failed: 2, Passed: 0, Skipped: 0, Total: 2";

            var result = TestOutputParser.ExtractFailedDetails(output);

            Assert.That(result, Is.Not.Null);
            Assert.That(result, Does.Contain("A.Test1"));
            Assert.That(result, Does.Contain("B.Test2"));
            Assert.That(result.IndexOf("A.Test1"), Is.LessThan(result.IndexOf("B.Test2")));
        }

        [Test]
        public void ExtractFailedDetails_PassedTestWithFailedInName_IsNotAFailure()
        {
            var output = "  Passed CopyAnswer_SurvivesIdleTransitionAfterFailedSummarize [2 s]\r\n" +
                         "  Passed Menu_ClickTwiceClosesDropdown [953 ms]\r\n" +
                         "Passed!  - Failed: 0, Passed: 2, Skipped: 0, Total: 2";

            Assert.That(TestOutputParser.ExtractFailedDetails(output), Is.Null);
        }

        [Test]
        public void ExtractFailedDetails_ProseStartingWithFailed_IsNotAFailure()
        {
            var output = "Failed to load the test adapter from the probing path.\r\n" +
                         "Passed!  - Failed: 0, Passed: 1, Skipped: 0, Total: 1";

            Assert.That(TestOutputParser.ExtractFailedDetails(output), Is.Null);
        }

        [Test]
        public void ExtractFailedDetails_FailedBlock_StopsAtNextResultLine()
        {
            var output = "  Failed A.Test1 [5 ms]\r\n" +
                         "  Error Message:\r\n" +
                         "   Expected: 1 But was: 2\r\n" +
                         "  Passed B.Test2 [1 s]\r\n" +
                         "  Skipped C.Test3 [< 1 ms]\r\n" +
                         "  Passed D.Test4 [1 s]";

            var result = TestOutputParser.ExtractFailedDetails(output);

            Assert.That(result, Does.Contain("Expected: 1 But was: 2"));
            Assert.That(result, Does.Not.Contain("B.Test2"));
            Assert.That(result, Does.Not.Contain("C.Test3"));
            Assert.That(result, Does.Not.Contain("D.Test4"));
        }

        [Test]
        public void ExtractFailedDetails_FailedTestNameWithSpacesBeforeDuration_IsAFailure()
        {
            var output = "  Failed Ns.Method(\"a b\") [3 ms]\r\n" +
                         "  Error Message:\r\n" +
                         "   boom";

            var result = TestOutputParser.ExtractFailedDetails(output);

            Assert.That(result, Does.Contain("Failed Ns.Method(\"a b\")"));
            Assert.That(result, Does.Contain("boom"));
        }

        [Test]
        public void ExtractFailedDetails_RunSummaryAfterBlock_IsNotPartOfIt()
        {
            var output = "  Failed A.Test1 [1 ms]\r\n" +
                         "  Error Message:\r\n" +
                         "   boom\r\n" +
                         "\r\n" +
                         "Test Run Failed.\r\n" +
                         "Total tests: 2\r\n" +
                         "     Passed: 1\r\n" +
                         "     Failed: 1";

            var result = TestOutputParser.ExtractFailedDetails(output);

            Assert.That(result, Does.Contain("boom"));
            Assert.That(result, Does.Not.Contain("Test Run Failed"));
            Assert.That(result, Does.Not.Contain("Total tests"));
        }

        [Test]
        public void ExtractFailedDetails_TwoFailuresWithLongStacks_KeepBothNamesAndMessages()
        {
            var output = FailedBlock("A.First", "first message", stackLines: 200) +
                         FailedBlock("B.Second", "second message", stackLines: 200);

            var result = TestOutputParser.ExtractFailedDetails(output, maxChars: 6000);

            Assert.That(result.Length, Is.LessThanOrEqualTo(6000));
            Assert.That(result, Does.Contain("Failed A.First"));
            Assert.That(result, Does.Contain("first message"));
            Assert.That(result, Does.Contain("Failed B.Second"));
            Assert.That(result, Does.Contain("second message"));
            Assert.That(result, Does.Contain("[details truncated]"));
        }

        [Test]
        public void ExtractFailedDetails_TooManyFailures_ListsTheRestByName()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 50; i++)
                sb.Append(FailedBlock("Ns.Test" + i, "message " + i, stackLines: 5));

            var result = TestOutputParser.ExtractFailedDetails(sb.ToString(), maxChars: 6000);

            Assert.That(result.Length, Is.LessThanOrEqualTo(6000));
            Assert.That(result, Does.Contain("Failed Ns.Test0 "));
            Assert.That(result, Does.Contain("message 0"));
            Assert.That(result, Does.Match(@"\.\.\. and \d+ more failed: "));
            Assert.That(result, Does.Contain("Ns.Test49"));
        }

        [Test]
        public void ExtractFailedDetails_WithinBudget_IsNotShortened()
        {
            var output = FailedBlock("A.Only", "only message", stackLines: 3);

            var result = TestOutputParser.ExtractFailedDetails(output, maxChars: 6000);

            Assert.That(result, Does.Contain("only message"));
            Assert.That(result, Does.Not.Contain("[details truncated]"));
            Assert.That(result, Does.Not.Contain("more failed"));
        }

        private static string FailedBlock(string name, string message, int stackLines)
        {
            var sb = new StringBuilder();
            sb.Append("  Failed ").Append(name).Append(" [12 ms]\r\n");
            sb.Append("  Error Message:\r\n");
            sb.Append("   ").Append(message).Append("\r\n");
            sb.Append("  Stack Trace:\r\n");
            for (int i = 0; i < stackLines; i++)
                sb.Append("     at ").Append(name).Append(".Frame").Append(i).Append("() in C:\\src\\File.cs:line ").Append(i).Append("\r\n");
            return sb.ToString();
        }

        [Test]
        public void ExtractDiagnosticSummary_EmptyOrWhitespace_ReturnsNull()
        {
            Assert.That(TestOutputParser.ExtractDiagnosticSummary(null), Is.Null);
            Assert.That(TestOutputParser.ExtractDiagnosticSummary("   "), Is.Null);
        }

        [Test]
        public void ExtractDiagnosticSummary_PrefersErrorLines_AndCapsOutput()
        {
            var output = "Build started.\r\n" +
                         "1>Project is building...\r\n" +
                         "Program.cs(12,1): error CS1002: ; expected\r\n" +
                         "Build FAILED.\r\n" +
                         "    1 Error(s)\r\n";

            var result = TestOutputParser.ExtractDiagnosticSummary(output, maxChars: 200);

            Assert.That(result, Is.Not.Null);
            Assert.That(result, Does.Contain("error CS1002"));
            Assert.That(result, Does.Contain("Build FAILED"));
            Assert.That(result, Does.Not.Contain("Project is building"));
        }

        [Test]
        public void ExtractDiagnosticSummary_NoDiagnosticLines_FallsBackToTail()
        {
            var output = "line1\r\nline2\r\nline3\r\nline4\r\nline5";

            var result = TestOutputParser.ExtractDiagnosticSummary(output, maxChars: 1000, tailLineCount: 2);

            Assert.That(result, Is.Not.Null);
            Assert.That(result, Does.Contain("line4"));
            Assert.That(result, Does.Contain("line5"));
            Assert.That(result, Does.Not.Contain("line1"));
        }
    }
}
