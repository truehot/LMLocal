using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Common
{
    [TestFixture]
    public class SolutionInspectorTests
    {
        [Test]
        public void IsTestProject_NativeFlag_AlwaysTrue()
        {
            Assert.That(SolutionInspector.IsTestProject("SomeApp", true), Is.True);
            Assert.That(SolutionInspector.IsTestProject("", true), Is.True);
            Assert.That(SolutionInspector.IsTestProject(null, true), Is.True);
        }

        [Test]
        public void IsTestProject_NameContainsTest_True()
        {
            Assert.That(SolutionInspector.IsTestProject("MyApp.Tests", false), Is.True);
            Assert.That(SolutionInspector.IsTestProject("TestUtils", false), Is.True);
            Assert.That(SolutionInspector.IsTestProject("LMLocal.Tests.Unit", false), Is.True);
        }

        [Test]
        public void IsTestProject_WithoutTestInName_False()
        {
            Assert.That(SolutionInspector.IsTestProject("MyApp.Core", false), Is.False);
            Assert.That(SolutionInspector.IsTestProject("", false), Is.False);
            Assert.That(SolutionInspector.IsTestProject(null, false), Is.False);
        }
    }
}
