using System;
using System.Net.Security;
using LMLocal.Infrastructure.Security;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    [TestFixture]
    public class ServerCertificateValidatorTests
    {
        private static readonly CertificateInfo Expected = new CertificateInfo(
            "AABBCCDD",
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2034, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            "CN=test",
            "CN=issuer");

        private static readonly DateTime Now = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        [Test]
        public void Validate_NoErrors_ReturnsTrue()
        {
            var validator = new ServerCertificateValidator(() => Now);

            Assert.That(validator.Validate(null, SslPolicyErrors.None, null), Is.True);
        }

        [Test]
        public void Validate_NullCertificateWithErrors_ReturnsFalse()
        {
            var validator = new ServerCertificateValidator(() => Now);

            Assert.That(validator.Validate(null, SslPolicyErrors.RemoteCertificateChainErrors, Expected), Is.False);
        }

        [Test]
        public void ValidateCore_MatchingThumbprintWithinValidity_ReturnsTrue()
        {
            var validator = new ServerCertificateValidator(() => Now);

            bool result = validator.ValidateCore(
                "AABBCCDD",
                Expected.NotBefore,
                Expected.NotAfter,
                SslPolicyErrors.RemoteCertificateChainErrors,
                Expected);

            Assert.That(result, Is.True);
        }

        [Test]
        public void ValidateCore_MismatchedThumbprint_ReturnsFalse()
        {
            var validator = new ServerCertificateValidator(() => Now);

            bool result = validator.ValidateCore(
                "FFFFFFFF",
                Expected.NotBefore,
                Expected.NotAfter,
                SslPolicyErrors.RemoteCertificateNameMismatch,
                Expected);

            Assert.That(result, Is.False);
        }

        [Test]
        public void ValidateCore_MatchingThumbprintButExpired_ReturnsFalse()
        {
            var validator = new ServerCertificateValidator(() => Now);
            var expired = new CertificateInfo(
                "AABBCCDD",
                new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                "CN=test",
                "CN=issuer");

            bool result = validator.ValidateCore(
                "AABBCCDD",
                expired.NotBefore,
                expired.NotAfter,
                SslPolicyErrors.RemoteCertificateChainErrors,
                expired);

            Assert.That(result, Is.False);
        }

        [Test]
        public void ValidateCore_ValidityPeriodCheckCanBeDisabled()
        {
            var validator = new ServerCertificateValidator(() => Now, checkValidityPeriod: false);
            var expired = new CertificateInfo(
                "AABBCCDD",
                new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2021, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                "CN=test",
                "CN=issuer");

            bool result = validator.ValidateCore(
                "AABBCCDD",
                expired.NotBefore,
                expired.NotAfter,
                SslPolicyErrors.RemoteCertificateChainErrors,
                expired);

            Assert.That(result, Is.True);
        }

        [Test]
        public void ValidateCore_NullExpectedCertificate_ReturnsFalse()
        {
            var validator = new ServerCertificateValidator(() => Now);

            bool result = validator.ValidateCore(
                "AABBCCDD",
                Expected.NotBefore,
                Expected.NotAfter,
                SslPolicyErrors.RemoteCertificateChainErrors,
                null);

            Assert.That(result, Is.False);
        }

        [Test]
        public void ThumbprintMatches_IsCaseInsensitive()
        {
            Assert.That(ServerCertificateValidator.ThumbprintMatches("AABBCCDD", "aabbccdd"), Is.True);
        }

        [Test]
        public void ThumbprintMatches_DifferentThumbprints_ReturnsFalse()
        {
            Assert.That(ServerCertificateValidator.ThumbprintMatches("AABBCCDD", "DDEEFFGG"), Is.False);
        }

        [Test]
        public void ThumbprintMatches_EmptyOrNullValues_ReturnsFalse()
        {
            Assert.That(ServerCertificateValidator.ThumbprintMatches("", "DDEEFFGG"), Is.False);
            Assert.That(ServerCertificateValidator.ThumbprintMatches(null, "DDEEFFGG"), Is.False);
            Assert.That(ServerCertificateValidator.ThumbprintMatches("DDEEFFGG", ""), Is.False);
            Assert.That(ServerCertificateValidator.ThumbprintMatches("DDEEFFGG", null), Is.False);
        }
    }
}
