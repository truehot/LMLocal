using System;
using System.Net;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.HttpWrapper;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    [TestFixture]
    public class ConnectionLimitsTests
    {
        // Each test uses its own host so ServicePoint state does not leak between tests.
        private static Uri UniqueHost() => new Uri($"https://lmlocal-{Guid.NewGuid():N}.invalid/v1/chat/completions");

        [Test]
        public void PerHost_CoversMaxParallelPlusHeadroom()
        {
            Assert.That(ConnectionLimits.PerHost, Is.GreaterThan(SubAgentsConfig.MaxParallelLimit));
        }

        [Test]
        public void EnsureForHost_LowLimit_IsRaised()
        {
            var uri = UniqueHost();
            ServicePointManager.FindServicePoint(uri).ConnectionLimit = 2;

            ConnectionLimits.EnsureForHost(uri);

            Assert.That(ServicePointManager.FindServicePoint(uri).ConnectionLimit, Is.EqualTo(ConnectionLimits.PerHost));
        }

        [Test]
        public void EnsureForHost_HigherLimit_IsNotLowered()
        {
            var uri = UniqueHost();
            ServicePointManager.FindServicePoint(uri).ConnectionLimit = ConnectionLimits.PerHost + 5;

            ConnectionLimits.EnsureForHost(uri);

            Assert.That(ServicePointManager.FindServicePoint(uri).ConnectionLimit, Is.EqualTo(ConnectionLimits.PerHost + 5));
        }

        [Test]
        public void Framework_LoopbackIsUnlimited_RemoteUsesDefaultLimit()
        {
            // Documents the .NET Framework behaviour ConnectionLimits relies on: only remote hosts are capped.
            var loopback = ServicePointManager.FindServicePoint(new Uri($"http://localhost:{40000 + new Random().Next(9999)}/"));
            Assert.That(loopback.ConnectionLimit, Is.EqualTo(int.MaxValue));

            var remote = ServicePointManager.FindServicePoint(UniqueHost());
            Assert.That(remote.ConnectionLimit, Is.EqualTo(ServicePointManager.DefaultConnectionLimit));
        }

        [Test]
        public void EnsureForHost_NullOrRelativeUri_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => ConnectionLimits.EnsureForHost(null));
            Assert.DoesNotThrow(() => ConnectionLimits.EnsureForHost(new Uri("/relative", UriKind.Relative)));
        }
    }
}
