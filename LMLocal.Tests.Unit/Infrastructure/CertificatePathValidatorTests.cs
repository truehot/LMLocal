using System;
using System.IO;
using LMLocal.Infrastructure.Security;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    [TestFixture]
    public class CertificatePathValidatorTests
    {
        private static readonly DateTime Now = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static readonly CertificateInfo ValidInfo = new CertificateInfo(
            "AABBCCDD",
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2034, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            "CN=test",
            "CN=issuer");

        private static readonly CertificateInfo ExpiredInfo = new CertificateInfo(
            "AABBCCDD",
            new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            "CN=test",
            "CN=issuer");

        private static readonly CertificateInfo NotYetValidInfo = new CertificateInfo(
            "AABBCCDD",
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2036, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            "CN=test",
            "CN=issuer");

        [Test]
        public void Constructor_NullCertificateLoader_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new CertificatePathValidator(null));
        }

        [Test]
        public void ValidateOrThrow_EmptyPath_ReturnsNullWithoutIo()
        {
            var loader = new Mock<IX509CertificateLoader>();
            var validator = new CertificatePathValidator(loader.Object, () => Now);

            var result = validator.ValidateOrThrow("");

            Assert.That(result, Is.Null);
            loader.Verify(l => l.LoadCertificateInfo(It.IsAny<string>()), Times.Never);
        }

        [Test]
        public void ValidateOrThrow_MissingFile_ThrowsFileNotFound()
        {
            string missingPath = Path.Combine(Path.GetTempPath(), "missing_" + Guid.NewGuid().ToString("N") + ".cer");
            var loader = new Mock<IX509CertificateLoader>();
            var validator = new CertificatePathValidator(loader.Object, () => Now);

            var ex = Assert.Throws<CertificatePathException>(() => validator.ValidateOrThrow(missingPath));

            Assert.That(ex.Message, Does.Contain("file not found"));
            Assert.That(ex.Message, Does.Contain(missingPath));
            loader.Verify(l => l.LoadCertificateInfo(It.IsAny<string>()), Times.Never);
        }

        [Test]
        public void ValidateOrThrow_InvalidFile_ThrowsNotValidCertificate()
        {
            string path = Path.Combine(Path.GetTempPath(), "invalid_" + Guid.NewGuid().ToString("N") + ".cer");
            File.WriteAllText(path, "not a certificate");
            try
            {
                var loader = new Mock<IX509CertificateLoader>();
                loader.Setup(l => l.LoadCertificateInfo(path)).Returns((CertificateInfo)null);
                var validator = new CertificatePathValidator(loader.Object, () => Now);

                var ex = Assert.Throws<CertificatePathException>(() => validator.ValidateOrThrow(path));

                Assert.That(ex.Message, Does.Contain("not a valid certificate"));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void ValidateOrThrow_ExpiredCertificate_ThrowsExpired()
        {
            string path = Path.Combine(Path.GetTempPath(), "expired_" + Guid.NewGuid().ToString("N") + ".cer");
            File.WriteAllText(path, "placeholder");
            try
            {
                var loader = new Mock<IX509CertificateLoader>();
                loader.Setup(l => l.LoadCertificateInfo(path)).Returns(ExpiredInfo);
                var validator = new CertificatePathValidator(loader.Object, () => Now);

                var ex = Assert.Throws<CertificatePathException>(() => validator.ValidateOrThrow(path));

                Assert.That(ex.Message, Does.Contain("certificate expired"));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void ValidateOrThrow_NotYetValidCertificate_ThrowsNotYetValid()
        {
            string path = Path.Combine(Path.GetTempPath(), "notyet_" + Guid.NewGuid().ToString("N") + ".cer");
            File.WriteAllText(path, "placeholder");
            try
            {
                var loader = new Mock<IX509CertificateLoader>();
                loader.Setup(l => l.LoadCertificateInfo(path)).Returns(NotYetValidInfo);
                var validator = new CertificatePathValidator(loader.Object, () => Now);

                var ex = Assert.Throws<CertificatePathException>(() => validator.ValidateOrThrow(path));

                Assert.That(ex.Message, Does.Contain("certificate not yet valid"));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void ValidateOrThrow_ValidCertificate_ReturnsInfo()
        {
            string path = Path.Combine(Path.GetTempPath(), "valid_" + Guid.NewGuid().ToString("N") + ".cer");
            File.WriteAllText(path, "placeholder");
            try
            {
                var loader = new Mock<IX509CertificateLoader>();
                loader.Setup(l => l.LoadCertificateInfo(path)).Returns(ValidInfo);
                var validator = new CertificatePathValidator(loader.Object, () => Now);

                CertificateInfo result = validator.ValidateOrThrow(path);

                Assert.That(result, Is.SameAs(ValidInfo));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}