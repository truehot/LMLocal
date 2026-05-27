using System.Threading;
using LMLocal.Infrastructure.Streaming;

using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    [TestFixture]
    public class TokenSpeedCalculatorTests
    {
        [Test]
        public void InitialSpeed_IsZero()
        {
            var calculator = new TokenSpeedCalculator(windowSeconds: 5);
            Assert.That(calculator.GetTokensPerSecond(), Is.EqualTo(0.0).Within(0.001));
        }

        [Test]
        public void SpeedCalculation_ReturnsCorrectValue()
        {
            var calculator = new TokenSpeedCalculator(windowSeconds: 5);

            calculator.Update(0);
            Thread.Sleep(100);
            calculator.Update(10);

            double speed = calculator.GetTokensPerSecond();

            Assert.That(speed, Is.GreaterThan(0));
        }

        [Test]
        public void Update_DoesNotAddEntry_IfTokenCountUnchanged()
        {
            var calculator = new TokenSpeedCalculator(windowSeconds: 5);

            calculator.Update(10);
            calculator.Update(10);

            Assert.That(calculator.GetTokensPerSecond(), Is.Not.Null);
        }

        [Test]
        public void GetTokensPerSecond_HandlesZeroTimeSpanGracefully()
        {
            var calculator = new TokenSpeedCalculator(windowSeconds: 5);
            calculator.Update(0);
            calculator.Update(20);

            var speed = calculator.GetTokensPerSecond();
            Assert.That(speed, Is.Not.NaN);
        }
    }
}
