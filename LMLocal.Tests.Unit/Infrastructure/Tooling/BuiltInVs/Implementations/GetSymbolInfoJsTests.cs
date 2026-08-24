using System.Collections.Generic;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Implementations
{
    [TestFixture]
    public class GetSymbolInfoJsTests
    {
        #region ExtractAndValidateParameters

        [Test]
        public void ExtractAndValidateParameters_NullParameters_ReturnsError()
        {
            var (symbolName, _, _, _, _, error) = GetSymbolInfoJs.ExtractAndValidateParameters(null);

            Assert.That(error, Is.Not.Null.And.Contains("Parameters"));
            Assert.That(symbolName, Is.Null);
        }

        [Test]
        public void ExtractAndValidateParameters_MissingSymbolName_ReturnsError()
        {
            var (_, _, _, _, _, error) = GetSymbolInfoJs.ExtractAndValidateParameters(new Dictionary<string, object>());

            Assert.That(error, Is.Not.Null.And.Contains("symbol_name"));
        }

        [Test]
        public void ExtractAndValidateParameters_NonStringSymbolName_ReturnsError()
        {

            Assert.That(GetSymbolInfoJs.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["symbol_name"] = 42
            }).error, Is.Not.Null);
        }

        [Test]
        public void ExtractAndValidateParameters_WhitespaceSymbolName_ReturnsError()
        {

            Assert.That(GetSymbolInfoJs.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["symbol_name"] = "   "
            }).error, Is.Not.Null);
        }

        [Test]
        public void ExtractAndValidateParameters_TooShortSymbolName_ReturnsError()
        {

            Assert.That(GetSymbolInfoJs.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["symbol_name"] = "ab"
            }).error, Is.Not.Null.And.Contains("3"));
        }

        [Test]
        public void ExtractAndValidateParameters_OnlySymbolName_ReturnsDefaults()
        {

            Assert.That(GetSymbolInfoJs.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["symbol_name"] = "validate"
            }).error, Is.Null);
            Assert.That(GetSymbolInfoJs.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["symbol_name"] = "validate"
            }).symbolName, Is.EqualTo("validate"));
            Assert.That(GetSymbolInfoJs.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["symbol_name"] = "validate"
            }).filePath, Is.Null);
            Assert.That(GetSymbolInfoJs.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["symbol_name"] = "validate"
            }).includeReferences, Is.True);
            Assert.That(GetSymbolInfoJs.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["symbol_name"] = "validate"
            }).pageSize, Is.EqualTo(50));
            Assert.That(GetSymbolInfoJs.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["symbol_name"] = "validate"
            }).pageToken, Is.Null);
        }

        [Test]
        public void ExtractAndValidateParameters_TrimsSymbolName()
        {
            var result = GetSymbolInfoJs.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["symbol_name"] = "  validate  "
            });

            Assert.That(result.error, Is.Null);
            Assert.That(result.symbolName, Is.EqualTo("validate"));
        }

        [Test]
        public void ExtractAndValidateParameters_AllParameters_Parsed()
        {
            var result = GetSymbolInfoJs.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["symbol_name"] = "validate",
                ["file_path"] = "src/utils/validation.js",
                ["include_references"] = false,
                ["max_references"] = 10,
                ["page_token"] = "2"
            });

            Assert.That(result.error, Is.Null);
            Assert.That(result.symbolName, Is.EqualTo("validate"));
            Assert.That(result.filePath, Is.EqualTo("src/utils/validation.js"));
            Assert.That(result.includeReferences, Is.False);
            Assert.That(result.pageSize, Is.EqualTo(10));
            Assert.That(result.pageToken, Is.EqualTo("2"));
        }

        [Test]
        public void ExtractAndValidateParameters_MaxReferences_Clamped()
        {
            var result = GetSymbolInfoJs.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["symbol_name"] = "validate",
                ["max_references"] = 5000
            });

            Assert.That(result.error, Is.Null);
            Assert.That(result.pageSize, Is.EqualTo(200));
        }

        [Test]
        public void ExtractAndValidateParameters_NonBoolIncludeReferences_Ignored()
        {
            var result = GetSymbolInfoJs.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["symbol_name"] = "validate",
                ["include_references"] = "yes"
            });

            Assert.That(result.error, Is.Null);
            Assert.That(result.includeReferences, Is.True);
        }

        [Test]
        public void ExtractAndValidateParameters_NonIntMaxReferences_Ignored()
        {
            var result = GetSymbolInfoJs.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["symbol_name"] = "validate",
                ["max_references"] = "many"
            });

            Assert.That(result.error, Is.Null);
            Assert.That(result.pageSize, Is.EqualTo(50));
        }

        [Test]
        public void ExtractAndValidateParameters_RemovedParameters_AreIgnored()
        {
            // max_depth / include_import_chain / extension_filter were removed from the tool contract.
            // They must not affect parsing (backward-compatible: silently ignored).
            var result = GetSymbolInfoJs.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["symbol_name"] = "validate",
                ["max_depth"] = 5,
                ["include_import_chain"] = false,
                ["extension_filter"] = ".ts"
            });

            Assert.That(result.error, Is.Null);
            Assert.That(result.symbolName, Is.EqualTo("validate"));
            Assert.That(result.includeReferences, Is.True);
            Assert.That(result.pageSize, Is.EqualTo(50));
        }

        #endregion

        #region BuildFileGroups

        [Test]
        public void BuildFileGroups_EmptyInputs_ReturnsEmptyList()
        {
            var groups = GetSymbolInfoJs.BuildFileGroups(
                new List<GetSymbolInfoJs.JsDefinitionItem>(),
                new List<GetSymbolInfoJs.JsReferenceItem>(),
                new List<GetSymbolInfoJs.JsImporterLink>());

            Assert.That(groups, Is.Empty);
        }

        [Test]
        public void BuildFileGroups_GroupsDefinitionsAndReferencesByFile()
        {
            var definitions = new List<GetSymbolInfoJs.JsDefinitionItem>
            {
                new GetSymbolInfoJs.JsDefinitionItem
                {
                    FilePath = "a.js",
                    Line = 1,
                    Column = 1,
                    DeclarationType = "const",
                    SymbolKind = "VariableDeclaration"
                }
            };
            var references = new List<GetSymbolInfoJs.JsReferenceItem>
            {
                new GetSymbolInfoJs.JsReferenceItem
                {
                    FilePath = "a.js",
                    LineNumber = 5,
                    LineText = "const x = UIText;",
                    Context = "identifier"
                },
                new GetSymbolInfoJs.JsReferenceItem
                {
                    FilePath = "b.js",
                    LineNumber = 2,
                    LineText = "foo(UIText)",
                    Context = "call"
                }
            };

            var groups = GetSymbolInfoJs.BuildFileGroups(definitions, references, new List<GetSymbolInfoJs.JsImporterLink>());

            Assert.That(groups, Has.Count.EqualTo(2));

            var groupA = groups.Find(g => g.FilePath == "a.js");
            Assert.That(groupA, Is.Not.Null);
            Assert.That(groupA.Definitions, Has.Count.EqualTo(1));
            Assert.That(groupA.References, Has.Count.EqualTo(1));
            Assert.That(groupA.ImportSource, Is.Null);

            var groupB = groups.Find(g => g.FilePath == "b.js");
            Assert.That(groupB, Is.Not.Null);
            Assert.That(groupB.Definitions, Is.Empty);
            Assert.That(groupB.References, Has.Count.EqualTo(1));
        }

        [Test]
        public void BuildFileGroups_ImporterSetsImportSource()
        {
            var importers = new List<GetSymbolInfoJs.JsImporterLink>
            {
                new GetSymbolInfoJs.JsImporterLink
                {
                    FilePath = "a.js",
                    ImportSource = "./constants/strings"
                }
            };

            var groups = GetSymbolInfoJs.BuildFileGroups(
                new List<GetSymbolInfoJs.JsDefinitionItem>(),
                new List<GetSymbolInfoJs.JsReferenceItem>(),
                importers);

            Assert.That(groups, Has.Count.EqualTo(1));
            Assert.That(groups[0].FilePath, Is.EqualTo("a.js"));
            Assert.That(groups[0].ImportSource, Is.EqualTo("./constants/strings"));
        }

        [Test]
        public void BuildFileGroups_ImporterWithoutReferences_CreatesGroup()
        {
            var importers = new List<GetSymbolInfoJs.JsImporterLink>
            {
                new GetSymbolInfoJs.JsImporterLink
                {
                    FilePath = "importer.js",
                    ImportSource = "./lib"
                }
            };

            var groups = GetSymbolInfoJs.BuildFileGroups(
                new List<GetSymbolInfoJs.JsDefinitionItem>(),
                new List<GetSymbolInfoJs.JsReferenceItem>(),
                importers);

            Assert.That(groups, Has.Count.EqualTo(1));
            Assert.That(groups[0].Definitions, Is.Empty);
            Assert.That(groups[0].References, Is.Empty);
            Assert.That(groups[0].ImportSource, Is.EqualTo("./lib"));
        }

        [Test]
        public void BuildFileGroups_ItemsWithoutFilePath_AreSkipped()
        {
            var definitions = new List<GetSymbolInfoJs.JsDefinitionItem>
            {
                new GetSymbolInfoJs.JsDefinitionItem { FilePath = null, Line = 1 }
            };
            var references = new List<GetSymbolInfoJs.JsReferenceItem>
            {
                new GetSymbolInfoJs.JsReferenceItem { FilePath = "", LineNumber = 1 }
            };

            var groups = GetSymbolInfoJs.BuildFileGroups(definitions, references, new List<GetSymbolInfoJs.JsImporterLink>());

            Assert.That(groups, Is.Empty);
        }

        [Test]
        public void BuildFileGroups_FilePathComparison_IsCaseInsensitive()
        {
            var definitions = new List<GetSymbolInfoJs.JsDefinitionItem>
            {
                new GetSymbolInfoJs.JsDefinitionItem { FilePath = "SRC/A.JS", Line = 1 }
            };
            var references = new List<GetSymbolInfoJs.JsReferenceItem>
            {
                new GetSymbolInfoJs.JsReferenceItem { FilePath = "src/a.js", LineNumber = 2 }
            };

            var groups = GetSymbolInfoJs.BuildFileGroups(definitions, references, new List<GetSymbolInfoJs.JsImporterLink>());

            Assert.That(groups, Has.Count.EqualTo(1));
            Assert.That(groups[0].Definitions, Has.Count.EqualTo(1));
            Assert.That(groups[0].References, Has.Count.EqualTo(1));
        }

        #endregion
    }
}
