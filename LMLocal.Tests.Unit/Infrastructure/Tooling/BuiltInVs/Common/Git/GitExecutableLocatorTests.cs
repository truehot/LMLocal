using System;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Git;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Common.Git
{
    [TestFixture]
    public class GitExecutableLocatorTests
    {
        // FileVersion strings of real git.exe builds (Git for Windows 2.53, VS-bundled 2.56): Version.TryParse rejects them.
        [TestCase("2.53.0.windows.3", 2, 53, 0)]
        [TestCase("2.56.1.windows.1", 2, 56, 1)]
        [TestCase("2.24.0", 2, 24, 0)]
        [TestCase("2.30", 2, 30, 0)]
        public void ParseVersion_ReadsLeadingNumbers(string text, int major, int minor, int build)
        {
            Assert.That(GitExecutableLocator.ParseVersion(text), Is.EqualTo(new Version(major, minor, build)));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("windows")]
        [TestCase("2")]
        public void ParseVersion_WithoutMajorAndMinor_ReturnsNull(string text)
        {
            Assert.That(GitExecutableLocator.ParseVersion(text), Is.Null);
        }

        [Test]
        public void TryGet_AcceptsSupportedVersion()
        {
            string existing = typeof(GitExecutableLocatorTests).Assembly.Location;
            var locator = new GitExecutableLocator(existing, new Version(2, 24, 0));

            string path;
            string error;
            Assert.That(locator.TryGet(out path, out error), Is.True, error);
            Assert.That(path, Is.EqualTo(existing));
        }

        [Test]
        public void TryGet_RejectsOldOrUnknownVersion()
        {
            string existing = typeof(GitExecutableLocatorTests).Assembly.Location;
            string path;
            string error;

            Assert.That(new GitExecutableLocator(existing, new Version(2, 23, 9)).TryGet(out path, out error), Is.False);
            Assert.That(error, Does.Contain("2.24"));
            Assert.That(new GitExecutableLocator(existing, null).TryGet(out path, out error), Is.False);
            Assert.That(error, Does.Contain("unknown version"));
        }

        [Test]
        public void TryGet_MissingExecutable_ReturnsInstallHint()
        {
            string path;
            string error;

            Assert.That(new GitExecutableLocator(null, null).TryGet(out path, out error), Is.False);
            Assert.That(error, Does.Contain("not found"));
        }
    }
}
