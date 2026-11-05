using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Common
{
    /// <summary>
    /// ToolFileOpener opens only files a tool actually wrote, through the typed <see cref="IToolFileOpenResult"/> contract.
    /// </summary>
    [TestFixture]
    public class ToolFileOpenerTests
    {
        private const string SolutionDir = @"C:\sln";

        private Mock<ISettingsManager> _settings;
        private Mock<IVsDependencies> _vs;
        private Mock<IFileSystem> _fileSystem;
        private List<string> _opened;
        private ToolFileOpener _opener;

        [SetUp]
        public void SetUp()
        {
            _settings = new Mock<ISettingsManager>();
            _settings.Setup(s => s.Current).Returns(new AppSettings { OpenToolFilesInEditor = true });

            _vs = new Mock<IVsDependencies>();
            _vs.Setup(v => v.GetSolutionDirectory()).Returns(SolutionDir);

            _fileSystem = new Mock<IFileSystem>();
            _fileSystem.Setup(f => f.FileExists(It.IsAny<string>())).Returns(true);

            _opened = new List<string>();
            _opener = new ToolFileOpener(_settings.Object, _vs.Object, new PathResolver(), _fileSystem.Object,
                path => { _opened.Add(path); return Task.CompletedTask; });
        }

        private static string Abs(string relative) => Path.Combine(SolutionDir, relative);

        [Test]
        public async Task SuccessfulReplace_OpensTheFile()
        {
            await _opener.TryOpenEditedFileAsync("replace_file_lines", new ReplaceLinesResponse { Success = true, FilePath = @"src\A.cs", LinesReplaced = 1 });

            Assert.That(_opened, Is.EqualTo(new[] { Abs(@"src\A.cs") }));
        }

        [Test]
        public async Task SuccessfulInsertCreateAndReplaceContent_OpenTheFile()
        {
            await _opener.TryOpenEditedFileAsync("insert_file_lines", new InsertLinesResponse { Success = true, FilePath = "A.cs" });
            await _opener.TryOpenEditedFileAsync("create_file", new CreateFileResponse { Success = true, CreatedSuccessfully = true, FilePath = "B.cs" });
            await _opener.TryOpenEditedFileAsync("replace_file_content", new ApplyCodeEditResponse { Success = true, FilePath = "C.cs" });

            Assert.That(_opened, Is.EqualTo(new[] { Abs("A.cs"), Abs("B.cs"), Abs("C.cs") }));
        }

        [Test]
        public async Task FailedCall_DoesNotOpen()
        {
            await _opener.TryOpenEditedFileAsync("replace_file_lines", new ReplaceLinesResponse { Success = false, FilePath = "A.cs" });
            await _opener.TryOpenEditedFileAsync("create_file", new CreateFileResponse { Success = false, FilePath = "B.cs" });
            await _opener.TryOpenEditedFileAsync("replace_file_content", new ApplyCodeEditResponse { Success = false, FilePath = "C.cs" });

            Assert.That(_opened, Is.Empty);
        }

        [Test]
        public async Task MatchFailure_DoesNotOpenAlthoughFilePathIsSet()
        {
            // A match failure names the file it looked at; that file was not changed.
            await _opener.TryOpenEditedFileAsync("replace_file_lines", new ReplaceLinesResponse { Success = false, IsMatchFailure = true, FilePath = "A.cs" });
            await _opener.TryOpenEditedFileAsync("insert_file_lines", new InsertLinesResponse { Success = false, IsMatchFailure = true, FilePath = "A.cs" });

            Assert.That(_opened, Is.Empty);
        }

        [Test]
        public async Task NoOpEdits_DoNotOpen()
        {
            await _opener.TryOpenEditedFileAsync("replace_file_lines", new ReplaceLinesResponse { Success = true, FilePath = "A.cs", IsNoOp = true });
            await _opener.TryOpenEditedFileAsync("replace_file_content", new ApplyCodeEditResponse { Success = true, FilePath = "A.cs", IsNoOp = true });

            Assert.That(_opened, Is.Empty);
        }

        [Test]
        public async Task ResultsWithoutTheContract_DoNotOpen()
        {
            await _opener.TryOpenEditedFileAsync("format_document", new FormatCodeResponse { Success = true, FilePath = "A.cs" });
            await _opener.TryOpenEditedFileAsync("set_file_project_status", new IncludeExcludeResponse { Success = true, FilePath = "A.cs" });
            await _opener.TryOpenEditedFileAsync("delete_file", new DeleteFileResponse { Success = true, FilePath = "A.cs" });
            await _opener.TryOpenEditedFileAsync("format_document", new FileBatchResponse<FormatCodeResponse> { Success = true });
            await _opener.TryOpenEditedFileAsync("replace_file_lines", null);

            Assert.That(_opened, Is.Empty);
        }

        [Test]
        public async Task SettingOff_DoesNotOpen()
        {
            _settings.Setup(s => s.Current).Returns(new AppSettings { OpenToolFilesInEditor = false });

            await _opener.TryOpenEditedFileAsync("replace_file_lines", new ReplaceLinesResponse { Success = true, FilePath = "A.cs" });

            Assert.That(_opened, Is.Empty);
        }

        [Test]
        public async Task SettingsNotLoaded_DoesNotOpenAndDoesNotThrow()
        {
            _settings.Setup(s => s.Current).Throws(new InvalidOperationException("not loaded"));

            await _opener.TryOpenEditedFileAsync("replace_file_lines", new ReplaceLinesResponse { Success = true, FilePath = "A.cs" });

            Assert.That(_opened, Is.Empty);
        }

        [Test]
        public async Task MissingFile_DoesNotOpen()
        {
            _fileSystem.Setup(f => f.FileExists(It.IsAny<string>())).Returns(false);

            await _opener.TryOpenEditedFileAsync("replace_file_lines", new ReplaceLinesResponse { Success = true, FilePath = "A.cs" });

            Assert.That(_opened, Is.Empty);
        }

        [Test]
        public async Task NoSolution_DoesNotOpen()
        {
            _vs.Setup(v => v.GetSolutionDirectory()).Returns((string)null);

            await _opener.TryOpenEditedFileAsync("replace_file_lines", new ReplaceLinesResponse { Success = true, FilePath = "A.cs" });

            Assert.That(_opened, Is.Empty);
        }

        [Test]
        public void OpenFailure_IsSwallowed()
        {
            var opener = new ToolFileOpener(_settings.Object, _vs.Object, new PathResolver(), _fileSystem.Object,
                path => throw new InvalidOperationException("VS refused"));

            Assert.DoesNotThrowAsync(() => opener.TryOpenEditedFileAsync(
                "replace_file_lines", new ReplaceLinesResponse { Success = true, FilePath = "A.cs" }));
        }

        [Test]
        public void ContractMembers_AreNotSerializedForTheModel()
        {
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(new ReplaceLinesResponse { Success = true, FilePath = "A.cs" });

            Assert.That(json, Does.Not.Contain("WroteFile").And.Not.Contain("FileToOpen"));
        }
    }
}
