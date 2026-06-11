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
                InternalLogger.Error("e", new Exception("ex"));
            });
        }

        [Test]
        public void SetLogger_DelegatesCalls_ToProvidedLogger()
        {
            var mock = new Mock<IInternalLogger>();
            InternalLogger.SetLogger(mock.Object);

            InternalLogger.Info("info-msg");
            InternalLogger.Warn("warn-msg");
            InternalLogger.Error("err-msg", null);
            InternalLogger.Debug("dbg-msg");

            // Verify that provided logger received calls. In Debug build Conditional methods are active.
            mock.Verify(m => m.Info("info-msg"), Times.Once);
            mock.Verify(m => m.Warn("warn-msg"), Times.Once);
            mock.Verify(m => m.Error("err-msg", null), Times.Once);
            mock.Verify(m => m.Debug("dbg-msg"), Times.Once);
        }

        private class ThrowingLogger : IInternalLogger
        {
            public void Debug(string message) => throw new Exception("boom");
            public void Info(string message) => throw new Exception("boom");
            public void Warn(string message) => throw new Exception("boom");
            public void Error(string message, Exception ex = null) => throw new Exception("boom");
        }
    }
}
