using LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations;

namespace LMLocal.Application.SubAgents.ReadRefs
{
    /// <summary>
    /// Records the references returned by successful <c>get_symbol_info</c> / <c>get_symbol_info_js</c> results into the
    /// per-run <see cref="SearchLedger"/>, and the declaration lines of <c>get_symbol_info</c> definitions that carry their text:
    /// a marker on a declaration line the tool returned is then expanded instead of being reported as unresolved.
    /// </summary>
    internal static class SymbolReferenceRecorder
    {
        public const string GetSymbolInfoToolName = "get_symbol_info";
        public const string GetSymbolInfoJsToolName = "get_symbol_info_js";

        /// <summary>Adds every returned reference and declaration line with text; returns the number of recorded lines.</summary>
        public static int TryRecord(SearchLedger ledger, object result)
        {
            if (ledger == null || result == null)
                return 0;

            int recorded = 0;

            if (result is GetSymbolInfo.SymbolInfoResponse cs && cs.Success)
            {
                recorded += RecordDefinitions(ledger, cs.Definitions);

                foreach (var reference in cs.References ?? new System.Collections.Generic.List<GetSymbolInfo.ReferenceItem>())
                {
                    if (reference == null || string.IsNullOrEmpty(reference.FilePath))
                        continue;
                    ledger.Add(new SearchRecord(reference.FilePath, reference.LineNumber, reference.LineText));
                    recorded++;
                }
            }
            else if (result is GetSymbolInfo.SymbolInfoMultiResponse multi && multi.Symbols != null)
            {
                foreach (var block in multi.Symbols)
                {
                    if (block != null && block.Success)
                        recorded += RecordDefinitions(ledger, block.Definitions);
                }
            }
            else if (result is GetSymbolInfoJs.JsSymbolInfoResponse js && js.Success && js.Files != null)
            {
                foreach (var group in js.Files)
                {
                    if (group?.References == null)
                        continue;

                    foreach (var reference in group.References)
                    {
                        if (reference == null)
                            continue;
                        var path = string.IsNullOrEmpty(reference.FilePath) ? group.FilePath : reference.FilePath;
                        if (string.IsNullOrEmpty(path))
                            continue;
                        ledger.Add(new SearchRecord(path, reference.LineNumber, reference.LineText));
                        recorded++;
                    }
                }
            }

            return recorded;
        }

        /// <summary>Declaration lines whose text the tool returned; a definition without text is not recorded (there is nothing to expand it to).</summary>
        private static int RecordDefinitions(SearchLedger ledger, System.Collections.Generic.IEnumerable<GetSymbolInfo.DefinitionItem> definitions)
        {
            int recorded = 0;
            foreach (var definition in definitions ?? System.Linq.Enumerable.Empty<GetSymbolInfo.DefinitionItem>())
            {
                if (definition == null || string.IsNullOrEmpty(definition.FilePath) || string.IsNullOrEmpty(definition.LineText))
                    continue;
                ledger.Add(new SearchRecord(definition.FilePath, definition.Line, definition.LineText));
                recorded++;
            }
            return recorded;
        }

        public static bool IsSymbolTool(string toolName)
        {
            return string.Equals(toolName, GetSymbolInfoToolName, System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(toolName, GetSymbolInfoJsToolName, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
