using System;
using System.Net.Http;
using LMLocal.Infrastructure.HttpWrapper;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure
{
    [TestFixture]
    public class HttpClientWrapperTests
    {
        [Test]
        public void SendAsync_AfterDispose_ThrowsObjectDisposedException()
        {
            var wrapper = new HttpClientWrapper();
            wrapper.Dispose();

            Assert.ThrowsAsync<ObjectDisposedException>(async () => await wrapper.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://localhost/")));
        }
    }
}
