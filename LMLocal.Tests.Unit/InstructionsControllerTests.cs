using System.Threading;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Instructions;
using LMLocal.Infrastructure.WebView.Controllers;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit
{
    [TestFixture]
    public class InstructionsControllerTests
    {
        [Test]
        public async Task GetInstructionsAsync_ReturnsJson()
        {
            var mock = new Mock<IInstructionsManager>();
            mock.Setup(m => m.GetAsync(It.IsAny<CancellationToken>())).Returns(Task.FromResult("{\"key\":\"value\"}"));
            var controller = new InstructionsController(mock.Object);

            var result = await controller.GetInstructionsAsync();

            Assert.That(result, Is.EqualTo("{\"key\":\"value\"}"));
        }

        [Test]
        public async Task GetInstructionsAsync_WhenThrows_ReturnsEmptyJson()
        {
            var mock = new Mock<IInstructionsManager>();
            mock.Setup(m => m.GetAsync(It.IsAny<CancellationToken>())).Throws(new System.Exception("fail"));
            var controller = new InstructionsController(mock.Object);

            var result = await controller.GetInstructionsAsync();

            Assert.That(result, Is.EqualTo("{}"));
        }

        [Test]
        public async Task UpdateInstructionsAsync_ValidJson_ReturnsTrue()
        {
            var mock = new Mock<IInstructionsManager>();
            mock.Setup(m => m.UpdateAsync("{\"key\":\"value\"}", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            var controller = new InstructionsController(mock.Object);

            var result = await controller.UpdateInstructionsAsync("{\"key\":\"value\"}");

            Assert.That(result, Is.True);
            mock.Verify(m => m.UpdateAsync("{\"key\":\"value\"}", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task UpdateInstructionsAsync_NullOrEmpty_ReturnsFalse()
        {
            var mock = new Mock<IInstructionsManager>();
            var controller = new InstructionsController(mock.Object);

            var resultNull = await controller.UpdateInstructionsAsync(null);
            var resultEmpty = await controller.UpdateInstructionsAsync("");

            Assert.That(resultNull, Is.False);
            Assert.That(resultEmpty, Is.False);
            mock.Verify(m => m.UpdateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task UpdateInstructionsAsync_WhenThrows_ReturnsFalse()
        {
            var mock = new Mock<IInstructionsManager>();
            mock.Setup(m => m.UpdateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Throws(new System.Exception("fail"));
            var controller = new InstructionsController(mock.Object);

            var result = await controller.UpdateInstructionsAsync("{\"x\":1}");

            Assert.That(result, Is.False);
        }

        [Test]
        public async Task UpdateInstructionsSelectedTabAsync_ValidTab_ReturnsTrue()
        {
            var mock = new Mock<IInstructionsManager>();
            mock.Setup(m => m.UpdateSelectedTabAsync("tab1", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            var controller = new InstructionsController(mock.Object);

            var result = await controller.UpdateInstructionsSelectedTabAsync("tab1");

            Assert.That(result, Is.True);
            mock.Verify(m => m.UpdateSelectedTabAsync("tab1", It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task UpdateInstructionsSelectedTabAsync_NullOrEmpty_ReturnsFalse()
        {
            var mock = new Mock<IInstructionsManager>();
            var controller = new InstructionsController(mock.Object);

            var resultNull = await controller.UpdateInstructionsSelectedTabAsync(null);
            var resultEmpty = await controller.UpdateInstructionsSelectedTabAsync("");

            Assert.That(resultNull, Is.False);
            Assert.That(resultEmpty, Is.False);
            mock.Verify(m => m.UpdateSelectedTabAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        }
    }
}
