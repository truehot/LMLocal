using LMLocal.Infrastructure.Tooling.BuiltInVs.Implementations;

namespace LMLocal.Application.SubAgents.ReadRefs
{
    /// <summary>
    /// Records the references returned by successful <c>get_symbol_info</c> / <c>get_symbol_info_js</c> results into the
    /// per-run <see cref="SearchLedger"/>.
    /// </summary>
    internal static class SymbolReferenceRecorder
    {
        public const string GetSymbolInfoToolName = "get_symbol_info";
        public const string GetSymbolInfoJsToolName = "get_symbol_info_js";

        /// <summary>Adds every returned reference; returns the number of recorded lines.</summary>
        public static int TryRecord(SearchLedger ledger, object result)
        {
            if (ledger == null || result == null)
                return 0;

            int recorded = 0;

            if (result is GetSymbolInfo.SymbolInfoResponse cs && cs.Success && cs.References != null)
            {
                foreach (var reference in cs.References)
                {
                    if (reference == null || string.IsNullOrEmpty(reference.FilePath))
                        continue;
                    ledger.Add(new SearchRecord(reference.FilePath, reference.LineNumber, reference.LineText));
                    recorded++;
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

        public static bool IsSymbolTool(string toolName)
        {
            return string.Equals(toolName, GetSymbolInfoToolName, System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(toolName, GetSymbolInfoJsToolName, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
