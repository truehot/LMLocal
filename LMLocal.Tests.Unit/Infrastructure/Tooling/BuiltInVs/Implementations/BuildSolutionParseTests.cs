using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Projects;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Implementations
{
    [TestFixture]
    public class BuildSolutionParseTests
    {
        [Test]
        public void ParseBuildOutput_Null_ReturnsEmptyList()
        {
            var result = BuildSolution.ParseBuildOutput(null);

            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void ParseBuildOutput_EmptyString_ReturnsEmptyList()
        {
            var result = BuildSolution.ParseBuildOutput(string.Empty);

            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void ParseBuildOutput_StandardErrorWithLineAndColumn_ParsesFields()
        {
            var result = BuildSolution.ParseBuildOutput("Program.cs(12,34): error CS1234: some text");

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].File, Is.EqualTo("Program.cs"));
            Assert.That(result[0].Line, Is.EqualTo(12));
            Assert.That(result[0].Column, Is.EqualTo(34));
            Assert.That(result[0].Message, Is.EqualTo("some text"));
        }

        [Test]
        public void ParseBuildOutput_WithProjectPrefixAndLineOnly_ColumnIsZero()
        {
            var result = BuildSolution.ParseBuildOutput("1>Program.cs(12): error CS1234: some text");

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].File, Is.EqualTo("Program.cs"));
            Assert.That(result[0].Line, Is.EqualTo(12));
            Assert.That(result[0].Column, Is.EqualTo(0));
            Assert.That(result[0].Message, Is.EqualTo("some text"));
        }

        [Test]
        public void ParseBuildOutput_ErrorWithoutCodeOrFile_MessageParsed()
        {
            var result = BuildSolution.ParseBuildOutput("error : The output path is not set for project 'X'");

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].File, Is.Empty);
            Assert.That(result[0].Message, Is.EqualTo("The output path is not set for project 'X'"));
        }

        [Test]
        public void ParseBuildOutput_ErrorWithSdkProjectSuffix_StripsSuffix()
        {
            var result = BuildSolution.ParseBuildOutput(@"error MSB4018: The task failed unexpectedly [C:\proj\file.csproj]");

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].File, Is.Empty);
            Assert.That(result[0].Message, Is.EqualTo("The task failed unexpectedly"));
        }

        [Test]
        public void ParseBuildOutput_WarningOnly_IsSkipped()
        {
            var result = BuildSolution.ParseBuildOutput("warning CS0219: unused variable");

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void ParseBuildOutput_WarningBeforeError_WarningSkippedErrorExtracted()
        {
            var output = "warning CS0219: unused variable\r\n" +
                         "Program.cs(12,34): error CS1234: real error\r\n";

            var result = BuildSolution.ParseBuildOutput(output);

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].File, Is.EqualTo("Program.cs"));
            Assert.That(result[0].Message, Is.EqualTo("real error"));
        }

        [TestCase("1>------ Build started: Project: X, Configuration: Debug Any CPU ------")]
        [TestCase("========== Build: 1 succeeded, 0 failed, 0 up-to-date, 0 skipped ==========")]
        [TestCase("Build FAILED.")]
        public void ParseBuildOutput_HeaderLines_AreSkipped(string line)
        {
            var result = BuildSolution.ParseBuildOutput(line);

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void ParseBuildOutput_EmptyMessage_IsSkipped()
        {
            var result = BuildSolution.ParseBuildOutput("1>Program.cs(12): error CS1234:  ");

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void ParseBuildOutput_MultipleErrors_AllExtracted()
        {
            var output = "1>a.cs(1): error CS1111: first\r\n" +
                         "1>b.cs(2,3): error CS2222: second\r\n" +
                         "1>c.cs(3): error CS3333: third\r\n";

            var result = BuildSolution.ParseBuildOutput(output);

            Assert.That(result, Has.Count.EqualTo(3));
            Assert.That(result[0].Message, Is.EqualTo("first"));
            Assert.That(result[1].Message, Is.EqualTo("second"));
            Assert.That(result[2].Message, Is.EqualTo("third"));
        }

        [Test]
        public void ParseBuildOutput_FatalErrorCpp_FileEmptyMessageParsed()
        {
            var result = BuildSolution.ParseBuildOutput("fatal error C1083: Cannot open include file: 'foo.h': No such file or directory");

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].File, Is.Empty);
            Assert.That(result[0].Line, Is.EqualTo(0));
            Assert.That(result[0].Message, Is.EqualTo("Cannot open include file: 'foo.h': No such file or directory"));
        }

        [Test]
        public void ParseBuildOutput_ProjectPrefixFatalError_Parsed()
        {
            var result = BuildSolution.ParseBuildOutput("1>fatal error C1083: Cannot open include file: 'foo.h'");

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].File, Is.Empty);
            Assert.That(result[0].Message, Is.EqualTo("Cannot open include file: 'foo.h'"));
        }

        [Test]
        public void ParseBuildOutput_LinkerErrorWithObj_ParsesFile()
        {
            var result = BuildSolution.ParseBuildOutput("foo.obj : error LNK2019: unresolved external symbol _main referenced in function main");

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].File, Is.EqualTo("foo.obj"));
            Assert.That(result[0].Message, Is.EqualTo("unresolved external symbol _main referenced in function main"));
        }

        [Test]
        public void ParseBuildOutput_LinkerFatalError_ParsesFileAndMessage()
        {
            var result = BuildSolution.ParseBuildOutput("LINK : fatal error LNK1104: cannot open file 'libfoo.lib'");

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].File, Is.EqualTo("LINK"));
            Assert.That(result[0].Message, Is.EqualTo("cannot open file 'libfoo.lib'"));
        }

        [Test]
        public void ParseBuildOutput_PathWithParentheses_ParsesFullFile()
        {
            var result = BuildSolution.ParseBuildOutput(@"C:\Program Files (x86)\MyApp\Program.cs(12,34): error CS1234: some text");

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].File, Is.EqualTo(@"C:\Program Files (x86)\MyApp\Program.cs"));
            Assert.That(result[0].Line, Is.EqualTo(12));
            Assert.That(result[0].Column, Is.EqualTo(34));
            Assert.That(result[0].Message, Is.EqualTo("some text"));
        }

        [Test]
        public void TrimToLastBuildSection_CutsPreviousBuildAndSummary()
        {
            var output =
                "1>old.cs(1,1): error CS1002: old\n" +
                "========== Build: 0 succeeded, 1 failed ==========\n" +
                "1>------ Build started: Project: A ------\n" +
                "1>a.cs(5,2): error CS0103: new\n" +
                "========== Build: 0 succeeded, 1 failed, 0 up-to-date, 0 skipped ==========";

            var result = BuildSolution.TrimToLastBuildSection(output);

            // Only the current build's body survives: previous build and both anchors are cut.
            Assert.That(result, Is.EqualTo("1>a.cs(5,2): error CS0103: new"));
        }

        [Test]
        public void TrimToLastBuildSection_NoAnchors_ReturnsInput()
        {
            Assert.That(BuildSolution.TrimToLastBuildSection("just text"), Is.EqualTo("just text"));
            Assert.That(BuildSolution.TrimToLastBuildSection(null), Is.Null);
            Assert.That(BuildSolution.TrimToLastBuildSection(string.Empty), Is.Empty);
        }

        [Test]
        public void TrimToLastBuildSection_NoStartAnchor_KeepsPreSummaryContent()
        {
            // Tail window cut off the "Build started" header: keep everything before the summary.
            var result = BuildSolution.TrimToLastBuildSection(
                "1>a.cs(1,1): error CS1002: x\n" +
                "========== Build: 0 succeeded, 1 failed ==========");

            Assert.That(result, Does.Contain("a.cs"));
            Assert.That(result, Does.Not.Contain("========== Build:"));
        }

        [Test]
        public void IsProjectNameMatch_MatchesByNameUniqueNameAndFileName()
        {
            const string name = "MyApp";
            const string uniqueName = @"MyApp\MyApp.csproj";
            const string fullName = @"C:\src\MyApp\MyApp.csproj";

            Assert.That(ProjectFinder.IsProjectNameMatch(name, uniqueName, fullName, "myapp"), Is.True);
            Assert.That(ProjectFinder.IsProjectNameMatch(name, uniqueName, fullName, "MyApp.csproj"), Is.True);
            Assert.That(ProjectFinder.IsProjectNameMatch(name, uniqueName, fullName, @"myapp/myapp.csproj"), Is.True);
            Assert.That(ProjectFinder.IsProjectNameMatch(name, uniqueName, fullName, "Other"), Is.False);
        }

        [Test]
        public void IsProjectNameMatch_NullFields_DoesNotThrow()
        {
            Assert.That(ProjectFinder.IsProjectNameMatch(null, null, null, "MyApp"), Is.False);
            Assert.That(ProjectFinder.IsProjectNameMatch("MyApp", null, null, "MyApp"), Is.True);
        }

        [Test]
        public void NormalizeProjectName_TrimsQuotesAndNormalizesSlashes()
        {
            Assert.That(ProjectFinder.NormalizeProjectName(@"""MyApp\MyApp.csproj"""), Is.EqualTo(@"MyApp\MyApp.csproj"));
            Assert.That(ProjectFinder.NormalizeProjectName("MyApp/MyApp.csproj"), Is.EqualTo(@"MyApp\MyApp.csproj"));
            Assert.That(ProjectFinder.NormalizeProjectName(null), Is.Null);
        }
    }
}
