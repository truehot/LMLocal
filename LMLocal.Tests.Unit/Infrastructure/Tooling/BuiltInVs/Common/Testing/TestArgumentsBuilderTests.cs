using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Testing;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Common.Testing
{
    [TestFixture]
    public class TestArgumentsBuilderTests
    {
        [Test]
        public void SanitizeFilter_AllowsSafeChars_RemovesUnsafe()
        {
            Assert.That(TestArgumentsBuilder.SanitizeFilter("MyTests.Method(1)"), Is.EqualTo("MyTests.Method(1)"));
            Assert.That(TestArgumentsBuilder.SanitizeFilter("A&B|\"C\""), Is.EqualTo("ABC"));
            Assert.That(TestArgumentsBuilder.SanitizeFilter("dir;test"), Is.EqualTo("dir;test"));
        }

        [Test]
        public void SanitizeFilter_EmptyAfterSanitize_ReturnsNull()
        {
            Assert.That(TestArgumentsBuilder.SanitizeFilter("&&|\""), Is.Null);
            Assert.That(TestArgumentsBuilder.SanitizeFilter("   "), Is.Null);
            Assert.That(TestArgumentsBuilder.SanitizeFilter(null), Is.Null);
        }

        [TestCase("RegenerateTests", "RegenerateTests")]
        [TestCase("  RegenerateTests  ", "RegenerateTests")]
        [TestCase("ChatHistoryManagerTests TruncateLastExchange_* ChatSessionOrchestratorReplaceLastTests",
            "ChatHistoryManagerTests|TruncateLastExchange_|ChatSessionOrchestratorReplaceLastTests")]
        [TestCase("TestArgumentsBuilder OR TestAssemblyLocator or RunTestsSummary",
            "TestArgumentsBuilder|TestAssemblyLocator|RunTestsSummary")]
        [TestCase("A|B||C", "A|B|C")]
        [TestCase("A A B", "A|B")]
        [TestCase("*Regenerate*", "Regenerate")]
        public void NormalizeFilter_SeveralNames_JoinedForOr(string raw, string expected)
        {
            Assert.That(TestArgumentsBuilder.NormalizeFilter(raw), Is.EqualTo(expected));
        }

        [TestCase("ChatHistoryManagerTests|TruncateLastExchange_", "ChatHistoryManagerTests TruncateLastExchange_")]
        [TestCase("RegenerateTests", "RegenerateTests")]
        [TestCase(null, null)]
        [TestCase("", null)]
        public void DescribeFilter_NamesSeparatedBySpaces(string normalized, string expected)
        {
            Assert.That(TestArgumentsBuilder.DescribeFilter(normalized), Is.EqualTo(expected));
        }

        [TestCase("FullyQualifiedName~LMLocal.Tests.E2E.AppTests.RegenerateTests", "LMLocal.Tests.E2E.AppTests.RegenerateTests")]
        [TestCase("fullyqualifiedname~RegenerateTests", "RegenerateTests")]
        [TestCase("FullyQualifiedName=Ns.Tests.One", "Ns.Tests.One")]
        [TestCase("Name~Regenerate", "Regenerate")]
        [TestCase("ClassName~RegenerateTests", "RegenerateTests")]
        [TestCase("FullyQualifiedName~A|FullyQualifiedName~B", "A|B")]
        public void NormalizeFilter_VsTestPrefix_IsDropped(string raw, string expected)
        {
            Assert.That(TestArgumentsBuilder.NormalizeFilter(raw), Is.EqualTo(expected));
        }

        [Test]
        public void NormalizeFilter_SpacesInsideParentheses_StayInTheName()
        {
            Assert.That(TestArgumentsBuilder.NormalizeFilter("Ns.Method(1, 2) Other"), Is.EqualTo("Ns.Method(1, 2)|Other"));
        }

        [TestCase(null)]
        [TestCase("   ")]
        [TestCase("OR")]
        [TestCase("* ** |")]
        [TestCase("&& \"\"")]
        public void NormalizeFilter_NothingLeft_ReturnsNull(string raw)
        {
            Assert.That(TestArgumentsBuilder.NormalizeFilter(raw), Is.Null);
        }

        [Test]
        public void BuildSdkTestArguments_SeveralNames_BuildsOrFilter()
        {
            var args = TestArgumentsBuilder.BuildSdkTestArguments(@"C:\p\App.csproj", TestArgumentsBuilder.NormalizeFilter("A.One B.Two"));

            Assert.That(args, Does.Contain("--filter \"FullyQualifiedName~A.One|FullyQualifiedName~B.Two\""));
        }

        [Test]
        public void BuildLegacyVstestArguments_SeveralNames_BuildsOrFilter()
        {
            var args = TestArgumentsBuilder.BuildLegacyVstestArguments(@"C:\p\App.dll", TestArgumentsBuilder.NormalizeFilter("A.One OR B.Two"));

            Assert.That(args, Does.Contain("--TestCaseFilter:\"FullyQualifiedName~A.One|FullyQualifiedName~B.Two\""));
        }

        [Test]
        public void SanitizeConfiguration_KeepsNameAndRemovesQuotes()
        {
            Assert.That(TestArgumentsBuilder.SanitizeConfiguration("Release"), Is.EqualTo("Release"));
            Assert.That(TestArgumentsBuilder.SanitizeConfiguration("  \"Release\"  "), Is.EqualTo("Release"));
            Assert.That(TestArgumentsBuilder.SanitizeConfiguration("My Config-2"), Is.EqualTo("My Config-2"));
            Assert.That(TestArgumentsBuilder.SanitizeConfiguration("\"\""), Is.Null);
            Assert.That(TestArgumentsBuilder.SanitizeConfiguration(null), Is.Null);
        }

        [Test]
        public void BuildSdkTestArguments_NoFilterNoConfiguration()
        {
            var args = TestArgumentsBuilder.BuildSdkTestArguments(@"C:\p\App.csproj");

            Assert.That(args, Does.Contain(@"test ""C:\p\App.csproj"""));
            Assert.That(args, Does.Contain("--no-build"));
            Assert.That(args.Contains("--no-restore"), Is.False);
            Assert.That(args.Contains("--filter"), Is.False);
            Assert.That(args.Contains("-c "), Is.False);
            Assert.That(args.Contains("Platform"), Is.False);
        }

        [Test]
        public void BuildSdkTestArguments_WithConfiguration_PassesConfigurationSwitch()
        {
            var args = TestArgumentsBuilder.BuildSdkTestArguments(@"C:\p\App.csproj", null, "Release");

            Assert.That(args, Does.Contain("-c \"Release\""));
            Assert.That(args.Contains("Platform"), Is.False, "no platform given → none passed");
        }

        [TestCase("x64", "x64")]
        [TestCase("x86", "x86")]
        [TestCase("ARM64", "ARM64")]
        [TestCase("Any CPU", null)]
        [TestCase("AnyCPU", null)]
        [TestCase("anycpu", null)]
        [TestCase("  ", null)]
        [TestCase(null, null)]
        public void ResolveSdkPlatform_PassesOnlyNonAnyCpu(string platform, string expected)
        {
            Assert.That(TestArgumentsBuilder.ResolveSdkPlatform(platform), Is.EqualTo(expected));
        }

        [Test]
        public void BuildSdkTestArguments_X64Platform_PassesPlatformProperty()
        {
            // The SDK appends a non-AnyCPU platform to OutputPath (bin\x64\Release\): without it --no-build misses the build.
            var args = TestArgumentsBuilder.BuildSdkTestArguments(@"C:\p\App.csproj", null, "Release", "x64");

            Assert.That(args, Does.Contain("-c \"Release\""));
            Assert.That(args, Does.Contain("-p:Platform=\"x64\""));
        }

        [Test]
        public void BuildSdkTestArguments_AnyCpuPlatform_OmitsPlatformProperty()
        {
            var args = TestArgumentsBuilder.BuildSdkTestArguments(@"C:\p\App.csproj", null, "Release", "Any CPU");

            Assert.That(args.Contains("Platform"), Is.False);
        }

        [Test]
        public void BuildSdkTestArguments_ConfigurationIsSanitized()
        {
            var args = TestArgumentsBuilder.BuildSdkTestArguments(@"C:\p\App.csproj", null, "\"Rele\"ase&");

            Assert.That(args, Does.Contain("-c \"Release\""));
        }

        [Test]
        public void BuildSdkTestArguments_WhitespaceConfiguration_OmitsSwitch()
        {
            var args = TestArgumentsBuilder.BuildSdkTestArguments(@"C:\p\App.csproj", null, "   ");

            Assert.That(args.Contains("-c "), Is.False);
        }

        [Test]
        public void BuildSdkTestArguments_WithFilterAndConfiguration()
        {
            var args = TestArgumentsBuilder.BuildSdkTestArguments(@"C:\p\App.csproj", "MyTests.Method", "Debug");

            Assert.That(args, Does.Contain(@"test ""C:\p\App.csproj"""));
            Assert.That(args, Does.Contain("--no-build"));
            Assert.That(args, Does.Contain("-c \"Debug\""));
            Assert.That(args, Does.Contain("--filter \"FullyQualifiedName~MyTests.Method\""));
        }

        [Test]
        public void BuildSdkTestArguments_WithFilter()
        {
            var args = TestArgumentsBuilder.BuildSdkTestArguments(@"C:\p\App.csproj", "MyTests.Method");

            Assert.That(args, Does.Contain(@"test ""C:\p\App.csproj"""));
            Assert.That(args, Does.Contain("--no-build"));
            Assert.That(args, Does.Contain("--filter \"FullyQualifiedName~MyTests.Method\""));
        }

        [Test]
        public void BuildSdkTestArguments_DoesNotBuild()
        {
            var args = TestArgumentsBuilder.BuildSdkTestArguments(@"C:\p\App.csproj");

            Assert.That(args, Does.Contain("--no-build"));
            Assert.That(args.Contains("--no-restore"), Is.False);
        }

        [Test]
        public void BuildLegacyVstestArguments_WithFilter()
        {
            var args = TestArgumentsBuilder.BuildLegacyVstestArguments(@"C:\p\bin\Debug\App.dll", "MyTests.Method");

            Assert.That(args, Does.Contain(@"vstest ""C:\p\bin\Debug\App.dll"""));
            Assert.That(args, Does.Contain("--TestCaseFilter:\"FullyQualifiedName~MyTests.Method\""));
            Assert.That(args, Does.Contain("--logger:console;verbosity=normal"));
        }

        [Test]
        public void BuildLegacyVstestArguments_NoFilter()
        {
            var args = TestArgumentsBuilder.BuildLegacyVstestArguments(@"C:\p\App.dll", null);

            Assert.That(args.Contains("--TestCaseFilter"), Is.False);
        }
    }
}
