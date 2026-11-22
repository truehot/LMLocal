using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using NetArchTest.Rules;
using NUnit.Framework;

namespace LMLocal.Tests.Architecture
{
    /// <summary>
    /// Layer boundaries of LMLocal.
    /// </summary>
    [TestFixture]
    public class LayeringRulesTests
    {
        private const string Core = "LMLocal.Core";
        private const string Application = "LMLocal.Application";
        private const string Ports = "LMLocal.Application.Abstractions.Ports";
        private const string Infrastructure = "LMLocal.Infrastructure";
        private const string DependencyInjection = "LMLocal.Infrastructure.DependencyInjection";
        private const string Commands = "LMLocal.Commands";

        private static readonly string[] UiTypes =
        {
            "LMLocal.MainWindow",
            "LMLocal.MainWindowControl",
            "Microsoft.VisualStudio.Shell.ToolWindowPane",
        };

        private static string AssemblyPath
        {
            get { return Path.Combine(TestContext.CurrentContext.TestDirectory, "LMLocal.dll"); }
        }

        private static Types LMLocalTypes()
        {
            return Types.FromFile(AssemblyPath);
        }

        /// <summary>Guards against a false green run: every checked layer must contain types.</summary>
        [TestCase(Core)]
        [TestCase(Application)]
        [TestCase(Ports)]
        [TestCase(Infrastructure)]
        [TestCase(Commands)]
        public void Layer_ContainsTypes(string layerNamespace)
        {
            Assert.That(File.Exists(AssemblyPath), Is.True, "LMLocal.dll not found: " + AssemblyPath);

            using (var module = ModuleDefinition.ReadModule(AssemblyPath))
            {
                var count = module.Types.Count(t => t.Namespace == layerNamespace
                                                    || t.Namespace.StartsWith(layerNamespace + "."));
                Assert.That(count, Is.GreaterThan(0), "No types found in namespace " + layerNamespace);
            }
        }

        [Test]
        public void Core_DoesNotDependOn_OuterLayers()
        {
            var outer = new List<string> { Application, Infrastructure, Commands };
            outer.AddRange(UiTypes);

            var result = LMLocalTypes()
                .That().ResideInNamespace(Core)
                .ShouldNot().HaveDependencyOnAny(outer.ToArray())
                .GetResult();

            AssertRatchet(result, new string[0], "Core -> Application/Infrastructure/Commands/UI");
        }

        [Test]
        public void Application_DoesNotDependOn_Infrastructure()
        {
            var result = LMLocalTypes()
                .That().ResideInNamespace(Application)
                .ShouldNot().HaveDependencyOn(Infrastructure)
                .GetResult();

            AssertRatchet(result, LayeringAllowlist.ApplicationToInfrastructure, "Application -> Infrastructure");
        }

        /// <summary>Strict: the allowlist above must not hide DI-container use in Application.</summary>
        [Test]
        public void Application_DoesNotDependOn_DependencyInjection()
        {
            var result = LMLocalTypes()
                .That().ResideInNamespace(Application)
                .ShouldNot().HaveDependencyOn(DependencyInjection)
                .GetResult();

            AssertRatchet(result, new string[0], "Application -> Infrastructure.DependencyInjection");
        }

        [Test]
        public void Application_DoesNotDependOn_CommandsOrUi()
        {
            var forbidden = new List<string> { Commands };
            forbidden.AddRange(UiTypes);

            var result = LMLocalTypes()
                .That().ResideInNamespace(Application)
                .ShouldNot().HaveDependencyOnAny(forbidden.ToArray())
                .GetResult();

            AssertRatchet(result, new string[0], "Application -> Commands/UI");
        }

        [Test]
        public void Ports_DoNotDependOn_Infrastructure()
        {
            var result = LMLocalTypes()
                .That().ResideInNamespace(Ports)
                .ShouldNot().HaveDependencyOn(Infrastructure)
                .GetResult();

            AssertRatchet(result, new string[0], "Application.Abstractions.Ports -> Infrastructure");
        }

        [Test]
        public void Commands_DoNotDependOn_DependencyInjection()
        {
            var result = LMLocalTypes()
                .That().ResideInNamespace(Commands)
                .ShouldNot().HaveDependencyOn(DependencyInjection)
                .GetResult();

            AssertRatchet(result, LayeringAllowlist.CommandsToDependencyInjection, "Commands -> Infrastructure.DependencyInjection");
        }

        [Test]
        public void Commands_DoNotDependOn_Ui()
        {
            var result = LMLocalTypes()
                .That().ResideInNamespace(Commands)
                .ShouldNot().HaveDependencyOnAny(UiTypes)
                .GetResult();

            AssertRatchet(result, new string[0], "Commands -> MainWindow/MainWindowControl/ToolWindowPane");
        }

        /// <summary>
        /// Fails on violations outside the allowlist and on allowlist entries that no longer
        /// violate (so the list shrinks as the refactoring goes).
        /// </summary>
        private static void AssertRatchet(TestResult result, IEnumerable<string> allowlist, string rule)
        {
            var failing = new HashSet<string>(
                (result.FailingTypeNames ?? Enumerable.Empty<string>()).Select(TopLevelTypeName));
            var allowed = new HashSet<string>(allowlist);

            var newViolations = failing.Where(n => !allowed.Contains(n)).OrderBy(n => n).ToList();
            var staleEntries = allowed.Where(n => !failing.Contains(n)).OrderBy(n => n).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(newViolations, Is.Empty,
                    "New '" + rule + "' violations:\n  " + string.Join("\n  ", newViolations));
                Assert.That(staleEntries, Is.Empty,
                    "No longer violate '" + rule + "' - remove from LayeringAllowlist:\n  " + string.Join("\n  ", staleEntries));
            });
        }

        /// <summary>Folds nested and compiler-generated types (Outer+Inner, Outer/&lt;M&gt;d__1) into the outer type.</summary>
        private static string TopLevelTypeName(string fullName)
        {
            var cut = fullName.IndexOfAny(new[] { '+', '/' });
            return cut < 0 ? fullName : fullName.Substring(0, cut);
        }
    }
}
