using System.Collections.Generic;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Projects;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Vs
{
    /// <summary>
    /// The optional build_action / copy_to_output / include_in_vsix parameters of set_file_project_status: parsing and validation.
    /// (Writing them to a project needs Visual Studio and is not covered here.)
    /// </summary>
    [TestFixture]
    public class FileProjectSettingsTests
    {
        private static Dictionary<string, object> Params(params (string Key, object Value)[] pairs)
        {
            var parameters = new Dictionary<string, object>();
            foreach (var pair in pairs)
                parameters[pair.Key] = pair.Value;
            return parameters;
        }

        [Test]
        public void NoParameters_ChangesNothing()
        {
            Assert.That(FileProjectSettings.TryParse(Params(), out var settings, out var error), Is.True);

            Assert.That(error, Is.Null);
            Assert.That(settings.HasAny, Is.False);
        }

        [Test]
        public void NullParameters_ChangesNothing()
        {
            Assert.That(FileProjectSettings.TryParse(null, out var settings, out _), Is.True);
            Assert.That(settings.HasAny, Is.False);
        }

        [Test]
        public void AllThree_AreRead()
        {
            var ok = FileProjectSettings.TryParse(
                Params(("build_action", "Content"), ("copy_to_output", "PreserveNewest"), ("include_in_vsix", true)), out var settings, out _);

            Assert.That(ok, Is.True);
            Assert.That(settings.BuildAction, Is.EqualTo("Content"));
            Assert.That(settings.CopyToOutput, Is.EqualTo("PreserveNewest"));
            Assert.That(settings.IncludeInVsix, Is.True);
            Assert.That(settings.Describe(), Is.EqualTo("item type Content, CopyToOutputDirectory=PreserveNewest, IncludeInVSIX=true"));
        }

        [TestCase("content", "Content")]
        [TestCase(" EMBEDDEDRESOURCE ", "EmbeddedResource")]
        [TestCase("none", "None")]
        public void BuildAction_IsMatchedIgnoringCaseAndSpaces(string input, string expected)
        {
            Assert.That(FileProjectSettings.TryParse(Params(("build_action", input)), out var settings, out _), Is.True);
            Assert.That(settings.BuildAction, Is.EqualTo(expected));
        }

        [TestCase("preservenewest", "PreserveNewest")]
        [TestCase("Always", "Always")]
        [TestCase("never", "Never")]
        public void CopyToOutput_IsMatchedIgnoringCase(string input, string expected)
        {
            Assert.That(FileProjectSettings.TryParse(Params(("copy_to_output", input)), out var settings, out _), Is.True);
            Assert.That(settings.CopyToOutput, Is.EqualTo(expected));
        }

        [TestCase("true", true)]
        [TestCase("False", false)]
        [TestCase(false, false)]
        public void IncludeInVsix_AcceptsABooleanOrItsText(object input, bool expected)
        {
            Assert.That(FileProjectSettings.TryParse(Params(("include_in_vsix", input)), out var settings, out _), Is.True);
            Assert.That(settings.IncludeInVsix, Is.EqualTo(expected));
        }

        [Test]
        public void UnknownBuildAction_NamesTheAllowedValues()
        {
            Assert.That(FileProjectSettings.TryParse(Params(("build_action", "Resource")), out _, out var error), Is.False);
            Assert.That(error, Is.EqualTo("Parameter 'build_action' must be one of: Compile, Content, None, EmbeddedResource."));
        }

        [Test]
        public void UnknownCopyToOutput_NamesTheAllowedValues()
        {
            Assert.That(FileProjectSettings.TryParse(Params(("copy_to_output", "yes")), out _, out var error), Is.False);
            Assert.That(error, Is.EqualTo("Parameter 'copy_to_output' must be one of: Never, Always, PreserveNewest."));
        }

        [Test]
        public void IncludeInVsixThatIsNotABoolean_IsRejected()
        {
            Assert.That(FileProjectSettings.TryParse(Params(("include_in_vsix", "maybe")), out _, out var error), Is.False);
            Assert.That(error, Is.EqualTo("Parameter 'include_in_vsix' must be true or false."));
        }

        [Test]
        public void EmptyOrNullValues_LeaveTheSettingAlone()
        {
            var ok = FileProjectSettings.TryParse(
                Params(("build_action", ""), ("copy_to_output", null), ("include_in_vsix", "  ")), out var settings, out _);

            Assert.That(ok, Is.True);
            Assert.That(settings.HasAny, Is.False);
        }

        [Test]
        public void OnlyTheGivenSettings_AreDescribed()
        {
            FileProjectSettings.TryParse(Params(("include_in_vsix", false)), out var settings, out _);

            Assert.That(settings.HasAny, Is.True);
            Assert.That(settings.BuildAction, Is.Null);
            Assert.That(settings.Describe(), Is.EqualTo("IncludeInVSIX=false"));
        }
    }
}
