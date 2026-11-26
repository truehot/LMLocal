using System;
using System.Reflection;
using LMLocal.Core.Common;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    [TestFixture]
    public class InternalLoggerTests
    {
        private IInternalLogger _original;
        private FieldInfo _instanceField;
        private readonly Exception _warnException = new InvalidOperationException("warn-with-exception");

        [SetUp]
        public void SetUp()
        {
            _instanceField = typeof(InternalLogger).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);
            _original = (IInternalLogger)_instanceField.GetValue(null);
        }

        [TearDown]
        public void TearDown()
        {
            // restore original logger
            try
            {
                InternalLogger.SetLogger(_original);
            }
            catch
            {
                // ignore
            }
        }

        [Test]
        public void SetLogger_Null_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => InternalLogger.SetLogger(null));
        }

        [Test]
        public void SafeLog_DoesNotThrow_WhenLoggerThrows()
        {
            var throwing = new ThrowingLogger();
            InternalLogger.SetLogger(throwing);

            // Calls are Conditional on DEBUG; in Debug build these should be executed and swallowed by SafeLog
            Assert.DoesNotThrow(() =>
            {
                InternalLogger.Info("i");
                InternalLogger.Debug("d");
                InternalLogger.Warn("w");
                InternalLogger.Warn("w", new Exception("ex"));
                InternalLogger.Error("e", new Exception("ex"));
            });
        }

#if DEBUG
        // InternalLogger's members are [Conditional("DEBUG")]: the call sites below are emitted only when the
        // calling assembly (this test project) is compiled with DEBUG. In a Release build they are removed by the
        // compiler, so delegation cannot be observed. The whole test is therefore Debug-only by construction.
        [Test]
        public void SetLogger_DelegatesCalls_ToProvidedLogger()
        {
            var mock = new Mock<IInternalLogger>();
            InternalLogger.SetLogger(mock.Object);

            InternalLogger.Info("info-msg");
            InternalLogger.Warn("warn-msg");
            InternalLogger.Warn("warn-msg", _warnException);
            InternalLogger.Error("err-msg", null);
            InternalLogger.Debug("dbg-msg");

            mock.Verify(m => m.Info("info-msg"), Times.Once);
            mock.Verify(m => m.Warn("warn-msg"), Times.Once);
            mock.Verify(m => m.Warn("warn-msg", _warnException), Times.Once);
            mock.Verify(m => m.Error("err-msg", null), Times.Once);
            mock.Verify(m => m.Debug("dbg-msg"), Times.Once);
        }
#endif

        private class ThrowingLogger : IInternalLogger
        {
            public void Debug(string message) => throw new Exception("boom");
            public void Info(string message) => throw new Exception("boom");
            public void Warn(string message) => throw new Exception("boom");
            public void Warn(string message, Exception ex) => throw new Exception("boom");
            public void Error(string message, Exception ex = null) => throw new Exception("boom");
        }
    }
}
