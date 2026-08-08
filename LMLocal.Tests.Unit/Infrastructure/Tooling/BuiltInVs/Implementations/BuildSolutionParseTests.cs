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
    }
}
