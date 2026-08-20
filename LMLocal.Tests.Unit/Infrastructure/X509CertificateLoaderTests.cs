using System;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using LMLocal.Infrastructure.Security;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    [TestFixture]
    public class X509CertificateLoaderTests
    {
        [Test]
        public void DecodePemCertificate_ValidPemBlock_ReturnsDerBytes()
        {
            string pem = "-----BEGIN CERTIFICATE-----\nAQID\n-----END CERTIFICATE-----";
            byte[] der = X509CertificateLoader.DecodePemCertificate(pem);

            Assert.That(der, Is.Not.Null);
            Assert.That(der, Is.EqualTo(new byte[] { 1, 2, 3 }));
        }

        [Test]
        public void DecodePemCertificate_NoPemBlock_ReturnsNull()
        {
            Assert.That(X509CertificateLoader.DecodePemCertificate("not a certificate"), Is.Null);
        }

        [Test]
        public void DecodePemCertificate_InvalidBase64_ReturnsNull()
        {
            string pem = "-----BEGIN CERTIFICATE-----\n!!!\n-----END CERTIFICATE-----";
            Assert.That(X509CertificateLoader.DecodePemCertificate(pem), Is.Null);
        }

        [Test]
        public void DecodePemCertificate_NullOrEmpty_ReturnsNull()
        {
            Assert.That(X509CertificateLoader.DecodePemCertificate(null), Is.Null);
            Assert.That(X509CertificateLoader.DecodePemCertificate(string.Empty), Is.Null);
        }

        [Test]
        public void TryLoadCertificate_InvalidFile_ReturnsFalse()
        {
            string path = Path.Combine(Path.GetTempPath(), "invalid_cert_" + Guid.NewGuid().ToString("N") + ".pem");
            File.WriteAllText(path, "not a certificate");

            try
            {
                Assert.That(new X509CertificateLoader().TryLoadCertificate(path, out X509Certificate2 certificate), Is.False);
                Assert.That(certificate, Is.Null);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void TryLoadCertificate_MissingFile_ReturnsFalse()
        {
            string path = Path.Combine(Path.GetTempPath(), "missing_cert_" + Guid.NewGuid().ToString("N") + ".pem");

            Assert.That(new X509CertificateLoader().TryLoadCertificate(path, out X509Certificate2 certificate), Is.False);
            Assert.That(certificate, Is.Null);
        }

        [Test]
        public void TryLoadCertificate_EmptyPath_ReturnsFalse()
        {
            Assert.That(new X509CertificateLoader().TryLoadCertificate(null, out X509Certificate2 certificate), Is.False);
            Assert.That(new X509CertificateLoader().TryLoadCertificate("   ", out certificate), Is.False);
        }

        [Test]
        public void LoadCertificateInfo_InvalidFile_ReturnsNull()
        {
            string path = Path.Combine(Path.GetTempPath(), "invalid_cert_info_" + Guid.NewGuid().ToString("N") + ".pem");
            File.WriteAllText(path, "not a certificate");

            try
            {
                Assert.That(new X509CertificateLoader().LoadCertificateInfo(path), Is.Null);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void LoadCertificateInfo_EmptyPath_ReturnsNull()
        {
            Assert.That(new X509CertificateLoader().LoadCertificateInfo(null), Is.Null);
            Assert.That(new X509CertificateLoader().LoadCertificateInfo("   "), Is.Null);
        }
    }
}
