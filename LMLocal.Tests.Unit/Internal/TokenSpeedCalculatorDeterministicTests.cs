using System;
using LMLocal.Infrastructure.Streaming;
using LMLocal.Infrastructure.Time;
using NUnit.Framework;

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

        [Test]
        public void GetAverageTokensPerSecond_ReturnsZero_WhenNoPositiveSamples()
        {
            var start = DateTime.UtcNow.Ticks;
            var fake = new FakeTimeProvider(start);
            var calculator = new TokenSpeedCalculator(windowSeconds: 5, timeProvider: fake);

            Assert.That(calculator.GetAverageTokensPerSecond(), Is.EqualTo(0.0));
        }

        [Test]
        public void GetAverageTokensPerSecond_AveragesPositiveSamples_IgnoresZero()
        {
            var start = DateTime.UtcNow.Ticks;
            var fake = new FakeTimeProvider(start);
            var calculator = new TokenSpeedCalculator(windowSeconds: 5, timeProvider: fake);

            calculator.Update(0);

            fake.Advance(TimeSpan.FromSeconds(1));
            calculator.Update(10);
            double s1 = calculator.GetTokensPerSecond();
            Assert.That(s1, Is.EqualTo(10.0).Within(0.0001));

            fake.Advance(TimeSpan.FromSeconds(1));
            calculator.Update(30);
            double s2 = calculator.GetTokensPerSecond();
            Assert.That(s2, Is.EqualTo(15.0).Within(0.0001));

            // A zero-speed sample (window fully elapsed without new tokens) must not pollute the average.
            fake.Advance(TimeSpan.FromSeconds(6));
            double zero = calculator.GetTokensPerSecond();
            Assert.That(zero, Is.EqualTo(0.0).Within(0.0001));

            double avg = calculator.GetAverageTokensPerSecond();
            Assert.That(avg, Is.EqualTo((s1 + s2) / 2).Within(0.0001));
        }
    }
}
