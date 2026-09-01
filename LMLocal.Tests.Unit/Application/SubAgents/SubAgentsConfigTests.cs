using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LMLocal.Application.Abstractions.Ports;
using LMLocal.Core.Common;
using LMLocal.Core.Models;
using LMLocal.Infrastructure.SubAgents;
using LMLocal.Infrastructure.Tooling;
using LMLocal.Infrastructure.Tooling.BuiltInVs;
using LMLocal.Tests.Unit.Infrastructure;
using Moq;
using NUnit.Framework;

namespace LMLocal.Tests.Unit.Application.SubAgents
{
    [TestFixture]
    public class SubAgentsConfigTests
    {
        private static SubAgentsConfig Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new SubAgentsConfig();
            }

            return json.FromJson<SubAgentsConfig>() ?? new SubAgentsConfig();
        }

        // =========================================================================
        // Parser — single agent
        // =========================================================================

        [Test]
        public void Parse_Empty_ReturnsNoAgents()
        {
            var cfg = Parse(string.Empty);

            Assert.That(cfg, Is.Not.Null);
            Assert.That(cfg.Agents, Is.Empty);
        }

        [Test]
        public void Parse_InvalidJson_Throws()
        {
            Assert.That(() => Parse("{ not valid json"), Throws.InstanceOf<Newtonsoft.Json.JsonException>());
        }

        [Test]
        public void Parse_SingleAgent_BecomesOneAgent()
        {
            var cfg = Parse("{ \"agents\": [ { \"model\": \"m\", \"customBaseUrl\": \"http://localhost:1234\" } ] }");

            Assert.That(cfg.Agents, Has.Count.EqualTo(1));
            Assert.That(cfg.Agents[0].Model, Is.EqualTo("m"));
            // Raw parse leaves providerType unset; the "lmstudio" fallback is applied
            // later by ApplyDefaults() (before validation) and/or the top-level defaults.
            Assert.That(cfg.Agents[0].ProviderType, Is.Null);
            Assert.That(cfg.Agents[0].Enabled, Is.True);
        }

        // =========================================================================
        // ApplyDefaults (top-level provider settings)
        // =========================================================================

        [Test]
        public void ApplyDefaults_MissingProviderType_FallsBackToLmstudio()
        {
            var cfg = Parse("{ \"agents\": [ { \"model\": \"m\", \"customBaseUrl\": \"http://localhost:1234\" } ] }");

            cfg.ApplyDefaults();

            Assert.That(cfg.Agents[0].ProviderType, Is.EqualTo("lmstudio"));
        }

        [Test]
        public void ApplyDefaults_FillsProviderSettingsFromRoot()
        {
            var cfg = Parse(@"{
                ""providerType"": ""openai"",
                ""customBaseUrl"": ""https://api.openai.com/"",
                ""customApiKey"": ""root-key"",
                ""agents"": [
                    { ""id"": ""a"", ""model"": ""m"", ""customBaseUrl"": ""http://localhost:1234"" }
                ]
            }");

            cfg.ApplyDefaults();

            var agent = cfg.Agents[0];
            Assert.That(agent.ProviderType, Is.EqualTo("openai"));
            Assert.That(agent.CustomBaseUrl, Is.EqualTo("http://localhost:1234")); // agent value wins
            Assert.That(agent.CustomApiKey, Is.EqualTo("root-key"));
        }

        [Test]
        public void ApplyDefaults_EmptyAgentValues_ReplacedWithRoot()
        {
            var cfg = Parse(@"{
                ""providerType"": ""ollama"",
                ""customBaseUrl"": ""http://localhost:5678"",
                ""agents"": [
                    { ""id"": ""a"", ""model"": ""m"", ""providerType"": """", ""customBaseUrl"": ""   "" }
                ]
            }");

            cfg.ApplyDefaults();

            Assert.That(cfg.Agents[0].ProviderType, Is.EqualTo("ollama"));
            Assert.That(cfg.Agents[0].CustomBaseUrl, Is.EqualTo("http://localhost:5678"));
        }

        [Test]
        public void ApplyDefaults_AgentValues_OverrideRoot()
        {
            var cfg = Parse(@"{
                ""providerType"": ""openai"",
                ""customBaseUrl"": ""https://api.openai.com/"",
                ""customApiKey"": ""root-key"",
                ""agents"": [
                    {
                        ""id"": ""a"",
                        ""model"": ""m"",
                        ""providerType"": ""lmstudio"",
                        ""customBaseUrl"": ""http://localhost:1234"",
                        ""customApiKey"": ""agent-key""
                    }
                ]
            }");

            cfg.ApplyDefaults();

            var agent = cfg.Agents[0];
            Assert.That(agent.ProviderType, Is.EqualTo("lmstudio"));
            Assert.That(agent.CustomBaseUrl, Is.EqualTo("http://localhost:1234"));
            Assert.That(agent.CustomApiKey, Is.EqualTo("agent-key"));
        }

        [Test]
        public void Parse_FullConfig_ParsesAllFields()
        {
            string json = @"{
                ""agents"": [
                    {
                        ""id"": ""subagent"",
                        ""description"": ""Research agent"",
                        ""providerType"": ""deepseek"",
                        ""customBaseUrl"": ""https://api.deepseek.com/"",
                        ""customApiKey"": ""secret"",
                        ""model"": ""deepseek-chat"",
                        ""temperature"": 0.3,
                        ""timeoutSeconds"": 90,
                        ""maxRounds"": 5,
                        ""maxTokens"": 2048,
                        ""allowedTools"": [""get_solution_overview"", ""read_file_lines""]
                    }
                ]
            }";
            var cfg = Parse(json);
            var agent = cfg.Agents[0];

            Assert.That(agent.Id, Is.EqualTo("subagent"));
            Assert.That(agent.Description, Is.EqualTo("Research agent"));
            Assert.That(agent.ProviderType, Is.EqualTo("deepseek"));
            Assert.That(agent.CustomBaseUrl, Is.EqualTo("https://api.deepseek.com/"));
            Assert.That(agent.CustomApiKey, Is.EqualTo("secret"));
            Assert.That(agent.Model, Is.EqualTo("deepseek-chat"));
            Assert.That(agent.Temperature, Is.EqualTo(0.3));
            Assert.That(agent.TimeoutSeconds, Is.EqualTo(90));
            Assert.That(agent.MaxRounds, Is.EqualTo(5));
            Assert.That(agent.MaxTokens, Is.EqualTo(2048));
            Assert.That(agent.Enabled, Is.True);
            Assert.That(agent.AllowedTools, Is.EquivalentTo(new[] { "get_solution_overview", "read_file_lines" }));
        }

        [Test]
        public void Parse_CommentedToolEntries_AreIgnored()
        {
            string json = @"{
                ""agents"": [
                    {
                        ""allowedTools"": [
                            ""find_files"",
                            // ""build_solution"",
                            ""search_file_content""
                        ]
                    }
                ]
            }";
            var cfg = Parse(json);

            Assert.That(cfg.Agents[0].AllowedTools, Is.EquivalentTo(new[] { "find_files", "search_file_content" }));
        }

        [Test]
        public void Parse_SystemWithNewlines_ParsesMultiline()
        {
            string json = @"{ ""agents"": [ { ""system"": ""first line\nsecond line"", ""temperature"": 0.5 } ] }";
            var cfg = Parse(json);

            Assert.That(cfg.Agents[0].System, Is.Not.Null);
            string normalized = cfg.Agents[0].System.Replace("\r\n", "\n");
            Assert.That(normalized, Is.EqualTo("first line\nsecond line"));
            Assert.That(cfg.Agents[0].Temperature, Is.EqualTo(0.5));
        }

        [Test]
        public void Parse_EmptyAllowedTools_ReturnsEmptyList()
        {
            var cfg = Parse("{ \"agents\": [ { \"allowedTools\": [] } ] }");
            Assert.That(cfg.Agents[0].AllowedTools, Is.Empty);
        }

        [Test]
        public void Parse_CaseInsensitiveKeys_LastWins()
        {
            string json = @"{ ""agents"": [ { ""PROVIDERTYPE"": ""ollama"", ""TimeoutSeconds"": 30, ""timeoutseconds"": 45 } ] }";
            var cfg = Parse(json);

            Assert.That(cfg.Agents[0].ProviderType, Is.EqualTo("ollama"));
            Assert.That(cfg.Agents[0].TimeoutSeconds, Is.EqualTo(45));
        }

        [Test]
        public void Parse_EnabledKey_ParsesTrueFalseAndDefaultsToTrue()
        {
            string json = @"{
                ""agents"": [
                    { ""id"": ""a"", ""enabled"": true },
                    { ""id"": ""b"", ""enabled"": false },
                    { ""id"": ""c"" }
                ]
            }";
            var cfg = Parse(json);

            Assert.That(cfg.Agents[0].Enabled, Is.True);
            Assert.That(cfg.Agents[1].Enabled, Is.False);
            Assert.That(cfg.Agents[2].Enabled, Is.True);
        }

        [Test]
        public void Parse_EnabledKey_CaseInsensitive()
        {
            string json = @"{ ""agents"": [ { ""id"": ""a"", ""ENABLED"": false } ] }";
            var cfg = Parse(json);

            Assert.That(cfg.Agents[0].Enabled, Is.False);
        }

        // =========================================================================
        // Parser — multi-agent mode
        // =========================================================================

        [Test]
        public void Parse_MultiAgent_ParsesAllAgentsInOrder()
        {
            string json = @"{
                ""agents"": [
                    {
                        ""id"": ""researcher"",
                        ""description"": ""Research agent"",
                        ""providerType"": ""deepseek"",
                        ""customBaseUrl"": ""https://api.deepseek.com"",
                        ""model"": ""deepseek-chat"",
                        ""temperature"": 0.3,
                        ""enabled"": true,
                        ""allowedTools"": [""get_solution_overview"", ""find_files""]
                    },
                    {
                        ""id"": ""coder"",
                        ""providerType"": ""lmstudio"",
                        ""customBaseUrl"": ""http://localhost:1234"",
                        ""model"": ""qwen2.5-coder-7b-instruct"",
                        ""enabled"": false
                    }
                ]
            }";
            var cfg = Parse(json);

            Assert.That(cfg.Agents, Has.Count.EqualTo(2));

            var first = cfg.Agents[0];
            Assert.That(first.Id, Is.EqualTo("researcher"));
            Assert.That(first.Description, Is.EqualTo("Research agent"));
            Assert.That(first.ProviderType, Is.EqualTo("deepseek"));
            Assert.That(first.CustomBaseUrl, Is.EqualTo("https://api.deepseek.com"));
            Assert.That(first.Model, Is.EqualTo("deepseek-chat"));
            Assert.That(first.Temperature, Is.EqualTo(0.3));
            Assert.That(first.Enabled, Is.True);
            Assert.That(first.AllowedTools, Is.EquivalentTo(new[] { "get_solution_overview", "find_files" }));

            var second = cfg.Agents[1];
            Assert.That(second.Id, Is.EqualTo("coder"));
            Assert.That(second.ProviderType, Is.EqualTo("lmstudio"));
            Assert.That(second.Model, Is.EqualTo("qwen2.5-coder-7b-instruct"));
            Assert.That(second.Enabled, Is.False);
            Assert.That(second.AllowedTools, Is.Empty);
        }

        [Test]
        public void Parse_MultiAgent_DescriptionAndSystem_WithNewlines()
        {
            string json = @"{
                ""agents"": [
                    {
                        ""id"": ""researcher"",
                        ""description"": ""First line.\nSecond line."",
                        ""system"": ""You are a research agent."",
                        ""model"": ""m"",
                        ""customBaseUrl"": ""http://localhost:1234""
                    },
                    {
                        ""id"": ""solo"",
                        ""model"": ""m"",
                        ""customBaseUrl"": ""http://localhost:1234""
                    }
                ]
            }";
            var cfg = Parse(json);
            var agent = cfg.Agents[0];

            string desc = agent.Description.Replace("\r\n", "\n");
            Assert.That(desc, Is.EqualTo("First line.\nSecond line."));

            string sys = agent.System.Replace("\r\n", "\n");
            Assert.That(sys, Is.EqualTo("You are a research agent."));

            Assert.That(cfg.Agents[1].Id, Is.EqualTo("solo"));
        }

        [Test]
        public void Parse_UserConfig_FullFields_ParsesAllAgents()
        {
            // The structure the user asked for: { "agents": [ ... ] } with all optional fields populated.
            string json = @"{
                ""agents"": [
                    {
                        ""id"": ""research_agent"",
                        ""enabled"": true,
                        ""description"": ""Use this agent to search the codebase, find references, inspect types, and read files.\nIt is read-only and CANNOT modify code. Use it to gather context before writing code."",
                        ""system"": ""You are a read-only Code Explorer. Your task is to find specific code, analyze symbols, or read file contents based on the orchestrator's request.\nDo not attempt to write or edit code. Provide a concise summary of your findings, including exact file paths and line numbers."",
                        ""allowedTools"": [
                            ""get_solution_overview"",
                            ""list_directory"",
                            ""find_files"",
                            ""search_file_content"",
                            ""read_file_lines"",
                            ""get_active_document"",
                            ""get_symbol_info"",
                            ""get_symbol_info_js"",
                            ""inspect_type""
                        ],
                        ""providerType"": ""lmstudio"",
                        ""customBaseUrl"": ""http://localhost:1234"",
                        ""model"": ""qwen3.5-4b-instruct-revised"",
                        ""temperature"": 0.1,
                        ""timeoutSeconds"": 480,
                        ""maxRounds"": 99,
                        ""maxTokens"": 16384
                    },
                    {
                        ""id"": ""coder_agent"",
                        ""enabled"": false,
                        ""model"": ""m2"",
                        ""customBaseUrl"": ""http://localhost:5678""
                    }
                ]
            }";
            var cfg = Parse(json);

            Assert.That(cfg.Agents, Has.Count.EqualTo(2));

            var first = cfg.Agents[0];
            Assert.That(first.Id, Is.EqualTo("research_agent"));
            Assert.That(first.Enabled, Is.True);
            Assert.That(first.Description.Replace("\r\n", "\n"), Does.Contain("Use this agent to search the codebase"));
            Assert.That(first.System.Replace("\r\n", "\n"), Is.EqualTo(
                "You are a read-only Code Explorer. Your task is to find specific code, analyze symbols, or read file contents based on the orchestrator's request.\n" +
                "Do not attempt to write or edit code. Provide a concise summary of your findings, including exact file paths and line numbers."));
            Assert.That(first.AllowedTools, Is.EquivalentTo(new[]
            {
                "get_solution_overview", "list_directory", "find_files", "search_file_content",
                "read_file_lines", "get_active_document", "get_symbol_info", "get_symbol_info_js",
                "inspect_type"
            }));
            Assert.That(first.ProviderType, Is.EqualTo("lmstudio"));
            Assert.That(first.CustomBaseUrl, Is.EqualTo("http://localhost:1234"));
            Assert.That(first.Model, Is.EqualTo("qwen3.5-4b-instruct-revised"));
            Assert.That(first.Temperature, Is.EqualTo(0.1));
            Assert.That(first.TimeoutSeconds, Is.EqualTo(480));
            Assert.That(first.MaxRounds, Is.EqualTo(99));
            Assert.That(first.MaxTokens, Is.EqualTo(16384));

            Assert.That(cfg.Agents[1].Id, Is.EqualTo("coder_agent"));
            Assert.That(cfg.Agents[1].Enabled, Is.False);
        }

        [Test]
        public void Parse_ExactUserJson_ThreeAgents_ParsesAllFields()
        {
            // A top-level "agents" array of three agent objects.
            string json = @"{
                ""agents"": [
                    {
                        ""id"": ""research_agent"",
                        ""enabled"": true,
                        ""description"": ""Use this agent to search the codebase, find references, inspect types, and read files.\nIt is read-only and CANNOT modify code. Use it to gather context before writing code."",
                        ""system"": ""You are a read-only Code Explorer. Your task is to find specific code, analyze symbols, or read file contents based on the orchestrator's request.\nDo not attempt to write or edit code. Provide a concise summary of your findings, including exact file paths and line numbers."",
                        ""allowedTools"": [
                            ""get_solution_overview"",
                            ""list_directory"",
                            ""find_files"",
                            ""search_file_content"",
                            ""read_file_lines"",
                            ""get_active_document"",
                            ""get_symbol_info"",
                            ""get_symbol_info_js"",
                            ""inspect_type""
                        ],
                        ""providerType"": ""lmstudio"",
                        ""customBaseUrl"": ""http://localhost:1234"",
                        ""model"": ""qwen3.5-4b-instruct-revised"",
                        ""temperature"": 0.1,
                        ""timeoutSeconds"": 480,
                        ""maxRounds"": 99,
                        ""maxTokens"": 16384
                    },
                    {
                        ""id"": ""editor_agent"",
                        ""enabled"": true,
                        ""description"": ""Use this agent to create, edit, delete, or format files.\nProvide it with exact instructions, file paths, and the exact code changes required."",
                        ""system"": ""You are a precise Code Editor. Your job is to apply code modifications requested by the orchestrator.\nAlways read the target lines first if you are replacing content. Ensure changes are syntactically correct. Run format_document after making significant changes."",
                        ""allowedTools"": [
                            ""read_file_lines"",
                            ""create_file"",
                            ""delete_file"",
                            ""set_file_project_status"",
                            ""replace_file_content"",
                            ""replace_file_lines"",
                            ""insert_file_lines"",
                            ""format_document""
                        ],
                        ""providerType"": ""lmstudio"",
                        ""customBaseUrl"": ""http://localhost:1234"",
                        ""model"": ""qwen3.5-4b-instruct-revised"",
                        ""temperature"": 0.1,
                        ""timeoutSeconds"": 480,
                        ""maxRounds"": 99,
                        ""maxTokens"": 16384
                    },
                    {
                        ""id"": ""qa_agent"",
                        ""enabled"": true,
                        ""description"": ""Use this agent to build the solution or run tests. It will return compiler errors or test failures."",
                        ""system"": ""You are a QA Agent. Your task is to build the solution or run tests using the provided tools.\nAnalyze the build or test output. If there are errors, return a concise report of the failing files, line numbers, and exact error messages. If successful, simply report \""Success\""."",
                        ""timeoutSeconds"": 480,
                        ""allowedTools"": [
                            ""build_solution"",
                            ""run_tests"",
                            ""read_file_lines""
                        ],
                        ""providerType"": ""lmstudio"",
                        ""customBaseUrl"": ""http://localhost:1234"",
                        ""model"": ""qwen3.5-4b-instruct-revised"",
                        ""temperature"": 0.1,
                        ""maxRounds"": 99,
                        ""maxTokens"": 16384
                    }
                ]
            }";
            var cfg = Parse(json);

            Assert.That(cfg.Agents, Has.Count.EqualTo(3));
            Assert.That(cfg.Validate(), Is.Empty);

            var research = cfg.Agents[0];
            Assert.That(research.Id, Is.EqualTo("research_agent"));
            Assert.That(research.Enabled, Is.True);
            Assert.That(research.Description.Replace("\r\n", "\n"), Does.StartWith("Use this agent to search the codebase"));
            Assert.That(research.System.Replace("\r\n", "\n"), Does.StartWith("You are a read-only Code Explorer"));
            Assert.That(research.AllowedTools, Is.EquivalentTo(new[]
            {
                "get_solution_overview", "list_directory", "find_files", "search_file_content",
                "read_file_lines", "get_active_document", "get_symbol_info", "get_symbol_info_js",
                "inspect_type"
            }));
            Assert.That(research.ProviderType, Is.EqualTo("lmstudio"));
            Assert.That(research.CustomBaseUrl, Is.EqualTo("http://localhost:1234"));
            Assert.That(research.Model, Is.EqualTo("qwen3.5-4b-instruct-revised"));
            Assert.That(research.Temperature, Is.EqualTo(0.1));
            Assert.That(research.TimeoutSeconds, Is.EqualTo(480));
            Assert.That(research.MaxRounds, Is.EqualTo(99));
            Assert.That(research.MaxTokens, Is.EqualTo(16384));

            var editor = cfg.Agents[1];
            Assert.That(editor.Id, Is.EqualTo("editor_agent"));
            Assert.That(editor.Enabled, Is.True);
            Assert.That(editor.AllowedTools, Is.EquivalentTo(new[]
            {
                "read_file_lines", "create_file", "delete_file", "set_file_project_status",
                "replace_file_content", "replace_file_lines", "insert_file_lines", "format_document"
            }));

            var qa = cfg.Agents[2];
            Assert.That(qa.Id, Is.EqualTo("qa_agent"));
            Assert.That(qa.Enabled, Is.True);
            Assert.That(qa.AllowedTools, Is.EquivalentTo(new[] { "build_solution", "run_tests", "read_file_lines" }));
            Assert.That(qa.TimeoutSeconds, Is.EqualTo(480));
        }

        // BOM is handled by the file system: IFileSystem.ReadAllTextAsync strips it (like
        // File.ReadAllText), so no BOM test exists at the parser level. See
        // GetAsync_Utf8BomFile_StillReads (manager) and DefaultFileSystemTests.

        // =========================================================================
        // Validation
        // =========================================================================

        [Test]
        public void Validate_MissingRequiredFields_ReportsErrors()
        {
            var cfg = Parse("{ \"agents\": [ { } ] }");

            var errors = cfg.Validate();

            Assert.That(errors, Is.Not.Empty);
            Assert.That(errors.Any(e => e.Contains("'id'")), Is.True);
            Assert.That(errors.Any(e => e.Contains("'description'")), Is.True);
            Assert.That(errors.Any(e => e.Contains("model")), Is.True);
            Assert.That(errors.Any(e => e.Contains("customBaseUrl")), Is.True);
        }

        [Test]
        public void Validate_MissingName_ReportsError()
        {
            var cfg = Parse("{ \"agents\": [ { \"description\": \"d\", \"model\": \"m\", \"customBaseUrl\": \"http://a\" } ] }");

            var errors = cfg.Validate();

            Assert.That(errors.Any(e => e.Contains("'id'")), Is.True);
        }

        [Test]
        public void Validate_MissingDescription_ReportsError()
        {
            var cfg = Parse("{ \"agents\": [ { \"id\": \"n\", \"model\": \"m\", \"customBaseUrl\": \"http://a\" } ] }");

            var errors = cfg.Validate();

            Assert.That(errors.Any(e => e.Contains("'description'")), Is.True);
        }

        [Test]
        public void Validate_InvalidBaseUrl_ReportsError()
        {
            var cfg = Parse("{ \"agents\": [ { \"model\": \"m\", \"customBaseUrl\": \"not a url\" } ] }");

            var errors = cfg.Validate();

            Assert.That(errors.Any(e => e.Contains("customBaseUrl")), Is.True);
        }

        [Test]
        public void Validate_TemperatureOutOfRange_ReportsError()
        {
            var cfg = Parse("{ \"agents\": [ { \"model\": \"m\", \"temperature\": 3.5 } ] }");

            var errors = cfg.Validate();

            Assert.That(errors.Any(e => e.Contains("temperature")), Is.True);
        }

        [Test]
        public void Validate_MaxRoundsZero_ReportsError()
        {
            var cfg = Parse("{ \"agents\": [ { \"model\": \"m\", \"maxRounds\": 0 } ] }");

            var errors = cfg.Validate();

            Assert.That(errors.Any(e => e.Contains("maxRounds")), Is.True);
        }

        [Test]
        public void Validate_ValidConfig_ReturnsNoErrors()
        {
            var cfg = Parse("{ \"agents\": [ { \"id\": \"n\", \"description\": \"d\", \"model\": \"m\", \"customBaseUrl\": \"http://localhost:1234\", \"temperature\": 0.2 } ] }");

            Assert.That(cfg.Validate(), Is.Empty);
        }

        [Test]
        public void Validate_DuplicateNames_ReportsError()
        {
            string json = @"{
                ""agents"": [
                    { ""id"": ""researcher"", ""description"": ""d1"", ""model"": ""m"", ""customBaseUrl"": ""http://a"" },
                    { ""id"": ""Researcher"", ""description"": ""d2"", ""model"": ""m2"", ""customBaseUrl"": ""http://b"" }
                ]
            }";
            var cfg = Parse(json);

            var errors = cfg.Validate();

            Assert.That(errors.Any(e => e.Contains("duplicate agent id")), Is.True);
        }

        [Test]
        public void Validate_ErrorsArePrefixedWithAgentIndex()
        {
            string json = @"{
                ""agents"": [
                    { ""id"": ""a"", ""description"": ""d1"", ""model"": ""m"", ""customBaseUrl"": ""http://a"" },
                    { ""id"": ""b"", ""description"": ""d2"" }
                ]
            }";
            var cfg = Parse(json);

            var errors = cfg.Validate();

            Assert.That(errors.Any(e => e.StartsWith("agent[1]") && e.Contains("model")), Is.True);
        }

        // =========================================================================
        // Writer
        // =========================================================================

        [Test]
        public void Writer_RoundTripsConfig()
        {
            var cfg = new SubAgentsConfig();
            cfg.Agents.Add(new SubAgentDefinition
            {
                Id = "researcher",
                Description = "Research agent",
                ProviderType = "deepseek",
                CustomBaseUrl = "https://api.deepseek.com",
                CustomApiKey = "secret",
                Model = "deepseek-chat",
                System = "You are a researcher.",
                Temperature = 0.3,
                TimeoutSeconds = 90,
                MaxRounds = 5,
                MaxTokens = 2048,
                Enabled = true,
                AllowedTools = new System.Collections.Generic.List<string> { "get_solution_overview", "find_files" }
            });
            cfg.Agents.Add(new SubAgentDefinition
            {
                Id = "coder",
                ProviderType = "lmstudio",
                CustomBaseUrl = "http://localhost:1234",
                Model = "qwen2.5-coder-7b-instruct",
                Enabled = false
            });

            var json = cfg.ToJsonIndented();
            var parsed = json.FromJson<SubAgentsConfig>();

            Assert.That(parsed.Agents, Has.Count.EqualTo(2));
            Assert.That(parsed.Agents[0].Id, Is.EqualTo("researcher"));
            Assert.That(parsed.Agents[0].Description, Is.EqualTo("Research agent"));
            Assert.That(parsed.Agents[0].ProviderType, Is.EqualTo("deepseek"));
            Assert.That(parsed.Agents[0].CustomBaseUrl, Is.EqualTo("https://api.deepseek.com"));
            Assert.That(parsed.Agents[0].CustomApiKey, Is.EqualTo("secret"));
            Assert.That(parsed.Agents[0].Model, Is.EqualTo("deepseek-chat"));
            Assert.That(parsed.Agents[0].System, Is.EqualTo("You are a researcher."));
            Assert.That(parsed.Agents[0].Temperature, Is.EqualTo(0.3));
            Assert.That(parsed.Agents[0].TimeoutSeconds, Is.EqualTo(90));
            Assert.That(parsed.Agents[0].MaxRounds, Is.EqualTo(5));
            Assert.That(parsed.Agents[0].MaxTokens, Is.EqualTo(2048));
            Assert.That(parsed.Agents[0].Enabled, Is.True);
            Assert.That(parsed.Agents[0].AllowedTools, Is.EquivalentTo(new[] { "get_solution_overview", "find_files" }));

            Assert.That(parsed.Agents[1].Id, Is.EqualTo("coder"));
            Assert.That(parsed.Agents[1].Enabled, Is.False);
        }

        [Test]
        public void Writer_WritesEnabledFalseExplicitly()
        {
            var cfg = new SubAgentsConfig();
            cfg.Agents.Add(new SubAgentDefinition
            {
                Id = "off",
                Model = "m",
                CustomBaseUrl = "http://x",
                Enabled = false
            });

            var json = cfg.ToJsonIndented();

            Assert.That(json.TrimStart(), Does.StartWith("{"));
            Assert.That(json, Does.Contain("\"agents\""));
            Assert.That(json, Does.Contain("\"id\": \"off\""));
            Assert.That(json, Does.Contain("\"enabled\": false"));
        }

        // =========================================================================
        // Manager
        // =========================================================================

        private static (SubAgentsConfigManager manager, InMemoryFileSystem fs, string path) CreateManager()
        {
            return CreateManager(new List<ToolDefinition>());
        }

        private static (SubAgentsConfigManager manager, InMemoryFileSystem fs, string path) CreateManager(
            IReadOnlyList<ToolDefinition> builtInTools)
        {
            var fs = new InMemoryFileSystem();
            var settings = new Mock<ISettingsManager>();
            settings.Setup(s => s.LocalAppDataFolder).Returns("LMLocalChat");

            var builtInToolsMock = new Mock<IBuiltInVsToolProvider>();
            builtInToolsMock.Setup(b => b.GetAllToolDefinitionsUnfiltered())
                .Returns(builtInTools);

            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LMLocalChat",
                "subagents.json");

            return (new SubAgentsConfigManager(fs, settings.Object, builtInToolsMock.Object), fs, path);
        }

        private static ToolDefinition Tool(string name)
        {
            return new ToolDefinition { Name = name };
        }

        [Test]
        public void ManagerValidate_NullConfig_ReturnsError()
        {
            var (manager, _, _) = CreateManager();

            var errors = manager.Validate(null);

            Assert.That(errors.Any(e => e.Contains("required")), Is.True);
        }

        [Test]
        public void ManagerValidate_MissingRequiredFields_ReportsErrors()
        {
            var (manager, _, _) = CreateManager();

            var config = new SubAgentsConfig();
            config.Agents.Add(new SubAgentDefinition { Id = "a" });

            var errors = manager.Validate(config);

            Assert.That(errors.Any(e => e.Contains("'description'")), Is.True);
            Assert.That(errors.Any(e => e.Contains("model")), Is.True);
            Assert.That(errors.Any(e => e.Contains("customBaseUrl")), Is.True);
        }

        [Test]
        public void ManagerValidate_DuplicateAgentName_ReportsError()
        {
            var (manager, _, _) = CreateManager(new List<ToolDefinition> { Tool("find_files") });

            var config = new SubAgentsConfig();
            config.Agents.Add(new SubAgentDefinition
            {
                Id = "researcher",
                Description = "A",
                CustomBaseUrl = "http://x",
                Model = "m",
                Enabled = true
            });
            config.Agents.Add(new SubAgentDefinition
            {
                Id = "Researcher",
                Description = "B",
                CustomBaseUrl = "http://y",
                Model = "m",
                Enabled = true
            });

            var errors = manager.Validate(config);

            Assert.That(errors.Any(e => e.Contains("not unique")), Is.True);
        }

        [Test]
        public void ManagerValidate_AgentNameCollidesWithBuiltInTool_ReportsError()
        {
            var (manager, _, _) = CreateManager(new List<ToolDefinition> { Tool("find_files") });

            var config = new SubAgentsConfig();
            config.Agents.Add(new SubAgentDefinition
            {
                Id = "find_files",
                Description = "A",
                CustomBaseUrl = "http://x",
                Model = "m",
                Enabled = true,
                AllowedTools = new List<string>()
            });

            var errors = manager.Validate(config);

            Assert.That(errors.Any(e => e.Contains("collides with a built-in tool")), Is.True);
        }

        [Test]
        public void ManagerValidate_AllowedToolsUnknownTool_ReportsError()
        {
            var (manager, _, _) = CreateManager(new List<ToolDefinition> { Tool("read_file_lines") });

            var config = new SubAgentsConfig();
            config.Agents.Add(new SubAgentDefinition
            {
                Id = "researcher",
                Description = "A",
                CustomBaseUrl = "http://x",
                Model = "m",
                Enabled = true,
                AllowedTools = new List<string> { "ghost_tool" }
            });

            var errors = manager.Validate(config);

            Assert.That(errors.Any(e => e.Contains("unknown tool 'ghost_tool'")), Is.True);
        }

        [Test]
        public void ManagerValidate_AllowedToolsReferencesAnotherSubAgent_ReportsError()
        {
            var (manager, _, _) = CreateManager(new List<ToolDefinition> { Tool("read_file_lines") });

            var config = new SubAgentsConfig();
            config.Agents.Add(new SubAgentDefinition
            {
                Id = "researcher",
                Description = "A",
                CustomBaseUrl = "http://x",
                Model = "m",
                Enabled = true,
                AllowedTools = new List<string> { "coder" }
            });
            config.Agents.Add(new SubAgentDefinition
            {
                Id = "coder",
                Description = "B",
                CustomBaseUrl = "http://y",
                Model = "m",
                Enabled = true
            });

            var errors = manager.Validate(config);

            Assert.That(errors.Any(e => e.Contains("references another SubAgent 'coder'")), Is.True);
        }

        [Test]
        public void ManagerValidate_ValidConfig_ReturnsNoErrors()
        {
            var (manager, _, _) = CreateManager(new List<ToolDefinition> { Tool("read_file_lines"), Tool("find_files") });

            var config = new SubAgentsConfig();
            config.Agents.Add(new SubAgentDefinition
            {
                Id = "researcher",
                Description = "A",
                CustomBaseUrl = "http://x",
                Model = "m",
                Enabled = true,
                AllowedTools = new List<string> { "read_file_lines", "find_files" }
            });

            Assert.That(manager.Validate(config), Is.Empty);
        }

        // =========================================================================
        // Manager — UpdateEnabledFlagsAsync
        // =========================================================================

        private static void WriteConfig(InMemoryFileSystem fs, string path, string json)
        {
            fs.WriteAllBytesAsync(path, System.Text.Encoding.UTF8.GetBytes(json)).GetAwaiter().GetResult();
        }

        private static string TwoAgentsJson()
        {
            return @"{ ""agents"": [
                { ""id"": ""researcher"", ""description"": ""A"", ""providerType"": ""deepseek"", ""customBaseUrl"": ""https://api.deepseek.com"", ""model"": ""deepseek-chat"", ""enabled"": true },
                { ""id"": ""coder"", ""description"": ""B"", ""customBaseUrl"": ""http://localhost:1234"", ""model"": ""qwen2.5-coder-7b-instruct"", ""enabled"": false }
            ] }";
        }

        [Test]
        public async Task UpdateEnabledFlagsAsync_MatchesByName_AndPersists()
        {
            var (manager, fs, path) = CreateManager();
            WriteConfig(fs, path, TwoAgentsJson());

            var errors = await manager.UpdateEnabledFlagsAsync(new List<SubAgentEnabledFlag>
            {
                new SubAgentEnabledFlag { Id = "researcher", Enabled = false },
                new SubAgentEnabledFlag { Id = "coder", Enabled = true }
            });

            Assert.That(errors, Is.Empty);

            var cfg = await manager.GetAsync();
            Assert.That(cfg.Agents[0].Id, Is.EqualTo("researcher"));
            Assert.That(cfg.Agents[0].Enabled, Is.False);
            Assert.That(cfg.Agents[0].Model, Is.EqualTo("deepseek-chat"));
            Assert.That(cfg.Agents[1].Enabled, Is.True);
        }

        [Test]
        public async Task UpdateEnabledFlagsAsync_UnknownName_FallsBackToIndex()
        {
            var (manager, fs, path) = CreateManager();
            WriteConfig(fs, path, TwoAgentsJson());

            var errors = await manager.UpdateEnabledFlagsAsync(new List<SubAgentEnabledFlag>
            {
                new SubAgentEnabledFlag { Index = 0, Enabled = false }
            });

            Assert.That(errors, Is.Empty);

            var cfg = await manager.GetAsync();
            Assert.That(cfg.Agents[0].Enabled, Is.False);
            Assert.That(cfg.Agents[1].Enabled, Is.False);
        }

        [Test]
        public async Task UpdateEnabledFlagsAsync_UnknownNameAndIndex_IgnoresEntry()
        {
            var (manager, fs, path) = CreateManager();
            WriteConfig(fs, path, TwoAgentsJson());

            var errors = await manager.UpdateEnabledFlagsAsync(new List<SubAgentEnabledFlag>
            {
                new SubAgentEnabledFlag { Id = "ghost", Enabled = true }
            });

            Assert.That(errors, Is.Empty);

            var cfg = await manager.GetAsync();
            Assert.That(cfg.Agents[0].Enabled, Is.True);
            Assert.That(cfg.Agents[1].Enabled, Is.False);
        }

        [Test]
        public async Task UpdateEnabledFlagsAsync_ValidationFails_ReturnsErrorsAndDoesNotSave()
        {
            var (manager, fs, path) = CreateManager(new List<ToolDefinition> { Tool("read_file_lines") });
            WriteConfig(fs, path,
                "{ \"agents\": [ { \"id\": \"researcher\", \"description\": \"A\", \"customBaseUrl\": \"http://x\", \"model\": \"m\", \"enabled\": true, \"allowedTools\": [ \"ghost_tool\" ] } ] }");

            var errors = await manager.UpdateEnabledFlagsAsync(new List<SubAgentEnabledFlag>
            {
                new SubAgentEnabledFlag { Id = "researcher", Enabled = false }
            });

            Assert.That(errors.Any(e => e.Contains("unknown tool 'ghost_tool'")), Is.True);
            Assert.That(manager.TryGetSnapshot().Agents, Is.Empty);
        }

        [Test]
        public async Task GetAsync_MissingFile_ReturnsEmptyConfigNotError()
        {
            var (manager, _, _) = CreateManager();

            var cfg = await manager.GetAsync();

            Assert.That(cfg, Is.Not.Null);
            Assert.That(cfg.Agents, Is.Empty);
            Assert.That(manager.LastErrors, Is.Empty);
        }

        [Test]
        public async Task GetAsync_InvalidFields_ReturnsConfigWithErrors()
        {
            var (manager, fs, path) = CreateManager();
            fs.WriteAllBytesAsync(path, System.Text.Encoding.UTF8.GetBytes("{ \"agents\": [ { \"model\": \"\" } ] }")).GetAwaiter().GetResult();

            var cfg = await manager.GetAsync();

            Assert.That(cfg, Is.Not.Null);
            Assert.That(manager.LastErrors, Is.Not.Empty);
        }

        [Test]
        public async Task GetAsync_ValidConfig_ReturnsConfig()
        {
            var (manager, fs, path) = CreateManager();
            string json = @"{
                ""agents"": [
                    {
                        ""id"": ""subagent"",
                        ""description"": ""Research agent"",
                        ""providerType"": ""deepseek"",
                        ""customBaseUrl"": ""https://api.deepseek.com"",
                        ""customApiKey"": ""secret"",
                        ""model"": ""deepseek-chat"",
                        ""temperature"": 0.3,
                        ""allowedTools"": [""get_solution_overview""]
                    }
                ]
            }";
            fs.WriteAllBytesAsync(path, System.Text.Encoding.UTF8.GetBytes(json)).GetAwaiter().GetResult();

            var cfg = await manager.GetAsync();

            Assert.That(cfg, Is.Not.Null);
            Assert.That(cfg.Agents, Has.Count.EqualTo(1));
            var agent = cfg.Agents[0];
            Assert.That(agent.Id, Is.EqualTo("subagent"));
            Assert.That(agent.ProviderType, Is.EqualTo("deepseek"));
            Assert.That(agent.Model, Is.EqualTo("deepseek-chat"));
            Assert.That(agent.Temperature, Is.EqualTo(0.3));
            Assert.That(agent.AllowedTools, Is.EquivalentTo(new[] { "get_solution_overview" }));
        }

        [Test]
        public async Task GetAsync_AppliesTopLevelDefaultsToAgents()
        {
            var (manager, fs, path) = CreateManager();
            string json = @"{
                ""providerType"": ""openai"",
                ""customBaseUrl"": ""https://api.openai.com/"",
                ""customApiKey"": ""root-key"",
                ""agents"": [
                    {
                        ""id"": ""a"",
                        ""description"": ""d"",
                        ""model"": ""m""
                    }
                ]
            }";
            fs.WriteAllBytesAsync(path, System.Text.Encoding.UTF8.GetBytes(json)).GetAwaiter().GetResult();

            var cfg = await manager.GetAsync();

            Assert.That(manager.LastErrors, Is.Empty);
            Assert.That(cfg.Agents, Has.Count.EqualTo(1));
            var agent = cfg.Agents[0];
            Assert.That(agent.ProviderType, Is.EqualTo("openai"));
            Assert.That(agent.CustomBaseUrl, Is.EqualTo("https://api.openai.com/"));
            Assert.That(agent.CustomApiKey, Is.EqualTo("root-key"));
        }

        [Test]
        public async Task GetAsync_AgentWithoutBaseUrl_UsesRootAndValidates()
        {
            var (manager, fs, path) = CreateManager();
            string json = @"{
                ""customBaseUrl"": ""http://localhost:1234"",
                ""agents"": [
                    {
                        ""id"": ""a"",
                        ""description"": ""d"",
                        ""model"": ""m"",
                        ""providerType"": ""ollama""
                    }
                ]
            }";
            fs.WriteAllBytesAsync(path, System.Text.Encoding.UTF8.GetBytes(json)).GetAwaiter().GetResult();

            var cfg = await manager.GetAsync();

            Assert.That(manager.LastErrors, Is.Empty);
            Assert.That(cfg.Agents[0].ProviderType, Is.EqualTo("ollama"));
            Assert.That(cfg.Agents[0].CustomBaseUrl, Is.EqualTo("http://localhost:1234"));
        }

        [Test]
        public async Task GetAsync_Utf8BomFile_StillReads()
        {
            // Notepad-style "UTF-8 with signature": the file starts with EF BB BF. Neither the
            // default nor the in-memory file system strips it, so the manager must.
            var (manager, fs, path) = CreateManager();
            byte[] bom = { 0xEF, 0xBB, 0xBF };
            byte[] json = System.Text.Encoding.UTF8.GetBytes("{ \"agents\": [ { \"id\": \"a\", \"description\": \"d\", \"model\": \"m\", \"customBaseUrl\": \"http://a\" } ] }");
            var withBom = new byte[bom.Length + json.Length];
            System.Buffer.BlockCopy(bom, 0, withBom, 0, bom.Length);
            System.Buffer.BlockCopy(json, 0, withBom, bom.Length, json.Length);
            fs.WriteAllBytesAsync(path, withBom).GetAwaiter().GetResult();

            var cfg = await manager.GetAsync();

            Assert.That(cfg.Agents, Has.Count.EqualTo(1));
            Assert.That(cfg.Agents[0].Id, Is.EqualTo("a"));
        }

        [Test]
        public async Task SaveAsync_WritesJson_AndReadsBack()
        {
            var (manager, fs, path) = CreateManager();

            var config = new SubAgentsConfig();
            config.Agents.Add(new SubAgentDefinition
            {
                Id = "researcher",
                Description = "Research agent",
                ProviderType = "lmstudio",
                CustomBaseUrl = "http://localhost:1234",
                Model = "qwen2.5-coder-7b-instruct",
                Enabled = true
            });

            await manager.SaveAsync(config);

            var written = fs.ReadAllText(path);
            Assert.That(written, Does.Contain("\"id\": \"researcher\""));
            Assert.That(written, Does.Contain("\"enabled\": true"));

            var loaded = await manager.GetAsync();
            Assert.That(loaded.Agents, Has.Count.EqualTo(1));
            Assert.That(loaded.Agents[0].Id, Is.EqualTo("researcher"));
            Assert.That(loaded.Agents[0].Model, Is.EqualTo("qwen2.5-coder-7b-instruct"));
        }

        [Test]
        public void SaveAsync_NullConfig_Throws()
        {
            var (manager, _, _) = CreateManager();

            Assert.ThrowsAsync<ArgumentNullException>(async () => await manager.SaveAsync(null));
        }
    }
}
