using System.Collections.Generic;
using LMLocal.Application.SubAgents;
using LMLocal.Core.Models;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Application.SubAgents
{
    [TestFixture]
    public class SubAgentPipelinePromptTests
    {
        [Test]
        public void Render_SubstitutesTaskAndPrev()
        {
            var text = SubAgentPipelinePrompt.Render("Task: {{task}}\nDraft:\n{{prev}}", "find X", "answer 1", null);

            Assert.That(text, Is.EqualTo("Task: find X\nDraft:\nanswer 1"));
        }

        [Test]
        public void Render_SubstitutesNamedStepOutputs()
        {
            var outputs = new Dictionary<string, string> { { "explore", "found in Foo.cs" } };

            var text = SubAgentPipelinePrompt.Render("Data:\n{{steps.explore}}", "task", "prev", outputs);

            Assert.That(text, Is.EqualTo("Data:\nfound in Foo.cs"));
        }

        [Test]
        public void Render_DoesNotExpandPlaceholdersInsideAnAnswer()
        {
            // One pass over the template: the answer must reach the next step exactly as the agent wrote it.
            var text = SubAgentPipelinePrompt.Render("{{prev}}", "the task", "see {{task}} and {{prev}}", null);

            Assert.That(text, Is.EqualTo("see {{task}} and {{prev}}"));
        }

        [Test]
        public void Render_LeavesUnknownPlaceholdersAsTheyAre()
        {
            var text = SubAgentPipelinePrompt.Render("{{task}} {{unknown}} {{steps.missing}}", "t", "p", null);

            Assert.That(text, Is.EqualTo("t {{unknown}} {{steps.missing}}"));
        }

        [Test]
        public void Render_NullPrev_BecomesEmptyText()
        {
            var text = SubAgentPipelinePrompt.Render("a{{prev}}b", "t", null, null);

            Assert.That(text, Is.EqualTo("ab"));
        }

        [Test]
        public void Render_EmptyTemplate_ReturnsEmpty()
        {
            Assert.That(SubAgentPipelinePrompt.Render(null, "t", "p", null), Is.Empty);
        }

        [Test]
        public void FindStepRefs_ReturnsIdsInOrderWithoutDuplicates()
        {
            var refs = SubAgentPipelinePlaceholders.FindStepRefs("{{steps.b}} {{steps.a}} {{steps.b}}");

            Assert.That(refs, Is.EqualTo(new[] { "b", "a" }));
        }

        [Test]
        public void ContainsPrev_DetectsThePlaceholder()
        {
            Assert.That(SubAgentPipelinePlaceholders.ContainsPrev("x {{prev}} y"), Is.True);
            Assert.That(SubAgentPipelinePlaceholders.ContainsPrev("x {{task}} y"), Is.False);
            Assert.That(SubAgentPipelinePlaceholders.ContainsPrev(null), Is.False);
        }

        [Test]
        public void BuildHeading_UsesIndexTotalAndLabel()
        {
            Assert.That(SubAgentPipelinePrompt.BuildHeading(2, 3, "analyze"), Is.EqualTo("### [2/3 analyze]"));
        }
    }
}
