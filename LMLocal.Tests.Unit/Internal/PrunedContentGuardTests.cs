using System.Collections.Generic;
using LMLocal.Application.Chat;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    /// <summary>
    /// A model that copies a history marker into a new call would write the placeholder into the file, so the
    /// write tools refuse such a call before anything is created or modified.
    /// </summary>
    [TestFixture]
    public class PrunedContentGuardTests
    {
        [Test]
        public void Validate_NormalCall_IsAccepted()
        {
            var parameters = new Dictionary<string, object>
            {
                { "file_path", "src/A.cs" },
                { "content", "public class A { }" }
            };

            Assert.That(PrunedContentGuard.Validate(parameters), Is.Null);
        }

        [Test]
        public void Validate_NullParameters_IsAccepted()
        {
            Assert.That(PrunedContentGuard.Validate(null), Is.Null);
        }

        [Test]
        public void Validate_PrunedKeyOnTopLevel_IsRejected()
        {
            var parameters = new Dictionary<string, object>
            {
                { "file_path", "src/A.cs" },
                { ToolHistoryPruner.PrunedKey, true }
            };

            var error = PrunedContentGuard.Validate(parameters);

            Assert.That(error, Is.Not.Null);
            Assert.That(error, Does.Contain("compacted history"));
        }

        [Test]
        public void Validate_BodyCarryingTheMarker_IsRejected()
        {
            var parameters = new Dictionary<string, object>
            {
                { "file_path", "src/A.cs" },
                { "new_lines", ToolHistoryPruner.Marker + ": 640 lines\u27E7" }
            };

            Assert.That(PrunedContentGuard.Validate(parameters), Is.Not.Null);
        }

        [Test]
        public void Validate_TraceLineInContent_IsRejected()
        {
            var parameters = new Dictionary<string, object>
            {
                { "file_path", "src/A.cs" },
                { "content", ToolHistoryPruner.Marker + "\u27E7 read_file_lines src/A.cs \u2192 ok, 81" }
            };

            Assert.That(PrunedContentGuard.Validate(parameters), Is.Not.Null);
        }

        [Test]
        public void Validate_MarkerInsideUnrelatedText_IsNotFalselyTriggeredByCase()
        {
            // Only the exact marker triggors rejection; ordinary text mentioning pruning must still work.
            var parameters = new Dictionary<string, object>
            {
                { "file_path", "src/A.cs" },
                { "content", "// pruned: true in the settings object" }
            };

            Assert.That(PrunedContentGuard.Validate(parameters), Is.Null);
        }
    }
}
