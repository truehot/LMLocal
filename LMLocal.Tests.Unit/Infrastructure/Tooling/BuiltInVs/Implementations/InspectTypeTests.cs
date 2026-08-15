using System.Collections.Generic;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations;
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
            var result = InspectType.ExtractAndValidateParameters(null);

            Assert.That(result.error, Is.Not.Null.And.Contains("type_name"));
            Assert.That(result.typeName, Is.Null);
        }

        [Test]
        public void ExtractAndValidateParameters_MissingTypeName_ReturnsError()
        {
            var result = InspectType.ExtractAndValidateParameters(new Dictionary<string, object>());

            Assert.That(result.error, Is.Not.Null.And.Contains("type_name"));
        }

        [Test]
        public void ExtractAndValidateParameters_NonStringTypeName_ReturnsError()
        {
            var result = InspectType.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["type_name"] = 42
            });

            Assert.That(result.error, Is.Not.Null);
        }

        [Test]
        public void ExtractAndValidateParameters_WhitespaceTypeName_ReturnsError()
        {
            var result = InspectType.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["type_name"] = "   "
            });

            Assert.That(result.error, Is.Not.Null);
        }

        [Test]
        public void ExtractAndValidateParameters_OnlyTypeName_ReturnsDefaults()
        {
            var result = InspectType.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["type_name"] = "Proposal"
            });

            Assert.That(result.error, Is.Null);
            Assert.That(result.typeName, Is.EqualTo("Proposal"));
            Assert.That(result.projectName, Is.Null);
            Assert.That(result.nsFilter, Is.Null);
            Assert.That(result.assemblyFilter, Is.Null);
            Assert.That(result.searchMode, Is.False);
        }

        [Test]
        public void ExtractAndValidateParameters_AllParameters_Parsed()
        {
            var result = InspectType.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["type_name"] = "Proposal",
                ["project_name"] = "LMLocal",
                ["namespace"] = "Microsoft.VisualStudio.Language.Proposals",
                ["assembly_name"] = "Microsoft.VisualStudio.Language",
                ["search_mode"] = true
            });

            Assert.That(result.error, Is.Null);
            Assert.That(result.typeName, Is.EqualTo("Proposal"));
            Assert.That(result.projectName, Is.EqualTo("LMLocal"));
            Assert.That(result.nsFilter, Is.EqualTo("Microsoft.VisualStudio.Language.Proposals"));
            Assert.That(result.assemblyFilter, Is.EqualTo("Microsoft.VisualStudio.Language"));
            Assert.That(result.searchMode, Is.True);
        }

        [Test]
        public void ExtractAndValidateParameters_WhitespaceFilters_TreatedAsNull()
        {
            var result = InspectType.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["type_name"] = "Proposal",
                ["project_name"] = "  ",
                ["namespace"] = "",
                ["assembly_name"] = "   "
            });

            Assert.That(result.error, Is.Null);
            Assert.That(result.projectName, Is.Null);
            Assert.That(result.nsFilter, Is.Null);
            Assert.That(result.assemblyFilter, Is.Null);
        }

        [Test]
        public void ExtractAndValidateParameters_NonBoolSearchMode_Ignored()
        {
            var result = InspectType.ExtractAndValidateParameters(new Dictionary<string, object>
            {
                ["type_name"] = "Proposal",
                ["search_mode"] = "yes"
            });

            Assert.That(result.error, Is.Null);
            Assert.That(result.searchMode, Is.False);
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
    }
}
