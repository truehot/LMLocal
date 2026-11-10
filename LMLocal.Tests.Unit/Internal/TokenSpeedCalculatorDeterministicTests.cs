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

            calculator.Update(1);

            fake.Advance(TimeSpan.FromSeconds(1));
            calculator.Update(11);

            var speed = calculator.GetTokensPerSecond();
            Assert.That(speed, Is.EqualTo(10.0).Within(0.0001), "Expected 10 deltas/sec after 1 second and +10 deltas");
        }

        [Test]
        public void WaitBeforeFirstDelta_IsNotCounted()
        {
            var start = DateTime.UtcNow.Ticks;
            var fake = new FakeTimeProvider(start);
            var calculator = new TokenSpeedCalculator(windowSeconds: 5, timeProvider: fake);

            // Request sending and prompt processing: no deltas yet.
            fake.Advance(TimeSpan.FromSeconds(3));
            Assert.That(calculator.GetTokensPerSecond(), Is.EqualTo(0.0));

            calculator.Update(1);
            fake.Advance(TimeSpan.FromSeconds(1));
            calculator.Update(11);

            // Previously the window started at construction: 11 / 4 s = 2.75.
            Assert.That(calculator.GetTokensPerSecond(), Is.EqualTo(10.0).Within(0.0001));
            Assert.That(calculator.GetAverageTokensPerSecond(), Is.EqualTo(10.0).Within(0.0001));
        }

        [Test]
        public void SingleDelta_ReportsZero_AndDoesNotAffectAverage()
        {
            var start = DateTime.UtcNow.Ticks;
            var fake = new FakeTimeProvider(start);
            var calculator = new TokenSpeedCalculator(windowSeconds: 5, timeProvider: fake);

            fake.Advance(TimeSpan.FromSeconds(2));
            calculator.Update(1);
            fake.Advance(TimeSpan.FromSeconds(1));

            Assert.That(calculator.GetTokensPerSecond(), Is.EqualTo(0.0));
            Assert.That(calculator.GetAverageTokensPerSecond(), Is.EqualTo(0.0));
        }

        [Test]
        public void SlidingWindow_EvictsOldEntries()
        {
            var start = DateTime.UtcNow.Ticks;
            var fake = new FakeTimeProvider(start);
            var calculator = new TokenSpeedCalculator(windowSeconds: 2, timeProvider: fake);

            calculator.Update(1);
            fake.Advance(TimeSpan.FromSeconds(1));
            calculator.Update(5);

            fake.Advance(TimeSpan.FromSeconds(1));
            calculator.Update(11);

            var speed = calculator.GetTokensPerSecond();
            Assert.That(speed, Is.EqualTo(5.0).Within(0.0001));

            fake.Advance(TimeSpan.FromSeconds(3));
            var speed2 = calculator.GetTokensPerSecond();
            Assert.That(speed2, Is.EqualTo(0.0).Within(0.0001));
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

            calculator.Update(1);

            fake.Advance(TimeSpan.FromSeconds(1));
            calculator.Update(11);
            double s1 = calculator.GetTokensPerSecond();
            Assert.That(s1, Is.EqualTo(10.0).Within(0.0001));

            fake.Advance(TimeSpan.FromSeconds(1));
            calculator.Update(31);
            double s2 = calculator.GetTokensPerSecond();
            Assert.That(s2, Is.EqualTo(15.0).Within(0.0001));

            // A zero-speed sample (window fully elapsed without new deltas) must not pollute the average.
            fake.Advance(TimeSpan.FromSeconds(6));
            double zero = calculator.GetTokensPerSecond();
            Assert.That(zero, Is.EqualTo(0.0).Within(0.0001));

            double avg = calculator.GetAverageTokensPerSecond();
            Assert.That(avg, Is.EqualTo((s1 + s2) / 2).Within(0.0001));
        }
    }
}
