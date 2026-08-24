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

        [Test]
        public void BuildSdkTestArguments_NoFilter()
        {
            var args = TestArgumentsBuilder.BuildSdkTestArguments(@"C:\p\App.csproj");

            Assert.That(args, Does.Contain(@"test ""C:\p\App.csproj"""));
            Assert.That(args, Does.Contain("--no-restore"));
            Assert.That(args.Contains("--filter"), Is.False);
            Assert.That(args.Contains("-c "), Is.False);
            Assert.That(args.Contains("Platform"), Is.False);
        }

        [Test]
        public void BuildSdkTestArguments_WithFilter()
        {
            var args = TestArgumentsBuilder.BuildSdkTestArguments(@"C:\p\App.csproj", "MyTests.Method");

            Assert.That(args, Does.Contain(@"test ""C:\p\App.csproj"""));
            Assert.That(args, Does.Contain("--filter \"FullyQualifiedName~MyTests.Method\""));
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

        [Test]
        public void BuildBuildArguments_DefaultsToDebug()
        {
            var args = TestArgumentsBuilder.BuildBuildArguments(@"C:\p\App.csproj");

            Assert.That(args, Does.Contain(@"build ""C:\p\App.csproj"""));
            Assert.That(args, Does.Contain("--configuration Debug"));
            Assert.That(args, Does.Contain("--no-restore"));
            Assert.That(args.Contains("Platform"), Is.False);
        }

        [Test]
        public void BuildSdkTestArguments_RestoreTrue_OmitsNoRestore()
        {
            var args = TestArgumentsBuilder.BuildSdkTestArguments(@"C:\p\App.csproj", null, restore: true);

            Assert.That(args.Contains("--no-restore"), Is.False);
            Assert.That(args, Does.Contain(@"test ""C:\p\App.csproj"""));
        }

        [Test]
        public void BuildSdkTestArguments_RestoreFalse_KeepsNoRestore()
        {
            var args = TestArgumentsBuilder.BuildSdkTestArguments(@"C:\p\App.csproj", null, restore: false);

            Assert.That(args, Does.Contain("--no-restore"));
        }

        [Test]
        public void BuildBuildArguments_RestoreTrue_OmitsNoRestore()
        {
            var args = TestArgumentsBuilder.BuildBuildArguments(@"C:\p\App.csproj", restore: true);

            Assert.That(args.Contains("--no-restore"), Is.False);
            Assert.That(args, Does.Contain("--configuration Debug"));
        }

        [Test]
        public void BuildBuildArguments_RestoreFalse_KeepsNoRestore()
        {
            var args = TestArgumentsBuilder.BuildBuildArguments(@"C:\p\App.csproj", restore: false);

            Assert.That(args, Does.Contain("--no-restore"));
        }
    }
}
