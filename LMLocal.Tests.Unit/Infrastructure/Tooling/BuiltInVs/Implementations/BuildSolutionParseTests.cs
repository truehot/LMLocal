using System.Collections.Generic;
using System.Linq;
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
        public void ParseBuildOutput_FourNumberLocation_ParsesFileLineAndColumn()
        {
            var result = BuildSolution.ParseBuildOutput(@"1>D:\S\A.cs(86,62,86,68): error CS0103: text");

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].File, Is.EqualTo(@"D:\S\A.cs"));
            Assert.That(result[0].Line, Is.EqualTo(86));
            Assert.That(result[0].Column, Is.EqualTo(62));
            Assert.That(result[0].Message, Is.EqualTo("text"));
        }

        [Test]
        public void ParseBuildOutput_FourNumberLocationInPathWithParentheses_ParsesFileWithoutCoordinates()
        {
            var result = BuildSolution.ParseBuildOutput(@"C:\Program Files (x86)\MyApp\S.cs(86,62,86,68): error CS0103: text");

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].File, Is.EqualTo(@"C:\Program Files (x86)\MyApp\S.cs"));
            Assert.That(result[0].Line, Is.EqualTo(86));
            Assert.That(result[0].Column, Is.EqualTo(62));
        }

        [Test]
        public void ParseBuildOutput_CompilerCode_IsReportedSeparately()
        {
            var result = BuildSolution.ParseBuildOutput(@"D:\S\A.cs(86,62,86,68): error CS0103: text");

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].Code, Is.EqualTo("CS0103"));
            Assert.That(result[0].Message, Is.EqualTo("text"));
        }

        [Test]
        public void ParseBuildOutput_MsbCode_IsReportedAndProjectSuffixRemoved()
        {
            var result = BuildSolution.ParseBuildOutput(@"error MSB3027: Could not copy ""a.dll"" [C:\p\App.csproj]");

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].Code, Is.EqualTo("MSB3027"));
            Assert.That(result[0].Message, Is.EqualTo(@"Could not copy ""a.dll"""));
        }

        [Test]
        public void ParseBuildOutput_ErrorWithoutCode_CodeIsNull()
        {
            var result = BuildSolution.ParseBuildOutput("error : The output path is not set for project 'X'");

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].Code, Is.Null);
        }

        [Test]
        public void ParseBuildOutput_BracketsInsideMessage_AreKept()
        {
            // The fragment " []" is part of the C# error text, not a project suffix.
            var result = BuildSolution.ParseBuildOutput(
                "P.cs(1,2): error CS0021: Cannot apply indexing with [] to an expression of type 'int'");

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].Code, Is.EqualTo("CS0021"));
            Assert.That(result[0].Message,
                Is.EqualTo("Cannot apply indexing with [] to an expression of type 'int'"));
        }

        [Test]
        public void ParseBuildOutput_BracketedTextAtEndWithoutPath_IsKept()
        {
            var result = BuildSolution.ParseBuildOutput("P.cs(1,2): error CS1002: expected [x]");

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].Message, Is.EqualTo("expected [x]"));
        }

        [Test]
        public void ParseBuildOutput_ToolNameAsFile_KeepsToolNameAndCode()
        {
            // "CSC" is a tool name, not a path: it cannot be matched to a project directory.
            var result = BuildSolution.ParseBuildOutput("1>CSC : error CS2001: Source file 'Foo.cs' could not be found.");

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].File, Is.EqualTo("CSC"));
            Assert.That(result[0].Code, Is.EqualTo("CS2001"));
            Assert.That(result[0].Message, Is.EqualTo("Source file 'Foo.cs' could not be found."));
        }

        [TestCase(@"text [C:\src\App\App.csproj]", "text")]
        [TestCase(@"text [C:\src\Native\Native.vcxproj]", "text")]
        [TestCase("text [App.csproj]", "text")]
        [TestCase("text [x]", "text [x]")]
        [TestCase("text [docs/readme]", "text [docs/readme]")]
        [TestCase(@"text [C:\src\file.cs]", @"text [C:\src\file.cs]")]
        [TestCase("expected [x]", "expected [x]")]
        [TestCase("Cannot apply indexing with [] to an expression", "Cannot apply indexing with [] to an expression")]
        [TestCase("no suffix", "no suffix")]
        [TestCase(@"unbalanced [C:\src\App.csproj", @"unbalanced [C:\src\App.csproj")]
        public void StripProjectSuffix_RemovesOnlyProjectSuffix(string message, string expected)
        {
            Assert.That(BuildSolution.StripProjectSuffix(message), Is.EqualTo(expected));
        }

        [Test]
        public void StripProjectSuffix_NullOrEmpty_ReturnsInput()
        {
            Assert.That(BuildSolution.StripProjectSuffix(null), Is.Null);
            Assert.That(BuildSolution.StripProjectSuffix(string.Empty), Is.Empty);
        }

        [Test]
        public void ParseBuildOutput_RealTwoProjectOutputWithBuildCompletedLine_ParsesBothErrors()
        {
            var output =
                "1>------ Build started: Project: Lib, Configuration: Debug Any CPU ------\n" +
                "1>D:\\S\\Lib\\L.cs(3,4,3,10): error CS0103: in lib\n" +
                "2>------ Build started: Project: App, Configuration: Debug Any CPU ------\n" +
                "2>D:\\S\\App\\A.cs(1,1,1,2): error CS0246: in app\n" +
                "========== Build: 0 succeeded, 2 failed, 0 up-to-date, 0 skipped ==========\n" +
                "========== Build completed at 22:15 and took 02,901 seconds ==========";

            var messages = BuildSolution.ParseBuildOutput(output);

            Assert.That(messages, Has.Count.EqualTo(2));
            Assert.That(messages.Select(m => m.Message), Is.EqualTo(new[] { "in lib", "in app" }));
            Assert.That(messages[0].File, Is.EqualTo(@"D:\S\Lib\L.cs"));
            Assert.That(messages[1].File, Is.EqualTo(@"D:\S\App\A.cs"));
        }

        [Test]
        public void OrderByFailedProjects_EarlierFailedProjectComesFirstAndOwnsItsErrors()
        {
            var messages = new List<BuildMessage>
            {
                new BuildMessage { File = @"D:\S\App\A.cs", Message = "dependent" },
                new BuildMessage { File = @"D:\S\Lib\L.cs", Message = "dependency" }
            };

            var result = BuildSolution.OrderByFailedProjects(
                messages, new[] { @"Lib\Lib.csproj", @"App\App.csproj" }, @"D:\S");

            Assert.That(result.Select(m => m.Message), Is.EqualTo(new[] { "dependency", "dependent" }));
            Assert.That(result[0].Project, Is.EqualTo(@"Lib\Lib.csproj"));
            Assert.That(result[1].Project, Is.EqualTo(@"App\App.csproj"));
        }

        [Test]
        public void OrderByFailedProjects_LongestDirectoryPrefixWins()
        {
            var messages = new List<BuildMessage> { new BuildMessage { File = @"D:\S\Lib\Sub\S.cs" } };

            var result = BuildSolution.OrderByFailedProjects(
                messages, new[] { @"Lib\Lib.csproj", @"Lib\Sub\Sub.csproj" }, @"D:\S");

            Assert.That(result[0].Project, Is.EqualTo(@"Lib\Sub\Sub.csproj"));
        }

        [Test]
        public void OrderByFailedProjects_SingleFailedProject_OwnsUnmatchedMessages()
        {
            var messages = new List<BuildMessage>
            {
                new BuildMessage { File = null },
                new BuildMessage { File = @"D:\Elsewhere\Shared.cs" }
            };

            var result = BuildSolution.OrderByFailedProjects(messages, new[] { @"Lib\Lib.csproj" }, @"D:\S");

            Assert.That(result.Select(m => m.Project), Is.EqualTo(new[] { @"Lib\Lib.csproj", @"Lib\Lib.csproj" }));
        }

        [Test]
        public void OrderByFailedProjects_MessageOutsideEveryFailedProject_HasNoProject()
        {
            var messages = new List<BuildMessage> { new BuildMessage { File = @"D:\S\App\A.cs" } };

            var result = BuildSolution.OrderByFailedProjects(
                messages, new[] { @"Lib\Lib.csproj", @"Other\Other.csproj" }, @"D:\S");

            Assert.That(result[0].Project, Is.Null);
        }

        [Test]
        public void OrderByFailedProjects_InvalidUniqueName_IsIgnoredInsteadOfFailing()
        {
            var messages = new List<BuildMessage> { new BuildMessage { File = @"D:\S\Other\O.cs" } };

            List<BuildMessage> result = null;
            Assert.DoesNotThrow(() => result = BuildSolution.OrderByFailedProjects(
                messages, new[] { "bad|name\\Bad.csproj", @"Other\Other.csproj" }, @"D:\S"));

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].Project, Is.EqualTo(@"Other\Other.csproj"),
                "the name that cannot be turned into a path must not break attribution of the other projects");
        }

        [Test]
        public void OrderByFailedProjects_NoFailedProjects_KeepsMessagesUntouched()
        {
            var messages = new List<BuildMessage> { new BuildMessage { File = @"D:\S\Lib\L.cs" } };

            var result = BuildSolution.OrderByFailedProjects(messages, new List<string>(), @"D:\S");

            Assert.That(result, Is.EqualTo(messages));
            Assert.That(result[0].Project, Is.Null);
        }

        [Test]
        public void OrderByFailedProjects_RootProject_OwnsSolutionRootFiles()
        {
            var messages = new List<BuildMessage> { new BuildMessage { File = @"D:\S\A.cs" } };

            var result = BuildSolution.OrderByFailedProjects(messages, new[] { "App.csproj" }, @"D:\S");

            Assert.That(result[0].Project, Is.EqualTo("App.csproj"));
        }

        [Test]
        public void OrderByFailedProjects_RootAndNestedProject_NestedWinsForNestedFiles()
        {
            var messages = new List<BuildMessage>
            {
                new BuildMessage { File = @"D:\S\A.cs" },
                new BuildMessage { File = @"D:\S\Lib\L.cs" }
            };

            var result = BuildSolution.OrderByFailedProjects(
                messages, new[] { "App.csproj", @"Lib\Lib.csproj" }, @"D:\S");

            Assert.That(result[0].Project, Is.EqualTo("App.csproj"));
            Assert.That(result[0].File, Is.EqualTo(@"D:\S\A.cs"));
            Assert.That(result[1].Project, Is.EqualTo(@"Lib\Lib.csproj"));
            Assert.That(result[1].File, Is.EqualTo(@"D:\S\Lib\L.cs"));
        }

        [Test]
        public void OrderByFailedProjects_SingleProjectWithUnusableName_DoesNotOwnUnmatchedMessages()
        {
            var messages = new List<BuildMessage> { new BuildMessage { File = null } };

            var result = BuildSolution.OrderByFailedProjects(messages, new[] { "bad|name\\Bad.csproj" }, @"D:\S");

            Assert.That(result[0].Project, Is.Null);
        }

        [Test]
        public void FailureSummary_FailedProjectsAndCut()
        {
            Assert.That(
                BuildSolution.FailureSummary(new[] { @"A\A.csproj", @"B\B.csproj" }, 25, 80),
                Is.EqualTo(@"Failed projects: A\A.csproj, B\B.csproj. Showing 25 of 80 errors."));
        }

        [Test]
        public void FailureSummary_CompleteList_OmitsTheCut()
        {
            Assert.That(
                BuildSolution.FailureSummary(new[] { @"A\A.csproj" }, 3, 3),
                Is.EqualTo(@"Failed projects: A\A.csproj."));
        }

        [Test]
        public void FailureSummary_NoFailedProject_KeepsThePreviousWording()
        {
            Assert.That(
                BuildSolution.FailureSummary(new List<string>(), 0, 0),
                Is.EqualTo("Build failed."));
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

        [Test]
        public void SearchVariants_JsonEscapedPath_AddsUnescapedSpelling()
        {
            // Seen in a real session: the model sent "LMLocal.Tests.Unit\\\\LMLocal.Tests.Unit.csproj" (doubled backslash).
            var variants = ProjectFinder.SearchVariants(@"LMLocal.Tests.Unit\\LMLocal.Tests.Unit.csproj");

            Assert.That(variants, Is.EqualTo(new[]
            {
                @"LMLocal.Tests.Unit\\LMLocal.Tests.Unit.csproj",
                @"LMLocal.Tests.Unit\LMLocal.Tests.Unit.csproj"
            }));
        }

        [Test]
        public void SearchVariants_UnescapedSpellingMatchesUniqueName()
        {
            const string uniqueName = @"LMLocal.Tests.Unit\LMLocal.Tests.Unit.csproj";
            var variants = ProjectFinder.SearchVariants(@"LMLocal.Tests.Unit\\LMLocal.Tests.Unit.csproj");

            Assert.That(ProjectFinder.IsProjectNameMatch("LMLocal.Tests.Unit", uniqueName, null, variants[0]), Is.False,
                "the doubled backslash alone does not match");
            Assert.That(ProjectFinder.IsProjectNameMatch("LMLocal.Tests.Unit", uniqueName, null, variants[1]), Is.True);
        }

        [TestCase("LMLocal")]
        [TestCase(@"MyApp\MyApp.csproj")]
        public void SearchVariants_NothingToUnescape_ReturnsOnlyTheInput(string search)
        {
            Assert.That(ProjectFinder.SearchVariants(search), Is.EqualTo(new[] { search }));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void SearchVariants_Empty_ReturnsNothing(string search)
        {
            Assert.That(ProjectFinder.SearchVariants(search), Is.Empty);
        }

        [Test]
        public void ToProjectFullPath_RelativeProjectPath_IsResolvedAgainstTheSolutionDirectory()
        {
            Assert.That(ProjectFinder.ToProjectFullPath(@"MyApp\MyApp.csproj", @"C:\src"),
                Is.EqualTo(@"C:\src\MyApp\MyApp.csproj"));
            Assert.That(ProjectFinder.ToProjectFullPath("MyApp/MyApp.csproj", @"C:\src"),
                Is.EqualTo(@"C:\src\MyApp\MyApp.csproj"));
        }

        [Test]
        public void ToProjectFullPath_AbsolutePath_IsKept()
        {
            Assert.That(ProjectFinder.ToProjectFullPath(@"D:\other\X.vcxproj", @"C:\src"), Is.EqualTo(@"D:\other\X.vcxproj"));
        }

        [TestCase("MyApp")]
        [TestCase(@"MyApp\Program.cs")]
        [TestCase("")]
        public void ToProjectFullPath_NotAProjectFile_ReturnsNull(string reference)
        {
            Assert.That(ProjectFinder.ToProjectFullPath(reference, @"C:\src"), Is.Null);
        }
    }
}
