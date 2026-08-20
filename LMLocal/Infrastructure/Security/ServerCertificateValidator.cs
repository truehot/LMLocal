using System;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace LMLocal.Infrastructure.Security
{
    /// <summary>
    /// Validates a server certificate during the TLS handshake.
    /// </summary>
    internal interface IServerCertificateValidator
    {
        bool Validate(X509Certificate2 serverCertificate, SslPolicyErrors errors, CertificateInfo expectedCertificate);
    }

    internal sealed class ServerCertificateValidator : IServerCertificateValidator
    {
        private readonly Func<DateTime> _nowProvider;
        private readonly bool _checkValidityPeriod;

        public ServerCertificateValidator()
            : this(null, true)
        {
        }

        public ServerCertificateValidator(Func<DateTime> nowProvider, bool checkValidityPeriod = true)
        {
            _nowProvider = nowProvider ?? (() => DateTime.Now);
            _checkValidityPeriod = checkValidityPeriod;
        }

        public bool Validate(X509Certificate2 serverCertificate, SslPolicyErrors errors, CertificateInfo expectedCertificate)
        {
            if (errors == SslPolicyErrors.None)
            {
                return true;
            }

            if (serverCertificate == null)
            {
                return false;
            }

            return ValidateCore(
                serverCertificate.Thumbprint,
                serverCertificate.NotBefore,
                serverCertificate.NotAfter,
                errors,
                expectedCertificate);
        }

        /// <summary>
        /// Core validation split from the X509Certificate2 dependency for unit testing.
        /// </summary>
        internal bool ValidateCore(
            string serverThumbprint,
            DateTime serverNotBefore,
            DateTime serverNotAfter,
            SslPolicyErrors errors,
            CertificateInfo expectedCertificate)
        {
            if (errors == SslPolicyErrors.None)
            {
                return true;
            }

            if (expectedCertificate == null)
            {
                return false;
            }

            if (!ThumbprintMatches(serverThumbprint, expectedCertificate.Thumbprint))
            {
                return false;
            }

            if (_checkValidityPeriod)
            {
                DateTime now = _nowProvider().ToUniversalTime();
                DateTime notBefore = serverNotBefore.ToUniversalTime();
                DateTime notAfter = serverNotAfter.ToUniversalTime();

                if (now < notBefore || now > notAfter)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Compares two certificate thumbprints case-insensitively.
        /// </summary>
        internal static bool ThumbprintMatches(string serverThumbprint, string expectedThumbprint)
        {
            if (string.IsNullOrWhiteSpace(serverThumbprint) || string.IsNullOrWhiteSpace(expectedThumbprint))
            {
                return false;
            }

            return string.Equals(serverThumbprint, expectedThumbprint, StringComparison.OrdinalIgnoreCase);
        }
    }
}
