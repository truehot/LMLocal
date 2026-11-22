using System;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    [TestFixture]
    public class DebugStopPolicyTests
    {
        [TestCase(DebugStopReason.Breakpoint, true, DebugStopKind.Breakpoint)]
        [TestCase(DebugStopReason.UserBreak, true, DebugStopKind.Breakpoint)]
        [TestCase(DebugStopReason.ExceptionThrown, true, DebugStopKind.Exception)]
        [TestCase(DebugStopReason.ExceptionNotHandled, true, DebugStopKind.Exception)]
        [TestCase(DebugStopReason.Step, false, DebugStopKind.None)]
        [TestCase(DebugStopReason.Go, false, DebugStopKind.None)]
        [TestCase(DebugStopReason.AttachProgram, false, DebugStopKind.None)]
        [TestCase(DebugStopReason.DetachProgram, false, DebugStopKind.None)]
        [TestCase(DebugStopReason.LaunchProgram, false, DebugStopKind.None)]
        [TestCase(DebugStopReason.EndProgram, false, DebugStopKind.None)]
        [TestCase(DebugStopReason.StopDebugging, false, DebugStopKind.None)]
        [TestCase(DebugStopReason.ContextSwitch, false, DebugStopKind.None)]
        [TestCase(DebugStopReason.None, false, DebugStopKind.None)]
        public void Policy_MatchesTheTable(DebugStopReason reason, bool expectedShow, DebugStopKind expectedKind)
        {
            Assert.Multiple(() =>
            {
                Assert.That(DebugStopPolicy.ShouldShow(reason), Is.EqualTo(expectedShow), "ShouldShow");
                Assert.That(DebugStopPolicy.GetKind(reason), Is.EqualTo(expectedKind), "GetKind");
            });
        }

        [Test]
        public void ShouldShow_IsConsistentWithGetKind_ForEveryReason()
        {
            foreach (DebugStopReason reason in Enum.GetValues(typeof(DebugStopReason)))
            {
                Assert.That(
                    DebugStopPolicy.ShouldShow(reason),
                    Is.EqualTo(DebugStopPolicy.GetKind(reason) != DebugStopKind.None),
                    $"ShouldShow and GetKind disagree for {reason}.");
            }
        }

        [Test]
        public void ShouldShow_IsTrueOnlyForBreakpointsExceptionsAndUserBreaks()
        {
            foreach (DebugStopReason reason in Enum.GetValues(typeof(DebugStopReason)))
            {
                bool expected = reason == DebugStopReason.Breakpoint
                                || reason == DebugStopReason.UserBreak
                                || reason == DebugStopReason.ExceptionThrown
                                || reason == DebugStopReason.ExceptionNotHandled;

                Assert.That(DebugStopPolicy.ShouldShow(reason), Is.EqualTo(expected), $"reason {reason}");
            }
        }

        [Test]
        public void Step_IsNeverShown()
        {
            Assert.That(DebugStopPolicy.ShouldShow(DebugStopReason.Step), Is.False,
                "a step (F10/F11) must not raise the banner");
        }

        [TestCase("dbgEventReasonBreakpoint", DebugStopReason.Breakpoint)]
        [TestCase("dbgEventReasonExceptionThrown", DebugStopReason.ExceptionThrown)]
        [TestCase("dbgEventReasonExceptionNotHandled", DebugStopReason.ExceptionNotHandled)]
        [TestCase("dbgEventReasonUserBreak", DebugStopReason.UserBreak)]
        [TestCase("dbgEventReasonStep", DebugStopReason.Step)]
        [TestCase("dbgEventReasonGo", DebugStopReason.Go)]
        [TestCase("dbgEventReasonAttachProgram", DebugStopReason.AttachProgram)]
        [TestCase("dbgEventReasonDetachProgram", DebugStopReason.DetachProgram)]
        [TestCase("dbgEventReasonLaunchProgram", DebugStopReason.LaunchProgram)]
        [TestCase("dbgEventReasonEndProgram", DebugStopReason.EndProgram)]
        [TestCase("dbgEventReasonStopDebugging", DebugStopReason.StopDebugging)]
        [TestCase("dbgEventReasonContextSwitch", DebugStopReason.ContextSwitch)]
        [TestCase("dbgEventReasonNone", DebugStopReason.None)]
        [TestCase("  dbgEventReasonBreakpoint  ", DebugStopReason.Breakpoint)]
        [TestCase("somethingElse", DebugStopReason.None)]
        [TestCase("", DebugStopReason.None)]
        [TestCase(null, DebugStopReason.None)]
        public void FromEventReasonName_MatchesTheAutomationEnum(string reasonName, DebugStopReason expected)
        {
            Assert.That(DebugStopPolicy.FromEventReasonName(reasonName), Is.EqualTo(expected));
        }

        [Test]
        public void FromEventReasonName_CoversEveryReasonTheEnumCanHold()
        {
            foreach (DebugStopReason reason in Enum.GetValues(typeof(DebugStopReason)))
                Assert.That(DebugStopPolicy.FromEventReasonName("dbgEventReason" + reason),
                    Is.EqualTo(reason), $"the name of {reason} is not mapped");
        }
    }
}
