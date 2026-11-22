using LMLocal.Core.Models;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    [TestFixture]
    public class DebugStopInfoTests
    {
        private static DebugStopInfo Stop(
            DebugStopReason reason = DebugStopReason.Breakpoint,
            string file = @"C:\Solution\LMLocal\OrderService.cs",
            int line = 42)
        {
            return new DebugStopInfo
            {
                Reason = reason,
                Kind = reason == DebugStopReason.ExceptionThrown ? DebugStopKind.Exception : DebugStopKind.Breakpoint,
                File = file,
                Line = line
            };
        }

        [Test]
        public void Equals_IsTrueForTheSameReasonFileAndLine()
        {
            var a = Stop();
            var b = Stop();

            Assert.That(a.Equals(b), Is.True);
            Assert.That(a.Equals((object)b), Is.True);
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        }

        [Test]
        public void Equals_IgnoresTheKind()
        {
            var a = Stop();
            var b = Stop();

            a.Kind = DebugStopKind.Exception;

            Assert.That(a.Equals(b), Is.True, "the kind is derived from the reason and does not make a new stop");
        }

        [Test]
        public void Equals_IsFalseForAnotherLine()
        {
            Assert.That(Stop(line: 42).Equals(Stop(line: 43)), Is.False);
        }

        [Test]
        public void Equals_IsFalseForAnotherFile()
        {
            Assert.That(Stop(file: @"C:\One.cs").Equals(Stop(file: @"C:\Two.cs")), Is.False);
        }

        [Test]
        public void Equals_IsFalseForAnotherReason()
        {
            Assert.That(Stop(DebugStopReason.Breakpoint).Equals(Stop(DebugStopReason.UserBreak)), Is.False);
        }

        [Test]
        public void Equals_IsCaseInsensitiveForTheFile()
        {
            var a = Stop(file: @"C:\Solution\OrderService.cs");
            var b = Stop(file: @"c:\solution\orderservice.cs");

            Assert.That(a.Equals(b), Is.True);
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
        }

        [Test]
        public void Equals_BothWithoutFile_IsTrue()
        {
            Assert.That(Stop(file: null).Equals(Stop(file: null)), Is.True);
        }

        [Test]
        public void Equals_Null_IsFalse()
        {
            Assert.That(Stop().Equals(null), Is.False);
            Assert.That(Stop().Equals((object)null), Is.False);
        }

        [Test]
        public void Equals_SameReference_IsTrue()
        {
            var a = Stop();
            Assert.That(a.Equals(a), Is.True);
        }
    }
}
