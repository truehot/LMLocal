using System;
using System.Collections.Generic;
using Acornima;
using LMLocal.Core.Common;

namespace LMLocal.Infrastructure.Syntax
{
    /// <summary>
    /// Determines how JavaScript is parsed depending on the file type.
    /// </summary>
    internal enum JsParseMode
    {
        /// <summary>
        /// Tries module first and falls back to script.
        /// </summary>
        Auto,

        /// <summary>
        /// Parses only as an ES module. Used for .mjs files.
        /// </summary>
        ModuleOnly,

        /// <summary>
        /// Parses only as a script. Used for .cjs files.
        /// </summary>
        ScriptOnly
    }

    /// <summary>
    /// Validates JavaScript syntax with the Acornima parser.
    /// </summary>
    internal sealed class JsSyntaxChecker : ISyntaxChecker
    {
        private const string FallbackErrorId = "JS-SYNTAX";

        private static readonly ParserOptions ParserOptions = ParserOptions.Default;

        private readonly JsParseMode _mode;

        public JsSyntaxChecker(JsParseMode mode = JsParseMode.Auto)
        {
            _mode = mode;
        }

        public bool IsSyntaxValid(string sourceCode, out List<SyntaxError> errors)
        {
            errors = new List<SyntaxError>();

            if (string.IsNullOrEmpty(sourceCode))
            {
                return true;
            }

            try
            {
                if (_mode == JsParseMode.ModuleOnly)
                {
                    if (TryParseModule(sourceCode, out SyntaxError moduleError))
                    {
                        return true;
                    }

                    errors.Add(moduleError);
                    return false;
                }

                if (_mode == JsParseMode.ScriptOnly)
                {
                    if (TryParseScript(sourceCode, out SyntaxError scriptError))
                    {
                        return true;
                    }

                    errors.Add(scriptError);
                    return false;
                }

                if (TryParseModule(sourceCode, out SyntaxError autoModuleError))
                {
                    return true;
                }

                if (TryParseScript(sourceCode, out SyntaxError autoScriptError))
                {
                    return true;
                }

                errors.Add(ChooseError(autoModuleError, autoScriptError));
                return false;
            }
            catch (Exception ex)
            {
                InternalLogger.Warn("Acornima could not validate JS syntax: " + ex.Message);
                errors.Clear();
                return true;
            }
        }

        private static bool TryParseModule(string sourceCode, out SyntaxError error)
        {
            try
            {
                new Parser(ParserOptions).ParseModule(sourceCode);
                error = null;
                return true;
            }
            catch (ParseErrorException ex)
            {
                error = ToSyntaxError(ex);
                return false;
            }
        }

        private static bool TryParseScript(string sourceCode, out SyntaxError error)
        {
            try
            {
                new Parser(ParserOptions).ParseScript(sourceCode, strict: false);
                error = null;
                return true;
            }
            catch (ParseErrorException ex)
            {
                error = ToSyntaxError(ex);
                return false;
            }
        }

        private static SyntaxError ToSyntaxError(ParseErrorException ex)
        {
            ParseError parseError = ex.Error;

            string id = parseError != null && !string.IsNullOrEmpty(parseError.Code)
                ? parseError.Code
                : FallbackErrorId;

            string message = !string.IsNullOrEmpty(ex.Description)
                ? ex.Description
                : parseError != null && !string.IsNullOrEmpty(parseError.Description)
                    ? parseError.Description
                    : ex.Message;

            int line = ex.LineNumber > 0 ? ex.LineNumber : parseError != null ? parseError.LineNumber : 0;
            int column = ex.Column > 0 ? ex.Column : parseError != null ? parseError.Column : 0;

            return new SyntaxError
            {
                Id = id,
                StartLine = line,
                StartColumn = column,
                EndLine = line,
                EndColumn = column + 1,
                Message = message,
                Severity = "Error"
            };
        }

        private static SyntaxError ChooseError(SyntaxError moduleError, SyntaxError scriptError)
        {
            if (moduleError == null)
            {
                return scriptError;
            }

            if (scriptError == null)
            {
                return moduleError;
            }

            if (moduleError.StartLine != scriptError.StartLine)
            {
                return moduleError.StartLine > scriptError.StartLine ? moduleError : scriptError;
            }

            return moduleError.StartColumn >= scriptError.StartColumn ? moduleError : scriptError;
        }
    }
}
