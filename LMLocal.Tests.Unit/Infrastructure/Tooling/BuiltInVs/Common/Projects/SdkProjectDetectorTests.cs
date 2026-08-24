using System.Text;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Projects;
using LMLocal.Tests.Unit.Infrastructure;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Common.Projects
{
    [TestFixture]
    public class SdkProjectDetectorTests
    {
        [Test]
        public async Task IsSdkStyleAsync_FirstLineSdk_ReturnsTrue()
        {
            var fs = new InMemoryFileSystem();
            await fs.WriteAllBytesAsync(@"C:\proj\App.csproj", Encoding.UTF8.GetBytes(
                "<Project Sdk=\"Microsoft.NET.Sdk\">\r\n</Project>"));

            var result = await SdkProjectDetector.IsSdkStyleAsync(fs, @"C:\proj\App.csproj");

            Assert.That(result, Is.True);
        }

        [Test]
        public async Task IsSdkStyleAsync_XmlDeclarationAndMultilineProject_ReturnsTrue()
        {
            var fs = new InMemoryFileSystem();
            await fs.WriteAllBytesAsync(@"C:\proj\App.csproj", Encoding.UTF8.GetBytes(
                "<?xml version=\"1.0\"?>\r\n<Project\r\n  Sdk=\"Microsoft.NET.Sdk.Web\">\r\n</Project>"));

            var result = await SdkProjectDetector.IsSdkStyleAsync(fs, @"C:\proj\App.csproj");

            Assert.That(result, Is.True);
        }

        [Test]
        public async Task IsSdkStyleAsync_LegacyProject_ReturnsFalse()
        {
            var fs = new InMemoryFileSystem();
            await fs.WriteAllBytesAsync(@"C:\proj\App.csproj", Encoding.UTF8.GetBytes(
                "<Project ToolsVersion=\"15.0\" xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">\r\n</Project>"));

            var result = await SdkProjectDetector.IsSdkStyleAsync(fs, @"C:\proj\App.csproj");

            Assert.That(result, Is.False);
        }

        [Test]
        public async Task IsSdkStyleAsync_CommentedOutSdk_ReturnsFalse()
        {
            var fs = new InMemoryFileSystem();
            await fs.WriteAllBytesAsync(@"C:\proj\App.csproj", Encoding.UTF8.GetBytes(
                "<!-- <Project Sdk=\"Microsoft.NET.Sdk\"> -->\r\n<Project ToolsVersion=\"15.0\">\r\n</Project>"));

            var result = await SdkProjectDetector.IsSdkStyleAsync(fs, @"C:\proj\App.csproj");

            Assert.That(result, Is.False);
        }

        [Test]
        public async Task IsSdkStyleAsync_MissingFile_ReturnsFalse()
        {
            var fs = new InMemoryFileSystem();

            var result = await SdkProjectDetector.IsSdkStyleAsync(fs, @"C:\proj\Missing.csproj");

            Assert.That(result, Is.False);
        }
    }
}
