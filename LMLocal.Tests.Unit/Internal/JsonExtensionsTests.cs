using NUnit.Framework;
using LMLocal.Core.Common;

namespace LMLocal.Tests.Unit
{
    [TestFixture]
    public class JsonExtensionsTests
    {
        private enum TestEnum { A, B }
        private class TestObj
        {
            public int X { get; set; }
            public TestEnum E { get; set; }
            public string S { get; set; }
        }

        [Test]
        public void ToJson_FromJson_Roundtrip_Works()
        {
            var obj = new TestObj { X = 5, E = TestEnum.B, S = "hello" };
            var json = obj.ToJson();

            var parsed = json.FromJson<TestObj>();

            Assert.That(parsed, Is.Not.Null);
            Assert.That(parsed.X, Is.EqualTo(obj.X));
            Assert.That(parsed.E, Is.EqualTo(obj.E));
            Assert.That(parsed.S, Is.EqualTo(obj.S));
        }

        [Test]
        public void ToJsonIndentedWithEnumValues_SerializesEnumAsString()
        {
            var obj = new TestObj { X = 1, E = TestEnum.B, S = "x" };
            var json = obj.ToJsonIndentedWithEnumValues();

            // Enum should be serialized as its string representation ("B")
            Assert.That(json.Contains("\"B\""), Is.True);
        }
    }
}
