using System;
using System.IO;
using NUnit.Framework;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Common
{
    [TestFixture]
    public class ProjectMetadataProviderTests
    {
        private string _root;

        [SetUp]
        public void SetUp()
        {
            ProjectMetadataProvider.ClearAll(); // static cache
            _root = Path.Combine(Path.GetTempPath(), "ProjectMetadataProviderTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
        }

        [Test]
        public void GetMetadata_ReturnsUnknown_ForNullOrEmptyPath()
        {
            var res1 = ProjectMetadataProvider.GetMetadata(null);
            Assert.That(res1.Language, Is.EqualTo("Unknown"));
            Assert.That(res1.TargetFramework, Is.Null);

            var res2 = ProjectMetadataProvider.GetMetadata(string.Empty);
            Assert.That(res2.Language, Is.EqualTo("Unknown"));
            Assert.That(res2.TargetFramework, Is.Null);
        }

        [Test]
        public void GetMetadata_DetectsCSharpAndTargetFramework_FromCsprojFile()
        {
            var file = Path.Combine(_root, "MyProj.csproj");
            var content = "<Project><PropertyGroup><TargetFramework>net472</TargetFramework></PropertyGroup></Project>";
            File.WriteAllText(file, content);

            var meta = ProjectMetadataProvider.GetMetadata(file);

            Assert.That(meta.Language, Is.EqualTo("C#"));
            Assert.That(meta.TargetFramework, Is.EqualTo("net472"));
        }

        [Test]
        public void GetMetadata_TakesFirstFramework_FromTargetFrameworks()
        {
            var file = Path.Combine(_root, "Multi.csproj");
            var content = "<Project><PropertyGroup><TargetFrameworks>net5.0;netcoreapp3.1</TargetFrameworks></PropertyGroup></Project>";
            File.WriteAllText(file, content);

            var meta = ProjectMetadataProvider.GetMetadata(file);

            Assert.That(meta.Language, Is.EqualTo("C#"));
            Assert.That(meta.TargetFramework, Is.EqualTo("net5.0"));
        }

        [Test]
        public void GetMetadata_ResolvesDirectory_ToFirstProjectFile()
        {
            var dir = Path.Combine(_root, "DirWithProj");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, "Contained.csproj");
            File.WriteAllText(file, "<Project><PropertyGroup><TargetFramework>netstandard2.0</TargetFramework></PropertyGroup></Project>");

            var meta = ProjectMetadataProvider.GetMetadata(dir);

            Assert.That(meta.Language, Is.EqualTo("C#"));
            Assert.That(meta.TargetFramework, Is.EqualTo("netstandard2.0"));
        }

        [Test]
        public void GetMetadata_DoesNotDetectLanguageFromContent_WhenExtensionUnknown()
        {
            var file = Path.Combine(_root, "weird.projx");
            File.WriteAllText(file, "This file mentions csharp in content but has unknown extension: csharp");

            var meta = ProjectMetadataProvider.GetMetadata(file);

            // Current implementation does not read files for unknown extensions, so language remains Unknown
            Assert.That(meta.Language, Is.EqualTo("Unknown"));
            Assert.That(meta.TargetFramework, Is.EqualTo("Unknown"));
        }

        [Test]
        public void GetMetadata_DoesNotRead_VeryLargeProjectFiles()
        {
            var file = Path.Combine(_root, "Large.csproj");
            // Create file slightly larger than 1MB
            var sb = new System.Text.StringBuilder();
            sb.Append("<Project><PropertyGroup>");
            while (sb.Length < 1 * 1024 * 1024 + 100)
                sb.Append("<Dummy>xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx</Dummy>");
            sb.Append("</PropertyGroup></Project>");
            File.WriteAllText(file, sb.ToString());

            var meta = ProjectMetadataProvider.GetMetadata(file);

            // Language is known by extension, but target framework is not read for large files
            Assert.That(meta.Language, Is.EqualTo("C#"));
            Assert.That(meta.TargetFramework, Is.EqualTo("Unknown"));
        }

        [Test]
        public void GetMetadata_DetectsCppLanguage_FromVcxproj()
        {
            var file = Path.Combine(_root, "Native.vcxproj");
            File.WriteAllText(file, "<Project><PropertyGroup><ConfigurationType>Application</ConfigurationType></PropertyGroup></Project>");

            var meta = ProjectMetadataProvider.GetMetadata(file);

            Assert.That(meta.Language, Is.EqualTo("C++"));
            Assert.That(meta.TargetFramework, Is.EqualTo("Unknown"));
            Assert.That(meta.IsNativeTestProject, Is.False);
        }

        [Test]
        public void GetMetadata_DetectsNativeUnitTest_FromVcxproj()
        {
            var file = Path.Combine(_root, "NativeTest.vcxproj");
            var content = "<Project><PropertyGroup><ConfigurationType>DynamicLibrary</ConfigurationType><UseNativeUnitTest>true</UseNativeUnitTest></PropertyGroup></Project>";
            File.WriteAllText(file, content);

            var meta = ProjectMetadataProvider.GetMetadata(file);

            Assert.That(meta.Language, Is.EqualTo("C++"));
            Assert.That(meta.IsNativeTestProject, Is.True);
        }

        [Test]
        public void GetMetadata_UseNativeUnitTest_False_IsNotTestProject()
        {
            var file = Path.Combine(_root, "NativeNoTest.vcxproj");
            var content = "<Project><PropertyGroup><UseNativeUnitTest>false</UseNativeUnitTest></PropertyGroup></Project>";
            File.WriteAllText(file, content);

            var meta = ProjectMetadataProvider.GetMetadata(file);

            Assert.That(meta.Language, Is.EqualTo("C++"));
            Assert.That(meta.IsNativeTestProject, Is.False);
        }
    }
}
