using System.Collections.Generic;
using System.IO;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Testing;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Common.Testing
{
    [TestFixture]
    public class TestAssemblyLocatorTests
    {
        private static readonly string ProjectDirectory = Path.Combine("C:\\", "src", "App");

        [Test]
        public void ResolveOutputDirectory_RelativeOutputPath_IsCombinedWithProjectDirectory()
        {
            var directory = TestAssemblyLocator.ResolveOutputDirectory(ProjectDirectory, @"bin\Release\");

            Assert.That(directory, Is.EqualTo(Path.Combine(ProjectDirectory, "bin", "Release")));
        }

        [Test]
        public void ResolveOutputDirectory_NestedOutputPath_KeepsFrameworkFolder()
        {
            var directory = TestAssemblyLocator.ResolveOutputDirectory(ProjectDirectory, @"bin\Release\net48");

            Assert.That(directory, Is.EqualTo(Path.Combine(ProjectDirectory, "bin", "Release", "net48")));
        }

        [Test]
        public void ResolveOutputDirectory_AbsoluteOutputPath_IsKept()
        {
            string absolute = Path.Combine("C:\\", "out", "Release");

            var directory = TestAssemblyLocator.ResolveOutputDirectory(ProjectDirectory, absolute + Path.DirectorySeparatorChar);

            Assert.That(directory, Is.EqualTo(absolute));
        }

        [Test]
        public void ResolveOutputDirectory_MissingInput_ReturnsNull()
        {
            Assert.That(TestAssemblyLocator.ResolveOutputDirectory(null, @"bin\Debug"), Is.Null);
            Assert.That(TestAssemblyLocator.ResolveOutputDirectory(ProjectDirectory, null), Is.Null);
            Assert.That(TestAssemblyLocator.ResolveOutputDirectory(ProjectDirectory, "   "), Is.Null);
        }

        [Test]
        public void ResolveOutputDirectory_InvalidPath_ReturnsNull()
        {
            Assert.That(TestAssemblyLocator.ResolveOutputDirectory(ProjectDirectory, "bin|Debug"), Is.Null);
        }

        [Test]
        public void ResolveAssemblyCandidates_OutputPath_ProducesSingleAssembly()
        {
            var candidates = TestAssemblyLocator.ResolveAssemblyCandidates(
                ProjectDirectory, "App.Tests.csproj", @"bin\Release\", "Release");

            Assert.That(candidates, Is.EqualTo(new List<string>
            {
                Path.Combine(ProjectDirectory, "bin", "Release", "App.Tests.dll")
            }));
        }

        [Test]
        public void ResolveAssemblyCandidates_NoOutputPath_FallsBackToConfigurationDirectory()
        {
            var candidates = TestAssemblyLocator.ResolveAssemblyCandidates(
                ProjectDirectory, "App.Tests.csproj", null, "Release");

            Assert.That(candidates, Is.EqualTo(new List<string>
            {
                Path.Combine(ProjectDirectory, "bin", "Release", "App.Tests.dll")
            }));
        }

        [Test]
        public void ResolveAssemblyCandidates_NoOutputPathNoConfiguration_ReturnsEmpty()
        {
            var candidates = TestAssemblyLocator.ResolveAssemblyCandidates(
                ProjectDirectory, "App.Tests.csproj", null, null);

            Assert.That(candidates, Is.Empty);
        }

        [Test]
        public void ResolveAssemblyCandidates_ConfigurationWithSeparators_IsNotUsedAsDirectory()
        {
            var candidates = TestAssemblyLocator.ResolveAssemblyCandidates(
                ProjectDirectory, "App.Tests.csproj", null, @"..\..\evil");

            Assert.That(candidates, Is.Empty);
        }

        [Test]
        public void ResolveAssemblyCandidates_MissingProjectFileName_ReturnsEmpty()
        {
            var candidates = TestAssemblyLocator.ResolveAssemblyCandidates(
                ProjectDirectory, null, @"bin\Release\", "Release");

            Assert.That(candidates, Is.Empty);
        }
    }
}
