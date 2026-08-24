using System;
using System.Collections.Generic;
using System.IO;

namespace LMLocal.Infrastructure.Syntax
{
    /// <summary>
    /// Resolves the appropriate <see cref="ISyntaxChecker"/> for a given file path based on its file extension.
    /// </summary>
    public interface ISyntaxCheckerFactory
    {
        /// <summary>
        /// Returns a checker for the file, or null if no checker supports the file extension.
        /// </summary>
        ISyntaxChecker GetChecker(string filePath);
    }

    internal sealed class SyntaxCheckerFactory : ISyntaxCheckerFactory
    {
        private readonly Dictionary<string, ISyntaxChecker> _map;

        public SyntaxCheckerFactory(CSharpSyntaxChecker csChecker, VisualBasicSyntaxChecker vbChecker)
        {
            var jsChecker = new JsSyntaxChecker(JsParseMode.Auto);
            var mjsChecker = new JsSyntaxChecker(JsParseMode.ModuleOnly);
            var cjsChecker = new JsSyntaxChecker(JsParseMode.ScriptOnly);

            _map = new Dictionary<string, ISyntaxChecker>(StringComparer.OrdinalIgnoreCase)
            {
                [".cs"] = csChecker,
                [".vb"] = vbChecker,
                [".js"] = jsChecker,
                [".mjs"] = mjsChecker,
                [".cjs"] = cjsChecker
            };
        }

        public ISyntaxChecker GetChecker(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return null;

            string extension = Path.GetExtension(filePath);
            if (string.IsNullOrEmpty(extension))
                return null;

            return _map.TryGetValue(extension, out var checker) ? checker : null;
        }
    }
}