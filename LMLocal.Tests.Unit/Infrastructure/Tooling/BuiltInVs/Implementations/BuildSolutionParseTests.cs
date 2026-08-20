using System;
using System.Collections.Generic;
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
        public void MergeMessages_RemovesDuplicates()
        {
            var target = new List<BuildMessage>
            {
                new BuildMessage { File = "a.cs", Line = 1, Column = 0, Message = "first" }
            };
            var source = new List<BuildMessage>
            {
                // duplicate of existing (column differs -> still duplicate)
                new BuildMessage { File = "a.cs", Line = 1, Column = 5, Message = "first" },
                new BuildMessage { File = "b.cs", Line = 2, Column = 0, Message = "second" },
                new BuildMessage { File = "c.cs", Line = 3, Column = 0, Message = string.Empty }
            };

            BuildSolution.MergeMessages(target, source);

            Assert.That(target, Has.Count.EqualTo(2));
            Assert.That(target[1].Message, Is.EqualTo("second"));
        }

        [Test]
        public void MergeMessages_ProjectUpgrade_EnrichesExistingProjectLessEntry()
        {
            var target = new List<BuildMessage>
            {
                // Error captured from the parse path: no project attribution.
                new BuildMessage { File = "a.cs", Line = 1, Column = 0, Message = "first" }
            };
            var source = new List<BuildMessage>
            {
                // Same error from the Error List, which knows the project.
                new BuildMessage { File = "a.cs", Line = 1, Column = 7, Message = "first", Project = "MyApp" }
            };

            BuildSolution.MergeMessages(target, source);

            Assert.That(target, Has.Count.EqualTo(1));
            Assert.That(target[0].Project, Is.EqualTo("MyApp"));
            // The richer column from the Error List entry wins.
            Assert.That(target[0].Column, Is.EqualTo(7));
        }

        [Test]
        public void MergeMessages_SameErrorFromDifferentProjects_KeepsBoth()
        {
            var target = new List<BuildMessage>
            {
                new BuildMessage { File = "a.cs", Line = 1, Column = 0, Message = "first", Project = "ProjectA" }
            };
            var source = new List<BuildMessage>
            {
                // Same shared source file built into a second project.
                new BuildMessage { File = "a.cs", Line = 1, Column = 0, Message = "first", Project = "ProjectB" }
            };

            BuildSolution.MergeMessages(target, source);

            Assert.That(target, Has.Count.EqualTo(2));
            Assert.That(target[1].Project, Is.EqualTo("ProjectB"));
        }

        [Test]
        public void MergeMessages_ProjectLessDuplicateWhenExistingHasProject_IsDropped()
        {
            var target = new List<BuildMessage>
            {
                new BuildMessage { File = "a.cs", Line = 1, Column = 0, Message = "first", Project = "ProjectA" }
            };
            var source = new List<BuildMessage>
            {
                // Project-less duplicate of an entry that already has a project.
                new BuildMessage { File = "a.cs", Line = 1, Column = 0, Message = "first" }
            };

            BuildSolution.MergeMessages(target, source);

            Assert.That(target, Has.Count.EqualTo(1));
            Assert.That(target[0].Project, Is.EqualTo("ProjectA"));
        }


        [Test]
        public void IsProjectNameMatch_MatchesByNameUniqueNameAndFileName()
        {
            const string name = "MyApp";
            const string uniqueName = @"MyApp\MyApp.csproj";
            const string fullName = @"C:\src\MyApp\MyApp.csproj";

            Assert.That(BuildSolution.IsProjectNameMatch(name, uniqueName, fullName, "myapp"), Is.True);
            Assert.That(BuildSolution.IsProjectNameMatch(name, uniqueName, fullName, "MyApp.csproj"), Is.True);
            Assert.That(BuildSolution.IsProjectNameMatch(name, uniqueName, fullName, @"myapp/myapp.csproj"), Is.True);
            Assert.That(BuildSolution.IsProjectNameMatch(name, uniqueName, fullName, "Other"), Is.False);
        }

        [Test]
        public void IsProjectNameMatch_NullFields_DoesNotThrow()
        {
            Assert.That(BuildSolution.IsProjectNameMatch(null, null, null, "MyApp"), Is.False);
            Assert.That(BuildSolution.IsProjectNameMatch("MyApp", null, null, "MyApp"), Is.True);
        }

        [Test]
        public void NormalizeProjectName_TrimsQuotesAndNormalizesSlashes()
        {
            Assert.That(BuildSolution.NormalizeProjectName(@"""MyApp\MyApp.csproj"""), Is.EqualTo(@"MyApp\MyApp.csproj"));
            Assert.That(BuildSolution.NormalizeProjectName("MyApp/MyApp.csproj"), Is.EqualTo(@"MyApp\MyApp.csproj"));
            Assert.That(BuildSolution.NormalizeProjectName(null), Is.Null);
        }

        [Test]
        public void ShouldIncludeError_ListGrew_ExcludesPreExistingKeys()
        {
            var preBuild = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "a.cs|1|old error",
                "b.cs|2|another old error"
            };

            // Stale entry already on screen before the build -> excluded.
            Assert.That(BuildSolution.ShouldIncludeError(2, 4, preBuild, "a.cs|1|old error"), Is.False);
            // Brand new entry -> included even though the list grew.
            Assert.That(BuildSolution.ShouldIncludeError(2, 4, preBuild, "c.cs|3|new error"), Is.True);
        }

        [Test]
        public void ShouldIncludeError_ListReset_IncludesEverything()
        {
            var preBuild = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "a.cs|1|old error"
            };

            // List was cleared/replaced (count did not grow): the same key is
            // relevant again for this build.
            Assert.That(BuildSolution.ShouldIncludeError(2, 1, preBuild, "a.cs|1|old error"), Is.True);
            Assert.That(BuildSolution.ShouldIncludeError(2, 2, preBuild, "a.cs|1|old error"), Is.True);
            Assert.That(BuildSolution.ShouldIncludeError(0, 0, preBuild, "a.cs|1|old error"), Is.True);
        }

        [Test]
        public void ShouldIncludeError_NoSnapshot_IncludesAll()
        {
            // When the pre-build snapshot is unavailable, never drop anything.
            Assert.That(BuildSolution.ShouldIncludeError(2, 5, null, "anything"), Is.True);
        }
    }
}

