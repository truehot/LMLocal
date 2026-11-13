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
        private static TokenSpeedCalculator CreateCalculator(FakeTimeProvider fake, int windowSeconds = 5)
        {
            return new TokenSpeedCalculator(windowSeconds: windowSeconds, timeProvider: fake);
        }

        /// <summary>
        /// Feeds one chunk exactly the way StreamProcessor does: a single estimate, handed to the calculator together with
        /// the character count of the same chunk.
        /// </summary>
        private static void AddChunk(TokenSpeedCalculator calculator, string text)
        {
            var estimator = new LMLocal.Core.Common.StreamedTokenEstimator();
            calculator.Add(text.Length, estimator.Add(text));
        }

        [Test]
        public void PerTokenDeltas_ReportDeltasPerSecond()
        {
            var fake = new FakeTimeProvider(DateTime.UtcNow.Ticks);
            var calculator = CreateCalculator(fake);

            AddChunk(calculator, " wants");                    // first delta opens the window and is its baseline
            for (int i = 0; i < 9; i++)
            {
                fake.Advance(TimeSpan.FromSeconds(0.1));
                AddChunk(calculator, " wants");
            }

            // 9 deltas after the baseline over 0.9 s.
            Assert.That(calculator.GetTokensPerSecond(), Is.EqualTo(10.0).Within(0.0001));

            // One reading taken, so the average equals it.
            Assert.That(calculator.GetAverageTokensPerSecond(), Is.EqualTo(10.0).Within(0.0001));
        }

        [Test]
        public void CyrillicPerTokenDeltas_AreNotInflated()
        {
            var fake = new FakeTimeProvider(DateTime.UtcNow.Ticks);
            var calculator = CreateCalculator(fake);

            AddChunk(calculator, " привет");
            for (int i = 0; i < 9; i++)
            {
                fake.Advance(TimeSpan.FromSeconds(0.1));
                AddChunk(calculator, " привет");
            }

            // Short per-token deltas keep the delta mode: the 2 chars/token estimate would report ~35 instead of 10.
            Assert.That(calculator.GetTokensPerSecond(), Is.EqualTo(10.0).Within(0.0001));
        }

        [Test]
        public void ChunkedProvider_ReportsEstimatedTokensPerSecond()
        {
            var fake = new FakeTimeProvider(DateTime.UtcNow.Ticks);
            var calculator = CreateCalculator(fake);

            // Raw GMI rows (plan §1.1): arrival offset and character count of each chunk.
            double[] offsetsSeconds = { 0.0006, 0.1426, 0.3398, 0.9320, 1.1125, 1.2189, 1.4584, 2.4712 };
            int[] chunkChars = { 3, 74, 139, 303, 73, 96, 35, 500 };

            double previous = 0.0;
            for (int i = 0; i < offsetsSeconds.Length; i++)
            {
                fake.Advance(TimeSpan.FromSeconds(offsetsSeconds[i] - previous));
                previous = offsetsSeconds[i];
                AddChunk(calculator, new string('x', chunkChars[i]));
            }

            // 1223 ASCII characters ≈ 306 tokens over 2.47 s — hundreds, not the 3 chunks/second the old code reported.
            double live = calculator.GetTokensPerSecond();
            Assert.That(live, Is.GreaterThan(100.0).And.LessThan(160.0));
            Assert.That(calculator.GetAverageTokensPerSecond(), Is.EqualTo(live).Within(0.0001));
        }

        [Test]
        public void ChunkAfterLongPause_CountsFromPreviousChunk()
        {
            var fake = new FakeTimeProvider(DateTime.UtcNow.Ticks);
            var calculator = CreateCalculator(fake);

            // create_file round (plan §1.1): 4 reasoning chunks in 1.5 s ...
            AddChunk(calculator, new string('r', 100));
            for (int i = 0; i < 3; i++)
            {
                fake.Advance(TimeSpan.FromSeconds(0.5));
                AddChunk(calculator, new string('r', 100));
            }

            // ... then 25.8 s of silence, during which the batch loop keeps sampling and shrinks the window ...
            for (int i = 0; i < 258; i++)
            {
                fake.Advance(TimeSpan.FromSeconds(0.1));
                calculator.GetTokensPerSecond();
            }

            // ... then every tool argument arrives as one chunk.
            AddChunk(calculator, new string('a', 15760));
            fake.Advance(TimeSpan.FromSeconds(0.1));

            double live = calculator.GetTokensPerSecond();

            // Baseline is the previous chunk (t = 1.5 s), not the chunk itself: 3940 estimated tokens over 25.9 s.
            Assert.That(live, Is.GreaterThan(140.0).And.LessThan(160.0));
        }

        [Test]
        public void ChunkAfterPause_WhenWindowHasManyPoints()
        {
            var fake = new FakeTimeProvider(DateTime.UtcNow.Ticks);
            var calculator = CreateCalculator(fake, windowSeconds: 2);

            AddChunk(calculator, " abc");
            for (int i = 0; i < 4; i++)
            {
                fake.Advance(TimeSpan.FromSeconds(0.4));
                AddChunk(calculator, " abc");
            }

            fake.Advance(TimeSpan.FromSeconds(6.0));
            AddChunk(calculator, " abc");
            fake.Advance(TimeSpan.FromSeconds(0.1));

            // Baseline = the newest point not newer than the cutoff (5.7 s) — the chunk at 1.6 s, not the new one.
            Assert.That(calculator.GetTokensPerSecond(), Is.EqualTo(1.0 / 6.1).Within(0.0001));
        }

        [Test]
        public void PauseShorterThanWindow_BehavesAsBefore()
        {
            var fake = new FakeTimeProvider(DateTime.UtcNow.Ticks);
            var calculator = CreateCalculator(fake);

            AddChunk(calculator, " abc");
            fake.Advance(TimeSpan.FromSeconds(1.0));
            AddChunk(calculator, " abc");

            Assert.That(calculator.GetTokensPerSecond(), Is.EqualTo(1.0).Within(0.0001));
        }

        [Test]
        public void SingleDelta_ReportsZero()
        {
            var fake = new FakeTimeProvider(DateTime.UtcNow.Ticks);
            var calculator = CreateCalculator(fake);

            fake.Advance(TimeSpan.FromSeconds(2));
            AddChunk(calculator, "hello");
            fake.Advance(TimeSpan.FromSeconds(1));

            Assert.That(calculator.GetTokensPerSecond(), Is.EqualTo(0.0));
            Assert.That(calculator.GetAverageTokensPerSecond(), Is.EqualTo(0.0));
        }

        [Test]
        public void WaitBeforeFirstDelta_IsNotCounted()
        {
            var fake = new FakeTimeProvider(DateTime.UtcNow.Ticks);
            var calculator = CreateCalculator(fake);

            // Request sending and prompt processing: no deltas yet.
            fake.Advance(TimeSpan.FromSeconds(3));
            Assert.That(calculator.GetTokensPerSecond(), Is.EqualTo(0.0));

            AddChunk(calculator, " abc");
            fake.Advance(TimeSpan.FromSeconds(1));
            AddChunk(calculator, " abc");

            // Previously the window started at construction: 2 / 4 s = 0.5.
            Assert.That(calculator.GetTokensPerSecond(), Is.EqualTo(1.0).Within(0.0001));
        }

        [Test]
        public void SlidingWindow_EvictsOldEntries()
        {
            var fake = new FakeTimeProvider(DateTime.UtcNow.Ticks);
            var calculator = CreateCalculator(fake, windowSeconds: 2);

            AddChunk(calculator, " abc");
            fake.Advance(TimeSpan.FromSeconds(1));
            AddChunk(calculator, " abc");
            fake.Advance(TimeSpan.FromSeconds(1));
            AddChunk(calculator, " abc");

            // 2 deltas after the baseline over 2 s.
            Assert.That(calculator.GetTokensPerSecond(), Is.EqualTo(1.0).Within(0.0001));

            fake.Advance(TimeSpan.FromSeconds(3));

            // No deltas inside the window: the reading drops to zero instead of staying at the last value.
            Assert.That(calculator.GetTokensPerSecond(), Is.EqualTo(0.0).Within(0.0001));
        }

        [Test]
        public void Mode_IsLatchedOncePerRound()
        {
            var fake = new FakeTimeProvider(DateTime.UtcNow.Ticks);
            var calculator = CreateCalculator(fake);

            // Short per-token deltas decide the delta mode after five deltas ...
            for (int i = 0; i < 5; i++)
            {
                AddChunk(calculator, " abc");
                fake.Advance(TimeSpan.FromSeconds(0.1));
            }

            // ... and a single huge chunk afterwards must not flip it mid-round.
            AddChunk(calculator, new string('a', 1000));

            // 5 deltas after the baseline over 0.5 s. The estimate mode would report ~508 (254 estimated tokens).
            Assert.That(calculator.GetTokensPerSecond(), Is.EqualTo(5.0 / 0.5).Within(0.0001));
        }

        [Test]
        public void Average_ReturnsZero_WhenNoPositiveReadings()
        {
            var fake = new FakeTimeProvider(DateTime.UtcNow.Ticks);
            var calculator = CreateCalculator(fake);

            AddChunk(calculator, " abc");
            fake.Advance(TimeSpan.FromSeconds(1));
            calculator.GetTokensPerSecond();                    // a single delta gives a 0 reading

            Assert.That(calculator.GetAverageTokensPerSecond(), Is.EqualTo(0.0));
        }

        [Test]
        public void Average_AveragesPositiveReadings_IgnoresZero()
        {
            var fake = new FakeTimeProvider(DateTime.UtcNow.Ticks);
            var calculator = CreateCalculator(fake);

            AddChunk(calculator, " abc");
            fake.Advance(TimeSpan.FromSeconds(1));
            AddChunk(calculator, " abc");
            Assert.That(calculator.GetTokensPerSecond(), Is.EqualTo(1.0).Within(0.0001));   // 1 delta / 1 s

            fake.Advance(TimeSpan.FromSeconds(1));
            AddChunk(calculator, " abc");
            AddChunk(calculator, " abc");
            Assert.That(calculator.GetTokensPerSecond(), Is.EqualTo(1.5).Within(0.0001));   // 3 deltas / 2 s

            fake.Advance(TimeSpan.FromSeconds(10));
            Assert.That(calculator.GetTokensPerSecond(), Is.EqualTo(0.0));                  // nothing in the window

            Assert.That(calculator.GetAverageTokensPerSecond(), Is.EqualTo(1.25).Within(0.0001));
        }

        [Test]
        public void EmptyChunk_IsIgnored()
        {
            var fake = new FakeTimeProvider(DateTime.UtcNow.Ticks);
            var calculator = CreateCalculator(fake);

            calculator.Add(0, 0.0);
            calculator.Add(-5, 1.0);

            Assert.That(calculator.GetTokensPerSecond(), Is.EqualTo(0.0));
            Assert.That(calculator.GetAverageTokensPerSecond(), Is.EqualTo(0.0));
        }
    }
}
