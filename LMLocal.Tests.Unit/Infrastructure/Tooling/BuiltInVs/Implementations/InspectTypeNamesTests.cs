using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.InspectTypes;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Implementations
{
    /// <summary>inspect_type: the single exact-name match among search results, and 'type_names'.</summary>
    [TestFixture]
    public class InspectTypeNamesTests
    {
        private static InspectType Tool() => new InspectType(new SearchResultCache(), new PathResolver());

        private static InspectType.MatchedType Match(string fullName)
        {
            int dot = fullName.LastIndexOf('.');
            return new InspectType.MatchedType { FullName = fullName, ShortName = dot >= 0 ? fullName.Substring(dot + 1) : fullName, IsFromSource = true };
        }

        #region Exact-name match

        [Test]
        public void PickSingleExactMatch_TypeNextToItsTestsAndInterface_PicksTheType()
        {
            var query = InspectType.TypeSearchQuery.Create("searchfilecontent");
            var matches = new List<InspectType.MatchedType>
            {
                Match("A.Tests.SearchFileContentTextsTests"),
                Match("A.ISearchFileContent"),
                Match("A.SearchFileContent")
            };

            Assert.That(InspectType.PickSingleExactMatch(matches, query), Is.EqualTo(2));
        }

        [Test]
        public void PickSingleExactMatch_SameNameInTwoNamespaces_PicksNone()
        {
            var query = InspectType.TypeSearchQuery.Create("Proposal");
            var matches = new List<InspectType.MatchedType> { Match("A.Proposal"), Match("B.Proposal"), Match("A.ProposalBase") };

            Assert.That(InspectType.PickSingleExactMatch(matches, query), Is.EqualTo(-1));
        }

        [Test]
        public void PickSingleExactMatch_NoExactName_PicksNone()
        {
            var query = InspectType.TypeSearchQuery.Create("Pagin");
            var matches = new List<InspectType.MatchedType> { Match("A.ToolPagination"), Match("A.SearchResultPaginator") };

            Assert.That(InspectType.PickSingleExactMatch(matches, query), Is.EqualTo(-1));
            Assert.That(InspectType.PickSingleExactMatch(null, query), Is.EqualTo(-1));
        }

        [Test]
        public void PickSingleExactMatch_QualifiedQuery_ComparesFullName()
        {
            var query = InspectType.TypeSearchQuery.Create("A.Search");
            var matches = new List<InspectType.MatchedType> { Match("A.Search.Scope"), Match("X.A.Search"), Match("A.Search") };

            Assert.That(InspectType.PickSingleExactMatch(matches, query), Is.EqualTo(2));
        }

        [Test]
        public void IsExactName_IgnoresGenericArity()
        {
            var query = InspectType.TypeSearchQuery.Create("List`1");

            Assert.That(InspectType.IsExactName(Match("System.Collections.Generic.List"), query), Is.True);
            Assert.That(InspectType.IsExactName(Match("System.Collections.Generic.ListBase"), query), Is.False);
        }

        [Test]
        public void ComputeMatchRank_StillPutsExactNameFirst()
        {
            var query = InspectType.TypeSearchQuery.Create("ToolPagination");

            Assert.That(InspectType.ComputeMatchRank(Match("A.ToolPagination"), query),
                Is.LessThan(InspectType.ComputeMatchRank(Match("A.ToolPaginationTests"), query)));
        }

        #endregion

        #region type_names parsing

        [Test]
        public void ExtractTypeNames_TopLevelNameFirst_DuplicatesAfterTrimDropped()
        {
            var parameters = new Dictionary<string, object> { ["type_name"] = "A" };
            var (names, error) = InspectType.ExtractTypeNames(parameters, new JArray("B", " A ", "B", "", "C"));

            Assert.That(error, Is.Null);
            Assert.That(names, Is.EqualTo(new[] { "A", "B", "C" }));
        }

        [Test]
        public void ExtractTypeNames_ArrayAsJsonString_IsParsed()
        {
            var (names, error) = InspectType.ExtractTypeNames(new Dictionary<string, object>(), "[\"X\", \"Y\"]");

            Assert.That(error, Is.Null);
            Assert.That(names, Is.EqualTo(new[] { "X", "Y" }));
        }

        [Test]
        public void ExtractTypeNames_Errors()
        {
            Assert.That(InspectType.ExtractTypeNames(new Dictionary<string, object>(), new JArray()).error, Does.Contain("no non-empty name"));
            Assert.That(InspectType.ExtractTypeNames(new Dictionary<string, object>(), new JArray(1, 2)).error, Does.Contain("array of strings"));
            Assert.That(InspectType.ExtractTypeNames(new Dictionary<string, object> { ["type_name"] = 5L }, new JArray("A")).error, Does.Contain("'type_name' must be a string"));

            var many = new JArray(Enumerable.Range(1, InspectType.MaxTypeNames + 1).Select(i => "T" + i));
            Assert.That(InspectType.ExtractTypeNames(new Dictionary<string, object>(), many).error, Does.Contain("At most 10 type names"));
        }

        [Test]
        public void NormalizeTypeName_StripsGlobalAndRejectsDot()
        {
            Assert.That(InspectType.NormalizeTypeName(" global::A.B ").typeName, Is.EqualTo("A.B"));
            Assert.That(InspectType.NormalizeTypeName(".").error, Does.Contain("cannot be '.'"));
            Assert.That(InspectType.NormalizeTypeName("global::").error, Does.Contain("cannot be empty"));
        }

        [Test]
        public void ExtractFilters_AppliesToEveryName()
        {
            var (project, ns, assembly, searchMode) = InspectType.ExtractFilters(new Dictionary<string, object>
            {
                ["project_name"] = " P ",
                ["namespace"] = "N",
                ["assembly_name"] = "",
                ["search_mode"] = true
            });

            Assert.That(project, Is.EqualTo("P"));
            Assert.That(ns, Is.EqualTo("N"));
            Assert.That(assembly, Is.Null);
            Assert.That(searchMode, Is.True);
        }

        [Test]
        public void ExtractAndValidateParameters_MissingName_MentionsTypeNames()
        {
            var error = InspectType.ExtractAndValidateParameters(new Dictionary<string, object>()).error;

            Assert.That(error, Does.Contain("'type_name' (or 'type_names')"));
        }

        #endregion

        #region type_names execution without a solution

        [Test]
        public async Task ExecuteAsync_TypeNamesTooMany_ReturnsMultiError()
        {
            var parameters = new Dictionary<string, object>
            {
                ["type_names"] = new JArray(Enumerable.Range(1, 11).Select(i => "T" + i))
            };

            var result = await Tool().ExecuteAsync(parameters) as InspectType.TypeInspectionMultiResponse;

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Success, Is.False);
            Assert.That(result.Types, Is.Empty);
        }

        [Test]
        public async Task ExecuteAsync_InvalidNames_FailOnlyTheirBlocks()
        {
            var parameters = new Dictionary<string, object> { ["type_names"] = new JArray(".", "global::") };

            var result = await Tool().ExecuteAsync(parameters) as InspectType.TypeInspectionMultiResponse;

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Does.Contain("No type was inspected"));
            Assert.That(result.Types.Select(t => t.TypeName), Is.EqualTo(new[] { ".", "global::" }));
            Assert.That(result.Types.All(t => !t.Success && t.ErrorMessage != null), Is.True);
        }

        #endregion

        #region Messages, JSON and schema

        [Test]
        public void GetCompletionMessage_Multi_ListsEveryBlock()
        {
            var multi = new InspectType.TypeInspectionMultiResponse
            {
                Success = true,
                Types = new List<InspectType.TypeInspectionResponse>
                {
                    new InspectType.TypeInspectionResponse { Success = true, TypeName = "A", Data = new TypeInspectionResult { FullName = "N.A" } },
                    new InspectType.TypeInspectionResponse { Success = true, TypeName = "B", Matches = new List<InspectType.TypeSearchMatch> { new InspectType.TypeSearchMatch(), new InspectType.TypeSearchMatch() } },
                    new InspectType.TypeInspectionResponse { Success = false, TypeName = "C", ErrorMessage = "x" }
                }
            };

            Assert.That(Tool().GetCompletionMessage(multi), Is.EqualTo("Inspected 1 of 3 types ('A': inspected, 'B': 2 matches, 'C': failed)."));
        }

        [Test]
        public void GetCompletionMessage_MultiWithoutBlocks_ReportsError()
        {
            var multi = new InspectType.TypeInspectionMultiResponse { Success = false, ErrorMessage = "bad", Types = new List<InspectType.TypeInspectionResponse>() };

            Assert.That(Tool().GetCompletionMessage(multi), Is.EqualTo("Inspection failed: bad"));
        }

        [TestCase(null, "Inspected N.A: 2 methods; 2 other matches")]
        [TestCase(12, "Inspected N.A: 2 methods; 11 other matches")]
        public void GetCompletionMessage_ExactMatchWithOthers_CountsOthers(int? total, string expected)
        {
            var resp = new InspectType.TypeInspectionResponse
            {
                Success = true,
                TypeName = "A",
                Data = new TypeInspectionResult { FullName = "N.A", TotalMethods = 2 },
                OtherMatches = new List<string> { "global::N.ATests", "global::N.IA" },
                TotalMatches = total
            };

            Assert.That(Tool().GetCompletionMessage(resp), Is.EqualTo(expected));
        }

        [Test]
        public void GetProcessingMessage_TypeNames_ListsNames()
        {
            var tool = Tool();

            Assert.That(tool.GetProcessingMessage(new Dictionary<string, object> { ["type_names"] = new JArray("A", "B") }),
                Is.EqualTo("Inspecting types 'A', 'B'..."));
            Assert.That(tool.GetProcessingMessage(new Dictionary<string, object> { ["type_names"] = new JArray("A"), ["search_mode"] = true, ["namespace"] = "N" }),
                Is.EqualTo("Searching for types matching 'A' in namespace 'N'..."));
        }

        [Test]
        public void Json_OtherMatchesOnlyWhenSet_MultiHasTypes()
        {
            string single = JsonConvert.SerializeObject(new InspectType.TypeInspectionResponse { Success = true, TypeName = "A" });
            string withOthers = JsonConvert.SerializeObject(new InspectType.TypeInspectionResponse { Success = true, OtherMatches = new List<string> { "global::N.B" } });
            var multi = JObject.Parse(JsonConvert.SerializeObject(new InspectType.TypeInspectionMultiResponse
            {
                Success = true,
                Types = new List<InspectType.TypeInspectionResponse> { new InspectType.TypeInspectionResponse { Success = true, TypeName = "A" } }
            }));

            Assert.That(single, Does.Not.Contain("other_matches"));
            Assert.That(withOthers, Does.Contain("\"other_matches\":[\"global::N.B\"]"));
            Assert.That(multi["types"][0]["type_name"].Value<string>(), Is.EqualTo("A"));
            Assert.That(multi["error_message"], Is.Null);
        }

        [Test]
        public void Schema_TypeNamesIsLastStringArray_NothingRequired()
        {
            var def = Tool().GetToolInfo();
            var props = def.Parameters.Properties;

            Assert.That(props.Keys.Last(), Is.EqualTo("type_names"));
            Assert.That(props["type_names"].Type, Is.EqualTo("array"));
            Assert.That(props["type_names"].Items.Type, Is.EqualTo("string"));
            Assert.That(def.Parameters.Required, Is.Empty);
            Assert.That(props["type_name"].Description, Does.Contain("Required unless 'type_names' is passed."));
            Assert.That(def.Description, Does.Contain("'other_matches'"));
        }

        #endregion
    }
}
