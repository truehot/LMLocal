using System.Collections.Generic;
using System.Linq;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.Persistence;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Snapshot;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Implementations
{
    /// <summary>
    /// The IPC RunTool command (E2E tests) maps positional arguments to parameters: Required first, then the rest in
    /// declaration order. A new optional parameter declared in the middle shifts every later positional argument
    /// (format_document "file|false" once sent "false" as a file path), so new optional parameters must come last.
    /// </summary>
    [TestFixture]
    public class ToolParameterOrderTests
    {
        /// <summary>
        /// Same ordering rule as IpcCommandHandler: Required in declared order, then the remaining properties in declaration order.
        /// </summary>
        private static List<string> PositionalOrder(ToolDefinition definition)
        {
            var props = definition.Parameters.Properties;
            var required = definition.Parameters.Required ?? new List<string>();
            var ordered = required.Where(props.ContainsKey).ToList();
            ordered.AddRange(props.Keys.Where(k => !ordered.Contains(k)));
            return ordered;
        }

        [Test]
        public void FormatDocument_PositionalOrderIsUnchanged()
        {
            var tool = new FormatDocument(
                Mock.Of<IVsDependencies>(), Mock.Of<IPathResolver>(), Mock.Of<ISnapshotManager>(), Mock.Of<IFileSystem>());

            Assert.That(PositionalOrder(tool.GetToolInfo()), Is.EqualTo(new[] { "file_path", "organize_usings", "file_paths" }));
        }

        [Test]
        public void SetFileProjectStatus_PositionalOrderIsUnchanged()
        {
            var tool = new SetFileProjectStatus(
                Mock.Of<IVsDependencies>(), Mock.Of<IPathResolver>(), Mock.Of<ISnapshotManager>(), Mock.Of<IFileSystem>());

            Assert.That(PositionalOrder(tool.GetToolInfo()), Is.EqualTo(new[] { "file_path", "project_path", "include", "file_paths" }));
        }
    }
}
