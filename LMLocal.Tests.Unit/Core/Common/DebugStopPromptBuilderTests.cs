using System;
using System.Collections.Generic;
using System.Linq;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    [TestFixture]
    public class DebugStopPromptBuilderTests
    {
        // ────────────────────────────── helpers ──────────────────────────────

        private const string FullPath = @"C:\Solution\LMLocal\OrderService.cs";
        private const string RelativePath = @"LMLocal\OrderService.cs";

        private static Func<string, string> Relative => _ => RelativePath;

        private static DebugStopInfo Info(
            DebugStopReason reason = DebugStopReason.Breakpoint,
            DebugStopKind kind = DebugStopKind.Breakpoint,
            string file = FullPath,
            int line = 42)
        {
            return new DebugStopInfo { Reason = reason, Kind = kind, File = file, Line = line };
        }

        private static DebugStopContext FullContext()
        {
            return new DebugStopContext
            {
                Info = Info(DebugStopReason.ExceptionThrown, DebugStopKind.Exception),
                Exceptions = new List<DebugExceptionInfo>
                {
                    new DebugExceptionInfo
                    {
                        Type = "System.NullReferenceException",
                        Message = "Object reference not set to an instance of an object."
                    },
                    new DebugExceptionInfo
                    {
                        Type = "System.InvalidOperationException",
                        Message = "Sequence contains no elements."
                    }
                },
                Frames = new List<DebugStackFrameInfo>
                {
                    new DebugStackFrameInfo { FunctionName = "OrderService.Process", File = FullPath, Line = 42 },
                    new DebugStackFrameInfo { FunctionName = "Program.Main" }
                },
                Locals = new List<DebugLocalVariable>
                {
                    new DebugLocalVariable { Name = "order", Type = "Order", Value = "{Order}" },
                    new DebugLocalVariable { Name = "count", Type = "int", Value = "42" },
                    new DebugLocalVariable { Name = "password", Type = "string", Value = "hunter2" }
                }
            };
        }

        // ────────────────────────────── nothing to send ──────────────────────────────

        [Test]
        public void Build_NullContext_ReturnsNull()
        {
            Assert.That(DebugStopPromptBuilder.Build(null, Relative), Is.Null);
        }

        [Test]
        public void Build_ContextWithoutInfo_ReturnsNull()
        {
            Assert.That(DebugStopPromptBuilder.Build(new DebugStopContext(), Relative), Is.Null);
        }

        // ────────────────────────────── exception stop ──────────────────────────────

        [Test]
        public void Build_ExceptionWithInnerChainAndLocals_ContainsEverySection()
        {
            string result = DebugStopPromptBuilder.Build(FullContext(), Relative);

            Assert.Multiple(() =>
            {
                Assert.That(result, Does.StartWith("Explain this Visual Studio debugger stop."));
                Assert.That(result, Does.Contain("Find the root cause"));
                Assert.That(result, Does.Contain("Stop: break mode on an exception"));
                Assert.That(result, Does.Contain(@"Location: LMLocal\OrderService.cs, line 42"));
                Assert.That(result, Does.Contain(
                    "Exception:\n" +
                    "1. System.NullReferenceException: Object reference not set to an instance of an object.\n" +
                    "2. Inner: System.InvalidOperationException: Sequence contains no elements."));
                Assert.That(result, Does.Contain(
                    "Stack:\n" +
                    @"1. OrderService.Process at LMLocal\OrderService.cs:42" + "\n" +
                    "2. Program.Main"));
                Assert.That(result, Does.Contain(
                    "Locals of the current frame:\n" +
                    "- order (Order): {Order}\n" +
                    "- count (int): 42\n" +
                    "- password (string): <hidden>"));
            });

            Assert.That(result, Does.Not.Contain("hunter2"), "a credential must never reach the prompt");
        }

        [Test]
        public void Build_ExceptionStop_AsksNotToApplyTheFix()
        {
            string result = DebugStopPromptBuilder.Build(FullContext(), Relative);

            Assert.Multiple(() =>
            {
                Assert.That(result, Does.Contain("do not apply any change"));
                Assert.That(result, Does.Contain("do not output a diff"));
                Assert.That(result, Does.Contain("Read the code around the reported location"));
            });
        }

        // ────────────────────────────── breakpoint stop ──────────────────────────────

        [Test]
        public void Build_BreakpointWithoutException_AsksForAnAnalysisInsteadOfABug()
        {
            var context = new DebugStopContext { Info = Info(line: 12) };

            string result = DebugStopPromptBuilder.Build(context, Relative);

            Assert.Multiple(() =>
            {
                Assert.That(result, Does.Contain("Visual Studio stopped at a breakpoint"));
                Assert.That(result, Does.Not.Contain("because an exception was thrown"));
                Assert.That(result, Does.Contain("If nothing looks wrong, say so instead of inventing a bug"));
                Assert.That(result, Does.Contain("Stop: break mode on a breakpoint"));
                Assert.That(result, Does.Contain(@"Location: LMLocal\OrderService.cs, line 12"));
                Assert.That(result, Does.Not.Contain("Exception:"));
                Assert.That(result, Does.Not.Contain("Stack:"));
                Assert.That(result, Does.Not.Contain("Locals"));
            });
        }

        [Test]
        public void Build_UserBreak_IsPresentedAsABreakpoint()
        {
            var context = new DebugStopContext { Info = Info(DebugStopReason.UserBreak, DebugStopKind.Breakpoint) };

            string result = DebugStopPromptBuilder.Build(context, null);

            Assert.That(result, Does.Contain("Stop: break mode on a user break"));
            Assert.That(result, Does.Not.Contain("because an exception was thrown"));
        }

        // ────────────────────────────── degraded input ──────────────────────────────

        [Test]
        public void Build_InfoWithoutAnyContext_ReturnsTheMinimalPrompt()
        {
            var context = new DebugStopContext
            {
                Info = Info(DebugStopReason.ExceptionNotHandled, DebugStopKind.Exception, line: 7)
            };

            string result = DebugStopPromptBuilder.Build(context, null);

            Assert.Multiple(() =>
            {
                Assert.That(result, Does.StartWith("Explain this Visual Studio debugger stop."));
                Assert.That(result, Does.Contain("Stop: break mode on an unhandled exception"));
                Assert.That(result, Does.Contain(@"Location: C:\Solution\LMLocal\OrderService.cs, line 7"),
                    "without a converter the full path is shown");
                Assert.That(result, Does.Not.Contain("Exception:"));
                Assert.That(result, Does.Not.Contain("Stack:"));
                Assert.That(result, Does.Not.Contain("Locals"));
            });
        }

        [Test]
        public void Build_FileIsNull_LocationIsUnknown()
        {
            var context = new DebugStopContext { Info = Info(file: null) };

            string result = DebugStopPromptBuilder.Build(context, Relative);

            Assert.That(result, Does.Contain("Location: unknown"));
        }

        [Test]
        public void Build_ZeroLine_OmitsTheLineNumber()
        {
            var context = new DebugStopContext { Info = Info(line: 0) };

            string result = DebugStopPromptBuilder.Build(context, Relative);

            Assert.That(result, Does.Contain(@"Location: LMLocal\OrderService.cs"));
            Assert.That(result, Does.Not.Contain(", line"));
        }

        [Test]
        public void Build_ThrowingPathConverter_FallsBackToTheFullPath()
        {
            string result = DebugStopPromptBuilder.Build(
                new DebugStopContext { Info = Info() },
                _ => throw new InvalidOperationException("no solution"));

            Assert.That(result, Does.Contain(@"Location: C:\Solution\LMLocal\OrderService.cs, line 42"));
        }

        [Test]
        public void Build_EmptyConverterResult_FallsBackToTheFullPath()
        {
            string result = DebugStopPromptBuilder.Build(new DebugStopContext { Info = Info() }, _ => "   ");

            Assert.That(result, Does.Contain(@"Location: C:\Solution\LMLocal\OrderService.cs, line 42"));
        }

        [Test]
        public void Build_ExceptionWithoutType_StillPrintsTheMessage()
        {
            var context = new DebugStopContext
            {
                Info = Info(DebugStopReason.ExceptionThrown, DebugStopKind.Exception),
                Exceptions = new List<DebugExceptionInfo> { new DebugExceptionInfo { Message = "boom" } }
            };

            string result = DebugStopPromptBuilder.Build(context, null);

            Assert.That(result, Does.Contain("1. (unknown type): boom"));
        }

        [Test]
        public void Build_ExceptionWithoutMessage_StillPrintsTheType()
        {
            var context = new DebugStopContext
            {
                Info = Info(DebugStopReason.ExceptionThrown, DebugStopKind.Exception),
                Exceptions = new List<DebugExceptionInfo> { new DebugExceptionInfo { Type = "System.NullReferenceException" } }
            };

            string result = DebugStopPromptBuilder.Build(context, null);

            Assert.That(result, Does.Contain("1. System.NullReferenceException"));
            Assert.That(result, Does.Not.Contain("System.NullReferenceException:"));
        }

        [Test]
        public void Build_EmptyExceptionEntry_IsSkipped()
        {
            var context = new DebugStopContext
            {
                Info = Info(DebugStopReason.ExceptionThrown, DebugStopKind.Exception),
                Exceptions = new List<DebugExceptionInfo> { new DebugExceptionInfo(), null }
            };

            string result = DebugStopPromptBuilder.Build(context, null);

            Assert.That(result, Does.Not.Contain("Exception:"));
        }

        // ────────────────────────────── limits ──────────────────────────────

        [Test]
        public void Build_MoreThanFiveFrames_KeepsTheFirstFive()
        {
            var frames = new List<DebugStackFrameInfo>();
            for (int i = 0; i < 9; i++)
                frames.Add(new DebugStackFrameInfo { FunctionName = "Frame" + i });

            string result = DebugStopPromptBuilder.Build(new DebugStopContext { Info = Info(), Frames = frames }, null);

            Assert.That(result.Split('\n').Count(line => line.StartsWith("1. ") || line.StartsWith("2. ")
                || line.StartsWith("3. ") || line.StartsWith("4. ") || line.StartsWith("5. ")
                || line.StartsWith("6. ")), Is.EqualTo(DebugStopPromptBuilder.MaxFrames));
            Assert.That(result, Does.Contain("5. Frame4"));
            Assert.That(result, Does.Not.Contain("Frame5"));
        }

        [Test]
        public void Build_MoreThanThreeExceptions_KeepsTheFirstThree()
        {
            var exceptions = new List<DebugExceptionInfo>();
            for (int i = 0; i < 5; i++)
                exceptions.Add(new DebugExceptionInfo { Type = "E" + i, Message = "m" + i });

            string result = DebugStopPromptBuilder.Build(
                new DebugStopContext { Info = Info(DebugStopReason.ExceptionThrown, DebugStopKind.Exception), Exceptions = exceptions },
                null);

            Assert.That(result, Does.Contain("3. Inner: E2: m2"));
            Assert.That(result, Does.Not.Contain("E3"));
        }

        [Test]
        public void Build_MoreThanTwentyLocals_KeepsTheFirstTwenty()
        {
            var locals = new List<DebugLocalVariable>();
            for (int i = 0; i < 25; i++)
                locals.Add(new DebugLocalVariable { Name = "v" + i, Type = "int", Value = i.ToString() });

            string result = DebugStopPromptBuilder.Build(new DebugStopContext { Info = Info(), Locals = locals }, null);

            Assert.That(result.Split('\n').Count(line => line.StartsWith("- ")),
                Is.EqualTo(DebugStopPromptBuilder.MaxLocals));
            Assert.That(result, Does.Contain("- v19 (int): 19"));
            Assert.That(result, Does.Not.Contain("- v20 "));
        }

        [Test]
        public void Build_LongValue_IsTruncated()
        {
            var locals = new List<DebugLocalVariable>
            {
                new DebugLocalVariable { Name = "payload", Type = "string", Value = new string('x', 250) }
            };

            string result = DebugStopPromptBuilder.Build(new DebugStopContext { Info = Info(), Locals = locals }, null);

            Assert.That(result, Does.Contain(new string('x', DebugStopPromptBuilder.MaxValueLength) + "..."));
            Assert.That(result, Does.Not.Contain(new string('x', DebugStopPromptBuilder.MaxValueLength + 1)));
        }

        [Test]
        public void Build_ValueNotRead_ShowsTheTypeOnly()
        {
            var locals = new List<DebugLocalVariable> { new DebugLocalVariable { Name = "order", Type = "Order" } };

            string result = DebugStopPromptBuilder.Build(new DebugStopContext { Info = Info(), Locals = locals }, null);

            Assert.That(result, Does.Contain("- order (Order)"));
            Assert.That(result, Does.Not.Contain("- order (Order):"));
        }

        [TestCase("userPassword")]
        [TestCase("api_key")]
        [TestCase("ConnectionString")]
        [TestCase("authToken")]
        [TestCase("client_secret")]
        public void Build_SensitiveName_IsHidden(string name)
        {
            var locals = new List<DebugLocalVariable>
            {
                new DebugLocalVariable { Name = name, Type = "string", Value = "s3cr3t" }
            };

            string result = DebugStopPromptBuilder.Build(new DebugStopContext { Info = Info(), Locals = locals }, null);

            Assert.That(result, Does.Contain($"- {name} (string): {DebugLocalVariable.HiddenValue}"));
            Assert.That(result, Does.Not.Contain("s3cr3t"));
        }

        [Test]
        public void Build_LocalWithoutName_IsSkipped()
        {
            var locals = new List<DebugLocalVariable>
            {
                null,
                new DebugLocalVariable { Name = "  ", Type = "int", Value = "1" }
            };

            string result = DebugStopPromptBuilder.Build(new DebugStopContext { Info = Info(), Locals = locals }, null);

            Assert.That(result, Does.Not.Contain("Locals"));
        }

        [Test]
        public void Build_MultiLineExceptionMessage_IsFlattenedToOneLine()
        {
            var context = new DebugStopContext
            {
                Info = Info(DebugStopReason.ExceptionThrown, DebugStopKind.Exception),
                Exceptions = new List<DebugExceptionInfo>
                {
                    new DebugExceptionInfo { Type = "System.Data.SqlClient.SqlException", Message = "line one\r\nline two\tescaped\\r\\nthree" }
                }
            };

            string result = DebugStopPromptBuilder.Build(context, null);

            Assert.That(result, Does.Contain("1. System.Data.SqlClient.SqlException: line one line two escaped three"));
            Assert.That(result, Does.Not.Contain("line one\r\n"));
        }

        [Test]
        public void Build_LongExceptionMessage_IsTruncated()
        {
            var context = new DebugStopContext
            {
                Info = Info(DebugStopReason.ExceptionThrown, DebugStopKind.Exception),
                Exceptions = new List<DebugExceptionInfo>
                {
                    new DebugExceptionInfo { Type = "System.Exception", Message = new string('m', 900) }
                }
            };

            string result = DebugStopPromptBuilder.Build(context, null);

            Assert.That(result, Does.Contain(new string('m', DebugStopPromptBuilder.MaxMessageLength) + "..."));
            Assert.That(result, Does.Not.Contain(new string('m', DebugStopPromptBuilder.MaxMessageLength + 1)));
        }

        [Test]
        public void Build_LocalValueWithLineBreaks_IsFlattened()
        {
            var locals = new List<DebugLocalVariable>
            {
                new DebugLocalVariable { Name = "json", Type = "string", Value = "\"{\\r\\n  \"\"a\"\": 1\\r\\n}\"" }
            };

            string result = DebugStopPromptBuilder.Build(new DebugStopContext { Info = Info(), Locals = locals }, null);

            Assert.That(result, Does.Contain("- json (string): \"{ \"\"a\"\": 1 }\""));
        }
    }
}
