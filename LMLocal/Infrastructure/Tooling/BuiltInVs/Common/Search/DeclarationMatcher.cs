using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace LMLocal.Infrastructure.Tooling.BuiltInVs.Common.Search
{
    /// <summary>
    /// Classifies a code line as a declaration based on the file extension.
    /// </summary>
    internal static class DeclarationMatcher
    {
        private static readonly ConcurrentDictionary<string, CompiledPatterns> Cache =
            new ConcurrentDictionary<string, CompiledPatterns>(StringComparer.OrdinalIgnoreCase);

        private static readonly CompiledPatterns Empty =
            new CompiledPatterns(new (SearchMatchKind Kind, Regex Regex)[0]);

        public static SearchMatchKind Classify(string extension, string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return SearchMatchKind.Other;

            CompiledPatterns patterns = GetPatterns(extension);
            return patterns.Classify(line);
        }

        private static CompiledPatterns GetPatterns(string extension)
        {
            string key = string.IsNullOrEmpty(extension) ? string.Empty : extension;
            return Cache.GetOrAdd(key, BuildPatterns);
        }

        private static CompiledPatterns BuildPatterns(string extension)
        {
            var definitions = BuildDefinitions(extension);
            if (definitions == null || definitions.Count == 0)
                return Empty;

            var rules = new (SearchMatchKind Kind, Regex Regex)[definitions.Count];
            for (int i = 0; i < definitions.Count; i++)
            {
                rules[i] = (definitions[i].Kind, new Regex(definitions[i].Pattern, RegexOptions.Compiled | definitions[i].Options));
            }

            return new CompiledPatterns(rules);
        }

        private static List<(SearchMatchKind Kind, string Pattern, RegexOptions Options)> BuildDefinitions(string extension)
        {
            switch (Normalize(extension))
            {
                case "cs":
                    return BuildCSharp();
                case "c":
                case "h":
                case "cpp":
                case "hpp":
                case "cc":
                case "cxx":
                case "c++":
                case "hh":
                    return BuildCpp();
                case "js":
                case "mjs":
                case "cjs":
                case "jsx":
                    return BuildJs();
                case "ts":
                case "mts":
                case "cts":
                case "tsx":
                    return BuildTs();
                case "vb":
                    return BuildVb();
                case "py":
                    return BuildPython();
                case "go":
                    return BuildGo();
                case "rs":
                    return BuildRust();
                default:
                    return BuildGeneric();
            }
        }

        private static string Normalize(string extension)
        {
            if (string.IsNullOrEmpty(extension))
                return string.Empty;

            string e = extension;
            if (e[0] == '.')
                e = e.Substring(1);

            return e.ToLowerInvariant();
        }

        private static List<(SearchMatchKind Kind, string Pattern, RegexOptions Options)> BuildCSharp()
        {
            var list = new List<(SearchMatchKind Kind, string Pattern, RegexOptions Options)>
            {
                (SearchMatchKind.Type,
                @"^\s*(?:(?:public|private|protected|internal|static|sealed|abstract|partial|readonly|ref|required|file|async|new|override|virtual)\s+)*(?:class|struct|interface|enum|record(?:\s+(?:struct|class))?|delegate)\s+[A-Za-z_]\w*",
                RegexOptions.None),

                (SearchMatchKind.Function,
                @"^\s*(?:(?:public|private|protected|internal|static)\s+)[A-Za-z_]\w*(?:\s*<[^>]*>)?\s*\([^;]*\)",
                RegexOptions.None),

                (SearchMatchKind.Function,
                @"^\s*(?!\b(?:return|throw|if|for|while|foreach|switch|case|goto|using|lock|fixed|checked|unchecked|sizeof|typeof|nameof|default|yield|catch|await|do|else|continue|break|new)\b)(?:(?:public|private|protected|internal|static|sealed|abstract|partial|async|new|override|virtual)\s+)*(?:[\w<>,.?\[\]]+\s+)+[A-Za-z_]\w*(?:\s*<[^>]*>)?\s*\([^;]*\)",
                RegexOptions.None),

                (SearchMatchKind.Property,
                @"^\s*(?:(?:public|private|protected|internal|static|virtual|abstract|sealed|override)\s+)*[\w<>,.?\[\]]+\s+[A-Za-z_]\w*\s*(?:\{[^}]*get|=>)",
                RegexOptions.None),

                (SearchMatchKind.Field,
                @"^\s*(?!\b(?:var|using|return|throw|goto|await|yield)\b)(?:(?:public|private|protected|internal|static|readonly|const|volatile)\s+)*[\w<>,.?\[\]]+\s+[A-Za-z_]\w*\s*(?:=[^;]+)?;",
                RegexOptions.None)
            };

            return list;
        }

        private static List<(SearchMatchKind Kind, string Pattern, RegexOptions Options)> BuildCpp()
        {
            var list = new List<(SearchMatchKind Kind, string Pattern, RegexOptions Options)>
            {
                (SearchMatchKind.Type,
                @"^\s*(?:template\s*<[^>]*>\s*)?(?:class|struct|enum|union|namespace|concept|typedef)\s+[A-Za-z_]\w*",
                RegexOptions.None),

                (SearchMatchKind.Function,
                @"^\s*[A-Za-z_]\w*(?:\s*::\s*[A-Za-z_]\w*)+\s*\([^;]*\)",
                RegexOptions.None),

                (SearchMatchKind.Function,
                @"^\s*(?:template\s*<[^>]*>\s*)?(?!\b(?:return|throw|if|for|while|switch|case|goto|sizeof|new|delete|static_cast|dynamic_cast|const_cast|reinterpret_cast)\b)(?:(?:static|inline|constexpr|virtual|extern|explicit|friend|mutable|register|const|volatile|noexcept|final|override)\s+)*[\w<>,.*&:]+\s+[A-Za-z_]\w*(?:\s*::\s*[A-Za-z_]\w*)?(?:\s*<[^>]*>)?\s*\([^;]*\)",
                RegexOptions.None),

                (SearchMatchKind.Field,
                @"^\s*(?!\b(?:return|throw|goto|delete|using)\b)(?:(?:static|const|constexpr|mutable|volatile|extern|inline|thread_local)\s+)*[\w<>,.*&:]+\s+[A-Za-z_]\w*\s*(?:=[^;]+)?;",
                RegexOptions.None)
            };

            return list;
        }

        private static List<(SearchMatchKind Kind, string Pattern, RegexOptions Options)> BuildJs()
        {
            var list = new List<(SearchMatchKind Kind, string Pattern, RegexOptions Options)>
            {
                (SearchMatchKind.Type,
                @"^\s*(?:export\s+)?(?:default\s+)?class\s+[A-Za-z_]\w*",
                RegexOptions.None),

                (SearchMatchKind.Function,
                @"^\s*(?:export\s+)?(?:default\s+)?(?:async\s+)?function\s*\*?\s*[A-Za-z_]\w*(?:\s*<[^>]*>)?\s*\(",
                RegexOptions.None),

                (SearchMatchKind.Function,
                @"^\s*(?:export\s+)?(?:const|let|var)\s+[A-Za-z_]\w*\s*=\s*(?:async\s+)?(?:function\b|\([^)]*\)\s*(?::\s*[^=]+)?\s*=>)",
                RegexOptions.None),

                (SearchMatchKind.Field,
                @"^\s*this\.[A-Za-z_]\w*\s*=",
                RegexOptions.None)
            };

            return list;
        }

        private static List<(SearchMatchKind Kind, string Pattern, RegexOptions Options)> BuildTs()
        {
            var list = BuildJs();

            list.Insert(0, (SearchMatchKind.Type,
                @"^\s*(?:export\s+)?(?:default\s+)?(?:abstract\s+)?(?:class|interface|enum|type|namespace|declare)\s+[A-Za-z_]\w*",
                RegexOptions.None));

            list.Add((SearchMatchKind.Property,
                @"^\s*(?:(?:public|private|protected|readonly|static|abstract)\s+)*[A-Za-z_]\w*[?!]?\s*:\s*[^=;]+;",
                RegexOptions.None));

            return list;
        }

        private static List<(SearchMatchKind Kind, string Pattern, RegexOptions Options)> BuildVb()
        {
            var options = RegexOptions.IgnoreCase;
            var list = new List<(SearchMatchKind Kind, string Pattern, RegexOptions Options)>
            {
                (SearchMatchKind.Type,
                @"^\s*(?:(?:Public|Private|Friend|Protected|Shared|ReadOnly|Partial|NotInheritable|MustInherit)\s+)*(?:Class|Module|Interface|Structure|Enum)\s+[A-Za-z_]\w*",
                options),

                (SearchMatchKind.Function,
                @"^\s*(?:(?:Public|Private|Friend|Protected|Shared|Async|Overrides|Overloads|Static|MustOverride|NotOverridable|Shadows)\s+)*(?:Function|Sub)\s+[A-Za-z_]\w*(?:\s*\([^)]*\))?",
                options),

                (SearchMatchKind.Property,
                @"^\s*(?:(?:Public|Private|Friend|Protected|Shared|ReadOnly|Default|Overrides|MustOverride|NotOverridable|Shadows)\s+)*(?:Property|Event)\s+[A-Za-z_]\w*",
                options),

                (SearchMatchKind.Field,
                @"^\s*(?:(?:Public|Private|Friend|Protected|Shared|ReadOnly|Const|Dim|Static)\s+)*[A-Za-z_]\w*\s+As\s+",
                options)
            };

            return list;
        }

        private static List<(SearchMatchKind Kind, string Pattern, RegexOptions Options)> BuildPython()
        {
            var list = new List<(SearchMatchKind Kind, string Pattern, RegexOptions Options)>
            {
                (SearchMatchKind.Type, @"^\s*class\s+[A-Za-z_]\w*", RegexOptions.None),
                (SearchMatchKind.Function, @"^\s*(?:async\s+)?def\s+[A-Za-z_]\w*\s*\(", RegexOptions.None)
            };

            return list;
        }

        private static List<(SearchMatchKind Kind, string Pattern, RegexOptions Options)> BuildGo()
        {
            var list = new List<(SearchMatchKind Kind, string Pattern, RegexOptions Options)>
            {
                (SearchMatchKind.Type, @"^\s*type\s+[A-Za-z_]\w*\s+(?:struct|interface)", RegexOptions.None),
                (SearchMatchKind.Function, @"^\s*func\s+(?:\([^)]*\)\s+)?[A-Za-z_]\w*(?:\[[^\]]*\])?\s*\(", RegexOptions.None)
            };

            return list;
        }

        private static List<(SearchMatchKind Kind, string Pattern, RegexOptions Options)> BuildRust()
        {
            var list = new List<(SearchMatchKind Kind, string Pattern, RegexOptions Options)>
            {
                (SearchMatchKind.Type, @"^\s*(?:pub\s+)?(?:struct|enum|trait|type)\s+[A-Za-z_]\w*", RegexOptions.None),
                (SearchMatchKind.Type, @"^\s*(?:pub\s+)?(?:unsafe\s+)?impl(?:\s*<[^>]*>)?\s+[A-Za-z_]\w*", RegexOptions.None),
                (SearchMatchKind.Function, @"^\s*(?:pub\s+)?(?:async\s+)?fn\s+[A-Za-z_]\w*(?:<[^>]*>)?\s*\(", RegexOptions.None)
            };

            return list;
        }

        private static List<(SearchMatchKind Kind, string Pattern, RegexOptions Options)> BuildGeneric()
        {
            var list = new List<(SearchMatchKind Kind, string Pattern, RegexOptions Options)>
            {
                (SearchMatchKind.Type,
                @"^\s*(?:(?:public|private|protected|internal|static|export|async|final|sealed|abstract)\s+)*(?:class|struct|interface|enum|type|module|namespace|record|trait|impl|delegate)\b",
                RegexOptions.None),

                (SearchMatchKind.Function,
                @"^\s*(?:(?:public|private|protected|internal|static|export|async|final|sealed|abstract)\s+)*(?:function|func|fun|def|fn)\s+[A-Za-z_]\w*\s*\(",
                RegexOptions.None)
            };

            return list;
        }

        private sealed class CompiledPatterns
        {
            private readonly (SearchMatchKind Kind, Regex Regex)[] _rules;

            public CompiledPatterns((SearchMatchKind Kind, Regex Regex)[] rules)
            {
                _rules = rules;
            }

            public SearchMatchKind Classify(string line)
            {
                for (int i = 0; i < _rules.Length; i++)
                {
                    if (_rules[i].Regex.IsMatch(line))
                        return _rules[i].Kind;
                }

                return SearchMatchKind.Other;
            }
        }
    }
}
