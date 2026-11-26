using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Common
{
    [TestFixture]
    public class CommandLineEscaperTests
    {
        [DllImport("shell32.dll", EntryPoint = "CommandLineToArgvW", CharSet = CharSet.Unicode)]
        private static extern IntPtr CommandLineToArgvW(string commandLine, out int count);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr handle);

        [TestCase("simple")]
        [TestCase("with space")]
        [TestCase("with\"quote")]
        [TestCase("ends with backslash\\")]
        [TestCase("a\\\"b")]
        [TestCase("")]
        [TestCase("кириллица в пути")]
        [TestCase("tab\there")]
        public void Escape_RoundTripsThroughCommandLineToArgvW(string argument)
        {
            // CommandLineToArgvW parses argv[0] with special rules; our escaped strings are always
            // arguments after the executable, so keep a dummy first token in the round-trip test.
            string commandLine = CommandLineEscaper.Join("git", argument);

            Assert.That(Parse(commandLine), Is.EqualTo(new[] { "git", argument }));
        }

        [Test]
        public void Join_EscapesEveryArgument()
        {
            string commandLine = CommandLineEscaper.Join("git", "log", "--grep=fix \"bug\"", "src/my file.cs");

            Assert.That(Parse(commandLine), Is.EqualTo(
                new[] { "git", "log", "--grep=fix \"bug\"", "src/my file.cs" }));
        }

        private static string[] Parse(string commandLine)
        {
            int count;
            IntPtr pointer = CommandLineToArgvW(commandLine, out count);
            if (pointer == IntPtr.Zero)
                throw new InvalidOperationException("CommandLineToArgvW failed.");

            try
            {
                var result = new List<string>();
                for (int i = 0; i < count; i++)
                    result.Add(Marshal.PtrToStringUni(Marshal.ReadIntPtr(pointer, i * IntPtr.Size)));
                return result.ToArray();
            }
            finally
            {
                LocalFree(pointer);
            }
        }
    }
}
