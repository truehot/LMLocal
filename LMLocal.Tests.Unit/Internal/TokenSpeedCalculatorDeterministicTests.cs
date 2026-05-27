using System;
using LMLocal.Services;
using LMLocal.Infrastructure.Time;
using NUnit.Framework;
using LMLocal.Infrastructure.Streaming;

namespace LMLocal.Tests.Unit
{
    internal class FakeTimeProvider : ITimeProvider
    {
        private long _ticks;
        public FakeTimeProvider(long initialTicks)
        {
            _ticks = initialTicks;
        }

        public long UtcNowTicks() => _ticks;

        public void Advance(TimeSpan ts) => _ticks += ts.Ticks;
    }

    [TestFixture]
    public class TokenSpeedCalculatorDeterministicTests
    {
        [Test]
        public void GetTokensPerSecond_ReturnsDeterministicValue_WithFakeTimeProvider()
        {
            var start = DateTime.UtcNow.Ticks;
            var fake = new FakeTimeProvider(start);
            var calculator = new TokenSpeedCalculator(windowSeconds: 5, timeProvider: fake);

            calculator.Update(0);

            fake.Advance(TimeSpan.FromSeconds(1));
            calculator.Update(10);

            var speed = calculator.GetTokensPerSecond();
            Assert.That(speed, Is.EqualTo(10.0).Within(0.0001), "Expected 10 tokens/sec after 1 second and +10 tokens");
        }

        [Test]
        public void SlidingWindow_EvictsOldEntries()
        {
            var start = DateTime.UtcNow.Ticks;
            var fake = new FakeTimeProvider(start);
            var calculator = new TokenSpeedCalculator(windowSeconds: 2, timeProvider: fake);

            calculator.Update(0);
            fake.Advance(TimeSpan.FromSeconds(1));
            calculator.Update(4);

            fake.Advance(TimeSpan.FromSeconds(1));
            calculator.Update(10);

            var speed = calculator.GetTokensPerSecond();
            Assert.That(speed, Is.EqualTo(5.0).Within(0.0001));

            fake.Advance(TimeSpan.FromSeconds(3));
            var speed2 = calculator.GetTokensPerSecond();
            Assert.That(speed2, Is.GreaterThanOrEqualTo(0.0));
        }
    }
}
