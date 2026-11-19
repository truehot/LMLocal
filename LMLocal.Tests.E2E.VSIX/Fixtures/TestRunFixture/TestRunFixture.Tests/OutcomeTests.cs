using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TestRunFixture.Tests
{
    // Read by IpcRunTestsToolTests: names and the failure message are asserted there, keep them in sync.
    [TestClass]
    public class OutcomeTests
    {
        // The one failure run_tests must report with its name and message.
        [TestMethod]
        public void Fails_WithKnownMessage()
        {
            Assert.Fail("expected-failure-marker");
        }

        // Passes, but "Failed" in the name made the old parser start a failure block here (2026-11-18 E2E run).
        [TestMethod]
        public void Retry_AfterFailedSave_Passes()
        {
            Assert.AreEqual(2, 1 + 1);
        }

        [TestMethod]
        public void Plain_Passes()
        {
            Assert.IsTrue(true);
        }
    }
}
