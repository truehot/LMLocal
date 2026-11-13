using System.Collections.Generic;
using System.Linq;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common.InspectTypes;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Infrastructure.Tooling.BuiltInVs.Implementations
{
    [TestFixture]
    public class InspectTypeTests
    {
        #region ExtractAndValidateParameters

        [Test]
        public void ExtractAndValidateParameters_NullParameters_ReturnsError()
        {
            var (typeName, _, _, _, _, error) = InspectType.ExtractAndValidateParameters(null);

            Assert.That(error, Is.Not.Null.And.Contains("type_name"));
            Assert.That(typeName, Is.Null);
        }

        [Test]
        public void ExtractAndValidateParameters_MissingTypeName_ReturnsError()
        {
            var (_, _, _, _, _, error) = InspectType.ExtractAndValidateParameters(new Dictionary<string, object>());

            Assert.That(error, Is.Not.Null.And.Contains("type_name"));
        }

        [TestCase("global::LMLocal.Core.Models.SubAgentsConfig", "LMLocal.Core.Models.SubAgentsConfig")]
        [TestCase("  global::System.IO.Path  ", "System.IO.Path")]
        [TestCase("System.Collections.Generic.List`1", "System.Collections.Generic.List`1")]
        [TestCase("Global::Foo", "Global::Foo")] // the C# alias is lower-case only
        public void ExtractAndValidateParameters_StripsGlobalAliasQualifier(string input, string expected)
        {
            var (typeName, _, _, _, _, error) = InspectType.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["type_name"] = input
            });

            Assert.That(error, Is.Null);
            Assert.That(typeName, Is.EqualTo(expected));
        }

        [Test]
        public void ExtractAndValidateParameters_OnlyGlobalAlias_ReturnsError()
        {
            var (_, _, _, _, _, error) = InspectType.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["type_name"] = "global::"
            });

            Assert.That(error, Is.Not.Null.And.Contains("cannot be empty"));
        }

        [Test]
        public void ExtractAndValidateParameters_NonStringTypeName_ReturnsError()
        {
            var (_, _, _, _, _, error) = InspectType.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["type_name"] = 42
            });

            Assert.That(error, Is.Not.Null);
        }

        [Test]
        public void ExtractAndValidateParameters_WhitespaceTypeName_ReturnsError()
        {
            var (_, _, _, _, _, error) = InspectType.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["type_name"] = "   "
            });

            Assert.That(error, Is.Not.Null);
        }

        [Test]
        public void ExtractAndValidateParameters_OnlyTypeName_ReturnsDefaults()
        {
            var (typeName, projectName, nsFilter, assemblyFilter, searchMode, error) = InspectType.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["type_name"] = "Proposal"
            });

            Assert.That(error, Is.Null);
            Assert.That(typeName, Is.EqualTo("Proposal"));
            Assert.That(projectName, Is.Null);
            Assert.That(nsFilter, Is.Null);
            Assert.That(assemblyFilter, Is.Null);
            Assert.That(searchMode, Is.False);
        }

        [Test]
        public void ExtractAndValidateParameters_AllParameters_Parsed()
        {
            var (typeName, projectName, nsFilter, assemblyFilter, searchMode, error) = InspectType.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["type_name"] = "Proposal",
                ["project_name"] = "LMLocal",
                ["namespace"] = "Microsoft.VisualStudio.Language.Proposals",
                ["assembly_name"] = "Microsoft.VisualStudio.Language",
                ["search_mode"] = true
            });

            Assert.That(error, Is.Null);
            Assert.That(typeName, Is.EqualTo("Proposal"));
            Assert.That(projectName, Is.EqualTo("LMLocal"));
            Assert.That(nsFilter, Is.EqualTo("Microsoft.VisualStudio.Language.Proposals"));
            Assert.That(assemblyFilter, Is.EqualTo("Microsoft.VisualStudio.Language"));
            Assert.That(searchMode, Is.True);
        }

        [Test]
        public void ExtractAndValidateParameters_WhitespaceFilters_TreatedAsNull()
        {
            var (_, projectName, nsFilter, assemblyFilter, _, error) = InspectType.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["type_name"] = "Proposal",
                ["project_name"] = "  ",
                ["namespace"] = "",
                ["assembly_name"] = "   "
            });

            Assert.That(error, Is.Null);
            Assert.That(projectName, Is.Null);
            Assert.That(nsFilter, Is.Null);
            Assert.That(assemblyFilter, Is.Null);
        }

        [Test]
        public void ExtractAndValidateParameters_NonBoolSearchMode_Ignored()
        {
            var (_, _, _, _, searchMode, error) = InspectType.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["type_name"] = "Proposal",
                ["search_mode"] = "yes"
            });

            Assert.That(error, Is.Null);
            Assert.That(searchMode, Is.False);
        }

        #endregion

        #region MatchesNamespace

        [Test]
        public void MatchesNamespace_NullOrEmptyFilter_ReturnsTrue()
        {
            Assert.That(InspectType.MatchesNamespace("A.B", null), Is.True);
            Assert.That(InspectType.MatchesNamespace("A.B", ""), Is.True);
            Assert.That(InspectType.MatchesNamespace(null, null), Is.True);
        }

        [Test]
        public void MatchesNamespace_SubstringMatch_CaseInsensitive_ReturnsTrue()
        {
            Assert.That(InspectType.MatchesNamespace("Microsoft.VisualStudio.Language.Proposals", "visualstudio"), Is.True);
            Assert.That(InspectType.MatchesNamespace("Microsoft.VisualStudio.Language.Proposals", "PROPOSALS"), Is.True);
        }

        [Test]
        public void MatchesNamespace_NoMatch_ReturnsFalse()
        {
            Assert.That(InspectType.MatchesNamespace("Microsoft.VisualStudio.Language.Proposals", "System"), Is.False);
        }

        [Test]
        public void MatchesNamespace_NullNamespaceWithFilter_ReturnsFalse()
        {
            Assert.That(InspectType.MatchesNamespace(null, "System"), Is.False);
        }

        #endregion

        #region MatchesAssembly

        [Test]
        public void MatchesAssembly_NullOrEmptyFilter_ReturnsTrue()
        {
            Assert.That(InspectType.MatchesAssembly("Microsoft.VisualStudio.Language", null), Is.True);
            Assert.That(InspectType.MatchesAssembly("Microsoft.VisualStudio.Language", ""), Is.True);
            Assert.That(InspectType.MatchesAssembly(null, null), Is.True);
        }

        [Test]
        public void MatchesAssembly_SubstringMatch_CaseInsensitive_ReturnsTrue()
        {
            Assert.That(InspectType.MatchesAssembly("Microsoft.VisualStudio.Language", "language"), Is.True);
            Assert.That(InspectType.MatchesAssembly("Microsoft.VisualStudio.Language", "MICROSOFT.VISUALSTUDIO"), Is.True);
        }

        [Test]
        public void MatchesAssembly_NoMatch_ReturnsFalse()
        {
            Assert.That(InspectType.MatchesAssembly("Microsoft.VisualStudio.Language", "System"), Is.False);
        }

        [Test]
        public void MatchesAssembly_NullAssemblyWithFilter_ReturnsFalse()
        {
            Assert.That(InspectType.MatchesAssembly(null, "System"), Is.False);
        }

        #endregion

        #region TypeSearchQuery.Create

        [Test]
        public void Create_ShortName_HasNoNamespace()
        {
            var query = InspectType.TypeSearchQuery.Create("Proposal");

            Assert.That(query.HasNamespace, Is.False);
            Assert.That(query.ShortName, Is.EqualTo("Proposal"));
            Assert.That(query.FullNameQuery, Is.Null);
        }

        [Test]
        public void Create_QualifiedNamespace_HasNamespace()
        {
            var query = InspectType.TypeSearchQuery.Create("Microsoft.VisualStudio.Language.Proposals");

            Assert.That(query.HasNamespace, Is.True);
            Assert.That(query.ShortName, Is.EqualTo("Proposals"));
            Assert.That(query.FullNameQuery, Is.EqualTo("Microsoft.VisualStudio.Language.Proposals"));
        }

        [Test]
        public void Create_QualifiedType_ShortNameIsLastSegment()
        {
            var query = InspectType.TypeSearchQuery.Create("System.Collections.Generic.List");

            Assert.That(query.HasNamespace, Is.True);
            Assert.That(query.ShortName, Is.EqualTo("List"));
            Assert.That(query.FullNameQuery, Is.EqualTo("System.Collections.Generic.List"));
        }

        [Test]
        public void Create_GenericWithArity_StripsArityFromFullName()
        {
            var query = InspectType.TypeSearchQuery.Create("System.Collections.Generic.List`1");

            Assert.That(query.HasNamespace, Is.True);
            Assert.That(query.ShortName, Is.EqualTo("List"));
            Assert.That(query.FullNameQuery, Is.EqualTo("System.Collections.Generic.List"));
        }

        [Test]
        public void Create_ShortGeneric_StripsArity()
        {
            var query = InspectType.TypeSearchQuery.Create("List`1");

            Assert.That(query.HasNamespace, Is.False);
            Assert.That(query.ShortName, Is.EqualTo("List"));
        }

        [Test]
        public void Create_Empty_ReturnsEmptyShortName()
        {
            var query = InspectType.TypeSearchQuery.Create("");

            Assert.That(query.ShortName, Is.Empty);
        }

        #endregion

        #region IsSegmentMatch

        [Test]
        public void IsSegmentMatch_ExactMatch_ReturnsTrue()
        {
            Assert.That(
                InspectType.IsSegmentMatch("Microsoft.VisualStudio.Language.Proposals.Proposal", "microsoft.visualstudio.language.proposals.proposal"),
                Is.True);
        }

        [Test]
        public void IsSegmentMatch_QueryIsNamespacePrefix_ReturnsTrue()
        {
            Assert.That(
                InspectType.IsSegmentMatch("Microsoft.VisualStudio.Language.Proposals.Proposal", "Microsoft.VisualStudio.Language"),
                Is.True);
        }

        [Test]
        public void IsSegmentMatch_QueryIsMiddleSegments_ReturnsTrue()
        {
            Assert.That(
                InspectType.IsSegmentMatch("Microsoft.VisualStudio.Language.Proposals.Proposal", "VisualStudio.Language.Proposals"),
                Is.True);
        }

        [Test]
        public void IsSegmentMatch_QueryIsSuffix_ReturnsTrue()
        {
            Assert.That(
                InspectType.IsSegmentMatch("Microsoft.VisualStudio.Language.Proposals.Proposal", "Proposals.Proposal"),
                Is.True);
        }

        [Test]
        public void IsSegmentMatch_PartialSegment_ReturnsFalse()
        {
            // 'Database' is not a full dot-separated segment of 'DatabaseContext.X'.
            Assert.That(InspectType.IsSegmentMatch("System.DatabaseContext.X", "System.Database"), Is.False);
            // ...but a complete segment chain does match.
            Assert.That(InspectType.IsSegmentMatch("System.DatabaseContext.X", "DatabaseContext.X"), Is.True);
        }

        [Test]
        public void IsSegmentMatch_NullOrEmpty_ReturnsFalse()
        {
            Assert.That(InspectType.IsSegmentMatch(null, "X"), Is.False);
            Assert.That(InspectType.IsSegmentMatch("X", null), Is.False);
            Assert.That(InspectType.IsSegmentMatch("", ""), Is.False);
        }

        [Test]
        public void IsSegmentMatch_Unrelated_ReturnsFalse()
        {
            Assert.That(InspectType.IsSegmentMatch("System.Text", "System.Data"), Is.False);
        }

        #endregion

        #region MatchesQuery

        [Test]
        public void MatchesQuery_ShortName_SubstringMatch()
        {
            var indexed = new InspectType.IndexedType { ShortName = "MyProposalManager" };
            var query = InspectType.TypeSearchQuery.Create("Proposal");

            Assert.That(InspectType.MatchesQuery(indexed, query), Is.True);
        }

        [Test]
        public void MatchesQuery_ShortName_NoMatch()
        {
            var indexed = new InspectType.IndexedType { ShortName = "MyManager" };
            var query = InspectType.TypeSearchQuery.Create("Proposal");

            Assert.That(InspectType.MatchesQuery(indexed, query), Is.False);
        }

        [Test]
        public void MatchesQuery_Qualified_SegmentMatch()
        {
            var indexed = new InspectType.IndexedType { FullName = "Microsoft.VisualStudio.Language.Proposals.Proposal" };
            var query = InspectType.TypeSearchQuery.Create("Microsoft.VisualStudio.Language.Proposals");

            Assert.That(InspectType.MatchesQuery(indexed, query), Is.True);
        }

        [Test]
        public void MatchesQuery_Qualified_NoMatch()
        {
            var indexed = new InspectType.IndexedType
            {
                FullName = "Microsoft.VisualStudio.Language.Suggestions.ProposalDisplayedEventArgs"
            };
            var query = InspectType.TypeSearchQuery.Create("Microsoft.VisualStudio.Language.Proposals");

            Assert.That(InspectType.MatchesQuery(indexed, query), Is.False);
        }

        #endregion

        #region Match ranking

        [Test]
        public void ComputeMatchRank_ExactNameBeatsSourceAndPrefix()
        {
            var query = InspectType.TypeSearchQuery.Create("Proposal");
            var exactFromAssembly = new InspectType.MatchedType { ShortName = "Proposal", FullName = "N.Proposal", IsFromSource = false };
            var prefixFromSource = new InspectType.MatchedType { ShortName = "ProposalSource", FullName = "N.ProposalSource", IsFromSource = true };
            var containsFromSource = new InspectType.MatchedType { ShortName = "MyProposal", FullName = "N.MyProposal", IsFromSource = true };

            Assert.That(InspectType.ComputeMatchRank(exactFromAssembly, query),
                Is.LessThan(InspectType.ComputeMatchRank(prefixFromSource, query)));
            Assert.That(InspectType.ComputeMatchRank(prefixFromSource, query),
                Is.LessThan(InspectType.ComputeMatchRank(containsFromSource, query)));
        }

        [Test]
        public void ComputeMatchRank_SourceCodeBeatsReferencedAssemblies()
        {
            var query = InspectType.TypeSearchQuery.Create("Proposal");
            var fromSource = new InspectType.MatchedType { ShortName = "ProposalManager", FullName = "A.ProposalManager", IsFromSource = true };
            var fromAssembly = new InspectType.MatchedType { ShortName = "ProposalManager", FullName = "A.ProposalManager", IsFromSource = false };

            Assert.That(InspectType.ComputeMatchRank(fromSource, query),
                Is.LessThan(InspectType.ComputeMatchRank(fromAssembly, query)));
        }

        [Test]
        public void ComputeMatchRank_QualifiedQuery_RanksExactFullNameFirst()
        {
            var query = InspectType.TypeSearchQuery.Create("System.Collections.Generic.List");
            var exact = new InspectType.MatchedType { ShortName = "List", FullName = "System.Collections.Generic.List", IsFromSource = false };
            var deeper = new InspectType.MatchedType { ShortName = "Node", FullName = "System.Collections.Generic.List.Node", IsFromSource = true };

            Assert.That(InspectType.ComputeMatchRank(exact, query),
                Is.LessThan(InspectType.ComputeMatchRank(deeper, query)));
        }

        [Test]
        public void CompareMatches_SameRank_SortsByFullName()
        {
            var query = InspectType.TypeSearchQuery.Create("List");
            var first = new InspectType.MatchedType { ShortName = "List", FullName = "System.Collections.List", IsFromSource = true };
            var second = new InspectType.MatchedType { ShortName = "List", FullName = "System.Collections.Specialized.List", IsFromSource = true };

            Assert.That(InspectType.CompareMatches(first, second, query), Is.LessThan(0));
            Assert.That(InspectType.CompareMatches(second, first, query), Is.GreaterThan(0));
        }

        [Test]
        public void CompareMatches_ExactNameFirstEvenWithoutSource()
        {
            var query = InspectType.TypeSearchQuery.Create("Manager");
            var exact = new InspectType.MatchedType { ShortName = "Manager", FullName = "Z.Manager", IsFromSource = false };
            var sourcePrefix = new InspectType.MatchedType { ShortName = "ManagerSettings", FullName = "A.ManagerSettings", IsFromSource = true };
            var sourceContains = new InspectType.MatchedType { ShortName = "SettingsManager", FullName = "A.SettingsManager", IsFromSource = true };

            var ordered = new List<InspectType.MatchedType> { sourceContains, exact, sourcePrefix };
            ordered.Sort((a, b) => InspectType.CompareMatches(a, b, query));

            Assert.That(ordered, Is.EqualTo(new[] { exact, sourcePrefix, sourceContains }));
        }

        [Test]
        public void AddTopMatch_KeepsOnlyTheBestItemsInOrder()
        {
            var top = new List<int>();

            foreach (var value in new[] { 5, 3, 9, 1, 7, 2 })
                InspectType.AddTopMatch(top, value, 3, Comparer<int>.Default.Compare);

            Assert.That(top, Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public void AddTopMatch_NonPositiveLimit_KeepsNothing()
        {
            var top = new List<int>();

            InspectType.AddTopMatch(top, 1, 0, Comparer<int>.Default.Compare);

            Assert.That(top, Is.Empty);
        }

        #endregion

        #region Candidate messages

        [Test]
        public void MaxCandidates_IsFive()
        {
            Assert.That(InspectType.MaxCandidates, Is.EqualTo(5));
        }

        [Test]
        public void BuildCandidatesMessage_SingleCandidate_DoesNotMentionTruncation()
        {
            Assert.That(
                InspectType.BuildCandidatesMessage("Foo", 1, 1),
                Is.EqualTo("Type 'Foo' not found with the given parameters. 1 candidate matches by name but not by namespace/assembly_name."));
        }

        [Test]
        public void BuildCandidatesMessage_ReportsTotalAndShownCount()
        {
            Assert.That(
                InspectType.BuildCandidatesMessage("Foo", 12, 5),
                Is.EqualTo("Type 'Foo' not found with the given parameters. 12 candidates match by name but not by namespace/assembly_name; showing the first 5."));
        }

        [Test]
        public void BuildCandidatesMessage_WhenAllShown_DoesNotMentionTruncation()
        {
            Assert.That(InspectType.BuildCandidatesMessage("Foo", 3, 3), Does.Not.Contain("showing"));
        }

        [TestCase(false, "Some.Ns", null)]
        [TestCase(false, null, "Some.Assembly")]
        [TestCase(true, "Some.Ns", "Some.Assembly")]
        public void BuildNotFoundMessage_WithFilters_DoesNotListFilters(bool searchMode, string nsFilter, string assemblyFilter)
        {
            Assert.That(
                InspectType.BuildNotFoundMessage("Foo", searchMode, nsFilter, assemblyFilter),
                Is.EqualTo("Type 'Foo' not found with the given parameters."));
        }

        [Test]
        public void BuildNotFoundMessage_NoFilters_KeepsExistingMessages()
        {
            Assert.That(
                InspectType.BuildNotFoundMessage("Foo", false, null, null),
                Is.EqualTo("Type 'Foo' not found in any project or referenced assembly."));
            Assert.That(
                InspectType.BuildNotFoundMessage("Foo", true, null, null),
                Is.EqualTo("No types matching 'Foo' were found in the current solution."));
        }

        #endregion

        #region Constant values

        [Test]
        public void FormatConstantValue_Null_ReturnsNullLiteral()
        {
            Assert.That(TypeInspector.FormatConstantValue(null), Is.EqualTo("null"));
        }

        [Test]
        public void FormatConstantValue_IsCultureInvariantAndQuotesStrings()
        {
            Assert.That(TypeInspector.FormatConstantValue(1.5), Is.EqualTo("1.5"));
            Assert.That(TypeInspector.FormatConstantValue("a\"b"), Is.EqualTo("\"a\\\"b\""));
            Assert.That(TypeInspector.FormatConstantValue('a'), Is.EqualTo("'a'"));
            Assert.That(TypeInspector.FormatConstantValue(true), Is.EqualTo("true"));
            Assert.That(TypeInspector.FormatConstantValue(5000000000L), Is.EqualTo("5000000000"));
        }

        #endregion

        #region Serialization

        [Test]
        public void Response_Candidates_SerializedOnlyWhenSet()
        {
            var withCandidates = JObject.Parse(JsonConvert.SerializeObject(new InspectType.TypeInspectionResponse
            {
                Success = false,
                Candidates = new List<InspectType.TypeSearchMatch>
                {
                    new InspectType.TypeSearchMatch { FullName = "global::N.Foo", ContainingNamespace = "N", AssemblyName = "A" }
                }
            }));
            var withoutCandidates = JObject.Parse(JsonConvert.SerializeObject(new InspectType.TypeInspectionResponse { Success = false }));

            Assert.That(withCandidates["candidates"][0].Value<string>("full_name"), Is.EqualTo("global::N.Foo"));
            Assert.That(withCandidates["candidates"][0].Value<string>("containing_namespace"), Is.EqualTo("N"));
            Assert.That(withCandidates["candidates"][0].Value<string>("assembly_name"), Is.EqualTo("A"));
            Assert.That(withoutCandidates.ContainsKey("candidates"), Is.False);
        }

        [Test]
        public void Members_FalseFlags_OmittedFromJson()
        {
            var method = JObject.Parse(JsonConvert.SerializeObject(new MethodInfo
            {
                Name = "M",
                Parameters = new List<ParamInfo>()
            }));
            var property = JObject.Parse(JsonConvert.SerializeObject(new PropertyInfo
            {
                Name = "P",
                Type = "global::System.Int32",
                Accessibility = "Public"
            }));
            var field = JObject.Parse(JsonConvert.SerializeObject(new FieldInfo
            {
                Name = "F",
                Type = "global::System.Int32",
                Accessibility = "Public"
            }));

            Assert.That(method.ContainsKey("is_static"), Is.False);
            Assert.That(method.ContainsKey("is_extension"), Is.False);
            Assert.That(method.ContainsKey("is_generic"), Is.False);

            Assert.That(property.ContainsKey("is_static"), Is.False);
            Assert.That(property.ContainsKey("is_read_only"), Is.False);
            Assert.That(property.ContainsKey("is_write_only"), Is.False);
            Assert.That(property.ContainsKey("has_getter"), Is.True);
            Assert.That(property.ContainsKey("has_setter"), Is.True);

            Assert.That(field.ContainsKey("is_static"), Is.False);
            Assert.That(field.ContainsKey("is_read_only"), Is.False);
            Assert.That(field.ContainsKey("is_const"), Is.False);
            Assert.That(field.ContainsKey("constant_value"), Is.False);
        }

        [Test]
        public void Members_TrueFlagsAndConstantValue_AreSerialized()
        {
            var field = JObject.Parse(JsonConvert.SerializeObject(new FieldInfo
            {
                Name = "F",
                Type = "global::System.Int32",
                Accessibility = "Public",
                IsStatic = true,
                IsConst = true,
                ConstantValue = "5"
            }));

            Assert.That(field.Value<bool>("is_static"), Is.True);
            Assert.That(field.Value<bool>("is_const"), Is.True);
            Assert.That(field.Value<string>("constant_value"), Is.EqualTo("5"));
        }

        [Test]
        public void Result_Events_AreSerialized()
        {
            var json = JObject.Parse(JsonConvert.SerializeObject(new TypeInspectionResult
            {
                Events = new List<EventInfo>
                {
                    new EventInfo { Name = "Changed", Type = "global::System.EventHandler", Accessibility = "Public" }
                },
                TotalEvents = 1
            }));

            Assert.That(json["events"][0].Value<string>("name"), Is.EqualTo("Changed"));
            Assert.That(((JObject)json["events"][0]).ContainsKey("is_static"), Is.False);
            Assert.That(json.Value<int>("total_events"), Is.EqualTo(1));
        }

        [Test]
        public void Members_Location_SerializedLastAndOnlyWhenSet()
        {
            var inSource = JObject.Parse(JsonConvert.SerializeObject(new PropertyInfo
            {
                Name = "P",
                Type = "string",
                Accessibility = "Public",
                Line = 14
            }));
            var otherPartialFile = JObject.Parse(JsonConvert.SerializeObject(new MethodInfo
            {
                Name = "M",
                Parameters = new List<ParamInfo>(),
                Line = 7,
                FilePath = @"LMLocal\Foo.Part2.cs"
            }));
            var fromAssembly = JObject.Parse(JsonConvert.SerializeObject(new FieldInfo
            {
                Name = "F",
                Type = "int",
                Accessibility = "Public"
            }));

            Assert.That(inSource.Value<int>("line"), Is.EqualTo(14));
            Assert.That(inSource.ContainsKey("file_path"), Is.False);
            Assert.That(inSource.Properties().Last().Name, Is.EqualTo("line"));
            Assert.That(otherPartialFile.Value<int>("line"), Is.EqualTo(7));
            Assert.That(otherPartialFile.Value<string>("file_path"), Is.EqualTo(@"LMLocal\Foo.Part2.cs"));
            Assert.That(fromAssembly.ContainsKey("line"), Is.False);
            Assert.That(fromAssembly.ContainsKey("file_path"), Is.False);
        }

        [Test]
        public void Result_FilePath_OmittedForAssemblyTypes()
        {
            var fromSource = JObject.Parse(JsonConvert.SerializeObject(new TypeInspectionResult { FilePath = @"LMLocal\Foo.cs" }));
            var fromAssembly = JObject.Parse(JsonConvert.SerializeObject(new TypeInspectionResult()));

            Assert.That(fromSource.Value<string>("file_path"), Is.EqualTo(@"LMLocal\Foo.cs"));
            Assert.That(fromAssembly.ContainsKey("file_path"), Is.False);
        }

        [Test]
        public void MethodInfo_NullTypeParameters_OmittedFromJson()
        {
            var json = JObject.Parse(JsonConvert.SerializeObject(new MethodInfo
            {
                Name = "M",
                Parameters = new List<ParamInfo>()
            }));

            Assert.That(json.ContainsKey("type_parameters"), Is.False);
        }

        [Test]
        public void Response_TotalMatches_SerializedOnlyWhenSet()
        {
            var withTotal = JObject.Parse(JsonConvert.SerializeObject(new InspectType.TypeInspectionResponse { Success = true, TotalMatches = 137 }));
            var withoutTotal = JObject.Parse(JsonConvert.SerializeObject(new InspectType.TypeInspectionResponse { Success = true }));

            Assert.That(withTotal.Value<int>("total_matches"), Is.EqualTo(137));
            Assert.That(withoutTotal.ContainsKey("total_matches"), Is.False);
        }

        #endregion

        #region Completion message

        private static string CompletionMessageForMatches(int shown, int? total)
        {
            var matches = new List<InspectType.TypeSearchMatch>();
            for (int i = 0; i < shown; i++)
                matches.Add(new InspectType.TypeSearchMatch());

            return new InspectType(new SearchResultCache(), new PathResolver()).GetCompletionMessage(new InspectType.TypeInspectionResponse
            {
                Success = true,
                TypeName = "e",
                Matches = matches,
                TotalMatches = total
            });
        }

        [Test]
        public void GetCompletionMessage_TrimmedList_ReportsTotalAndCap()
        {
            Assert.That(CompletionMessageForMatches(50, 44018), Is.EqualTo("Found 44018 types matching 'e' (capped at 50)."));
        }

        [Test]
        public void GetCompletionMessage_ExactlyFiftyWithoutTrimming_DoesNotClaimCap()
        {
            Assert.That(CompletionMessageForMatches(50, null), Is.EqualTo("Found 50 types matching 'e'."));
        }

        #endregion

        #region Parameter validation details

        [Test]
        public void ExtractAndValidateParameters_DotOnly_ReturnsError()
        {
            var (_, _, _, _, _, error) = InspectType.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["type_name"] = "."
            });

            Assert.That(error, Is.Not.Null.And.Contains("type_name"));
        }

        [Test]
        public void ExtractAndValidateParameters_FiltersWithSpaces_AreTrimmed()
        {
            var (_, projectName, nsFilter, assemblyFilter, _, error) = InspectType.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["type_name"] = " Proposal ",
                ["project_name"] = " LMLocal ",
                ["namespace"] = " A.B ",
                ["assembly_name"] = " Some.Assembly "
            });

            Assert.That(error, Is.Null);
            Assert.That(projectName, Is.EqualTo("LMLocal"));
            Assert.That(nsFilter, Is.EqualTo("A.B"));
            Assert.That(assemblyFilter, Is.EqualTo("Some.Assembly"));
        }

        #endregion
    }
}
