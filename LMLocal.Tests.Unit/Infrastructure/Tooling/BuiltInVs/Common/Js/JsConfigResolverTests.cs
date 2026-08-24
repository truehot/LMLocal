using System;
using System.IO;
using System.Text;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Js;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Common.Js
{
    [TestFixture]
    public class JsConfigResolverTests
    {
        private string _root;
        private JsConfigResolver _resolver;
        private InMemoryFileSystem _fs;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "JsConfigResolverTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_root);
            _fs = new InMemoryFileSystem();
            _resolver = new JsConfigResolver(_fs);
        }

        [TearDown]
        public void TearDown()
        {
            try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
        }

        private void WriteConfig(string fileName, string content)
        {
            _fs.WriteAllBytesAsync(Path.Combine(_root, fileName), Encoding.UTF8.GetBytes(content)).GetAwaiter().GetResult();
        }

        [Test]
        public void Load_ReturnsNullWhenNoConfig()
        {
            var config = _resolver.Load(_root);

            Assert.That(config, Is.Null);
        }

        [Test]
        public void Load_ParsesBaseUrl()
        {
            WriteConfig("jsconfig.json", "{ \"compilerOptions\": { \"baseUrl\": \".\" } }");

            var config = _resolver.Load(_root);

            Assert.That(config, Is.Not.Null);
            Assert.That(config.BaseUrl, Is.EqualTo(Path.GetFullPath(_root)));
        }

        [Test]
        public void Load_ParsesPaths()
        {
            WriteConfig("jsconfig.json", "{ \"compilerOptions\": { \"baseUrl\": \".\", \"paths\": { \"@app/*\": [\"Resources/js/*\"] } } }");

            var config = _resolver.Load(_root);

            Assert.That(config, Is.Not.Null);
            Assert.That(config.PathMappings, Is.Not.Empty);
            Assert.That(config.PathMappings.ContainsKey("@app/*"), Is.True);
            Assert.That(config.PathMappings["@app/*"], Is.EqualTo("Resources/js/*"));
        }

        [Test]
        public void Load_PrefersJsConfigOverTsConfig()
        {
            WriteConfig("jsconfig.json", "{ \"compilerOptions\": { \"baseUrl\": \".\" } }");
            WriteConfig("tsconfig.json", "{ \"compilerOptions\": { \"baseUrl\": \"./ts\" } }");

            var config = _resolver.Load(_root);

            Assert.That(config, Is.Not.Null);
            Assert.That(config.ConfigFilePath, Does.EndWith("jsconfig.json"));
        }

        [Test]
        public void Load_InvalidJson_ReturnsNull()
        {
            WriteConfig("jsconfig.json", "{ not valid json ");

            var config = _resolver.Load(_root);

            Assert.That(config, Is.Null);
        }

        [Test]
        public void Resolve_AliasWithWildcard()
        {
            WriteConfig("jsconfig.json", "{ \"compilerOptions\": { \"baseUrl\": \".\", \"paths\": { \"@app/*\": [\"Resources/js/*\"] } } }");
            var config = _resolver.Load(_root);

            string fromFile = Path.Combine(_root, "Resources", "js", "app.js");
            string target = Path.Combine(_root, "Resources", "js", "chat", "controller.js");
            _fs.WriteAllBytesAsync(target, new byte[0]).GetAwaiter().GetResult();

            string resolved = _resolver.ResolveModule("@app/chat/controller", fromFile, config);

            Assert.That(resolved, Is.EqualTo(target));
        }

        [Test]
        public void Resolve_RelativeImport()
        {
            var config = new JsConfig { BaseUrl = _root };
            string fromFile = Path.Combine(_root, "src", "app.js");
            string target = Path.Combine(_root, "src", "utils", "helper.js");
            _fs.WriteAllBytesAsync(target, new byte[0]).GetAwaiter().GetResult();

            string resolved = _resolver.ResolveModule("./utils/helper", fromFile, config);

            Assert.That(resolved, Is.EqualTo(target));
        }

        [Test]
        public void Resolve_BareSpecifierIsExternal()
        {
            var config = new JsConfig { BaseUrl = _root };
            string fromFile = Path.Combine(_root, "src", "app.js");

            string resolved = _resolver.ResolveModule("react", fromFile, config);

            Assert.That(resolved, Is.Null);
        }

        [Test]
        public void Resolve_IndexFileFallback()
        {
            var config = new JsConfig { BaseUrl = _root };
            string fromFile = Path.Combine(_root, "src", "app.js");
            string target = Path.Combine(_root, "src", "utils", "index.js");
            _fs.WriteAllBytesAsync(target, new byte[0]).GetAwaiter().GetResult();

            string resolved = _resolver.ResolveModule("./utils", fromFile, config);

            Assert.That(resolved, Is.EqualTo(target));
        }

        [Test]
        public void Load_FindsConfigInProjectRootBelowSolutionDir()
        {
            // Real-world layout: the config is NOT in solutionDir, but in a project root below it.
            string projectRoot = Path.Combine(_root, "LMLocal");
            Directory.CreateDirectory(projectRoot);
            _fs.WriteAllBytesAsync(
                Path.Combine(projectRoot, "jsconfig.json"),
                Encoding.UTF8.GetBytes("{ \"compilerOptions\": { \"baseUrl\": \".\", \"paths\": { \"@app/*\": [\"Resources/js/*\"] } } }"))
                .GetAwaiter().GetResult();

            var config = _resolver.Load(_root, new[] { projectRoot });

            Assert.That(config, Is.Not.Null);
            Assert.That(config.ConfigFilePath, Does.EndWith("jsconfig.json"));
            Assert.That(config.PathMappings.ContainsKey("@app/*"), Is.True);
        }

        [Test]
        public void Load_WalksUpFromJsFileDirToConfig()
        {
            // Config is a few levels above the JS file directory; Load must walk up to it.
            string projectRoot = Path.Combine(_root, "LMLocal");
            string jsDir = Path.Combine(projectRoot, "Resources", "js");
            Directory.CreateDirectory(jsDir);
            _fs.WriteAllBytesAsync(
                Path.Combine(projectRoot, "jsconfig.json"),
                Encoding.UTF8.GetBytes("{ \"compilerOptions\": { \"baseUrl\": \".\" } }"))
                .GetAwaiter().GetResult();

            var config = _resolver.Load(_root, new[] { jsDir });

            Assert.That(config, Is.Not.Null);
            Assert.That(config.ConfigFilePath, Does.EndWith("jsconfig.json"));
        }

        [Test]
        public void Load_BaseUrlDefaultsToConfigDir()
        {
            // No baseUrl in the config — the default should be the config file directory, not solutionDir.
            string projectRoot = Path.Combine(_root, "LMLocal");
            Directory.CreateDirectory(projectRoot);
            _fs.WriteAllBytesAsync(
                Path.Combine(projectRoot, "jsconfig.json"),
                Encoding.UTF8.GetBytes("{ \"compilerOptions\": { \"paths\": { \"@app/*\": [\"Resources/js/*\"] } } }"))
                .GetAwaiter().GetResult();

            var config = _resolver.Load(_root, new[] { projectRoot });

            Assert.That(config.BaseUrl, Is.EqualTo(Path.GetFullPath(projectRoot)));
        }

        [Test]
        public void Resolve_AliasWithProjectRootConfig()
        {
            // Integration: config in the project root, alias @app/* resolved against Resources/js.
            string projectRoot = Path.Combine(_root, "LMLocal");
            string jsDir = Path.Combine(projectRoot, "Resources", "js");
            string target = Path.Combine(jsDir, "chat", "controller.js");
            _fs.WriteAllBytesAsync(target, new byte[0]).GetAwaiter().GetResult();
            _fs.WriteAllBytesAsync(
                Path.Combine(projectRoot, "jsconfig.json"),
                Encoding.UTF8.GetBytes("{ \"compilerOptions\": { \"baseUrl\": \".\", \"paths\": { \"@app/*\": [\"Resources/js/*\"] } } }"))
                .GetAwaiter().GetResult();

            var config = _resolver.Load(_root, new[] { projectRoot });
            string resolved = _resolver.ResolveModule("@app/chat/controller", Path.Combine(jsDir, "app.js"), config);

            Assert.That(resolved, Is.EqualTo(target));
        }
    }
}
