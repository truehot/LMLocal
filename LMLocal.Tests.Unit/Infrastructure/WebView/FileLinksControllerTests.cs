using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.WebView.Controllers;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.WebView
{
    [TestFixture]
    public class FileLinksControllerTests
    {
        [Test]
        public async Task OpenAsync_EmptyJson_ReturnsNotFound()
        {
            var (controller, resolver) = CreateController();

            string json = await controller.OpenAsync(null);
            Assert.That(json, Is.EqualTo("{\"status\":\"notFound\"}"));

            json = await controller.OpenAsync("   ");
            Assert.That(json, Is.EqualTo("{\"status\":\"notFound\"}"));

            json = await controller.OpenAsync("{\"link\":\"\"}");
            Assert.That(json, Is.EqualTo("{\"status\":\"notFound\"}"));

            resolver.Verify(r => r.OpenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task OpenAsync_Opened_UsesCamelCaseAndOmitsNulls()
        {
            var (controller, resolver) = CreateController(new FileLinkOpenResult
            {
                Status = FileLinkStatus.Opened,
                Path = @"LMLocal\Resources\js\a.js",
                Via = "suffix"
            });

            string json = await controller.OpenAsync("{\"link\":\"a.js\"}");

            Assert.That(json, Is.EqualTo("{\"status\":\"opened\",\"path\":\"LMLocal\\\\Resources\\\\js\\\\a.js\",\"via\":\"suffix\"}"));
            Assert.That(json, Does.Not.Contain("candidates"));
            resolver.Verify(r => r.OpenAsync("a.js", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task OpenAsync_Ambiguous_ReturnsCandidates()
        {
            var (controller, _) = CreateController(new FileLinkOpenResult
            {
                Status = FileLinkStatus.Ambiguous,
                Candidates = new List<string> { @"A\Util.cs", @"B\Util.cs" }
            });

            string json = await controller.OpenAsync("{\"link\":\"Util.cs\"}");

            Assert.That(json, Is.EqualTo("{\"status\":\"ambiguous\",\"candidates\":[\"A\\\\Util.cs\",\"B\\\\Util.cs\"]}"));
        }

        [Test]
        public async Task OpenAsync_NotFound_HasStatusOnly()
        {
            var (controller, _) = CreateController(new FileLinkOpenResult { Status = FileLinkStatus.NotFound });

            string json = await controller.OpenAsync("{\"link\":\"a.js\"}");

            Assert.That(json, Is.EqualTo("{\"status\":\"notFound\"}"));
        }

        [Test]
        public async Task OpenAsync_ResolverThrows_ReturnsError()
        {
            var resolver = new Mock<IFileLinkResolver>();
            resolver
                .Setup(r => r.OpenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new System.InvalidOperationException("boom"));

            var controller = new FileLinksController(resolver.Object);

            string json = await controller.OpenAsync("{\"link\":\"a.js\"}");

            Assert.That(json, Is.EqualTo("{\"status\":\"error\"}"));
        }

        private static (FileLinksController Controller, Mock<IFileLinkResolver> Resolver) CreateController(FileLinkOpenResult result = null)
        {
            var resolver = new Mock<IFileLinkResolver>();
            resolver
                .Setup(r => r.OpenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(result ?? new FileLinkOpenResult { Status = FileLinkStatus.NotFound });

            return (new FileLinksController(resolver.Object), resolver);
        }
    }
}
