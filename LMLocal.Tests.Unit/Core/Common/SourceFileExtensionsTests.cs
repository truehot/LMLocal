using LMLocal.Core.Common;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Core.Common
{
    [TestFixture]
    public class SourceFileExtensionsTests
    {
        [Test]
        public void IsSourceFile_DotNetExtensions_ForNonCppLanguage()
        {
            Assert.That(SourceFileExtensions.IsSourceFile(".cs", "C#"), Is.True);
            Assert.That(SourceFileExtensions.IsSourceFile(".vb", "VB.NET"), Is.True);
            Assert.That(SourceFileExtensions.IsSourceFile(".fs", "F#"), Is.True);
            Assert.That(SourceFileExtensions.IsSourceFile(".xaml", "C#"), Is.True);
            Assert.That(SourceFileExtensions.IsSourceFile(".resx", "C#"), Is.True);
        }

        [Test]
        public void IsSourceFile_CppExtensions_ForCppLanguage()
        {
            Assert.That(SourceFileExtensions.IsSourceFile(".cpp", "C++"), Is.True);
            Assert.That(SourceFileExtensions.IsSourceFile(".cc", "C++"), Is.True);
            Assert.That(SourceFileExtensions.IsSourceFile(".cxx", "C++"), Is.True);
            Assert.That(SourceFileExtensions.IsSourceFile(".c", "C++"), Is.True);
            Assert.That(SourceFileExtensions.IsSourceFile(".h", "C++"), Is.True);
            Assert.That(SourceFileExtensions.IsSourceFile(".hh", "C++"), Is.True);
            Assert.That(SourceFileExtensions.IsSourceFile(".hpp", "C++"), Is.True);
            Assert.That(SourceFileExtensions.IsSourceFile(".hxx", "C++"), Is.True);
            Assert.That(SourceFileExtensions.IsSourceFile(".inl", "C++"), Is.True);
            Assert.That(SourceFileExtensions.IsSourceFile(".ipp", "C++"), Is.True);
            Assert.That(SourceFileExtensions.IsSourceFile(".def", "C++"), Is.True);
            Assert.That(SourceFileExtensions.IsSourceFile(".idl", "C++"), Is.True);
        }

        [Test]
        public void IsSourceFile_CppExtension_IsNotSource_ForDotNetLanguage()
        {
            Assert.That(SourceFileExtensions.IsSourceFile(".cpp", "C#"), Is.False);
            Assert.That(SourceFileExtensions.IsSourceFile(".h", "C#"), Is.False);
        }

        [Test]
        public void IsSourceFile_DotNetExtension_IsNotSource_ForCppLanguage()
        {
            Assert.That(SourceFileExtensions.IsSourceFile(".cs", "C++"), Is.False);
            Assert.That(SourceFileExtensions.IsSourceFile(".xaml", "C++"), Is.False);
        }

        [Test]
        public void IsSourceFile_CaseInsensitive()
        {
            Assert.That(SourceFileExtensions.IsSourceFile(".CS", "C#"), Is.True);
            Assert.That(SourceFileExtensions.IsSourceFile(".CPP", "C++"), Is.True);
            Assert.That(SourceFileExtensions.IsSourceFile(".HPP", "c++"), Is.True);
        }

        [Test]
        public void IsSourceFile_UnknownExtension_IsFalse()
        {
            Assert.That(SourceFileExtensions.IsSourceFile(".png", "C#"), Is.False);
            Assert.That(SourceFileExtensions.IsSourceFile(".txt", "C++"), Is.False);
            Assert.That(SourceFileExtensions.IsSourceFile(".json", null), Is.False);
        }

        [Test]
        public void IsSourceFile_NullOrEmptyExtension_IsFalse()
        {
            Assert.That(SourceFileExtensions.IsSourceFile(null, "C#"), Is.False);
            Assert.That(SourceFileExtensions.IsSourceFile("", "C#"), Is.False);
        }

        [Test]
        public void IsSourceFile_UnknownLanguage_UsesDotNetSet()
        {
            Assert.That(SourceFileExtensions.IsSourceFile(".cs", null), Is.True);
            Assert.That(SourceFileExtensions.IsSourceFile(".cs", "Python"), Is.True);
        }
    }
}
