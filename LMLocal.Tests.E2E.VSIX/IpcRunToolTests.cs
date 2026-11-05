using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace LMLocal.Tests.E2E.VSIX
{
    /// <summary>
    /// End-to-end coverage for the remaining built-in tools reachable through the
    /// 'RunTool|&lt;tool_name&gt;|&lt;arg1&gt;|&lt;arg2&gt;' IPC pass-through.
    /// Build/run tests (build_solution, run_tests) are intentionally excluded —
    /// they need a full build/runner and cannot be driven reliably from the harness.
    /// </summary>
    [TestClass]
    public class IpcRunToolTests
    {
        private const string PipeName = "LMLocal.Ipc";

        /// <summary>Scratch dir inside the solution so write tools pass the inside-solution guard.</summary>
        private static string ScratchDir =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "..", "..", "..", "..", "LMLocal.Tests.E2E.VSIX", "Scratch");

        private static string ScratchPath(string name) => Path.Combine(ScratchDir, name);

        private void EnsureScratchExists() => Directory.CreateDirectory(ScratchDir);

        private static void CleanupFiles(params string[] paths)
        {
            foreach (var p in paths)
            {
                try { if (File.Exists(p)) File.Delete(p); } catch { }
            }
        }

        private static string UriEnc(string value) => Uri.EscapeDataString(value ?? string.Empty);

        private async Task<string> SendAsync(IpcClient client, string command, CancellationToken ct)
        {
            return await client.SendCommandAsync(command, TimeSpan.FromMinutes(2), ct);
        }

        [TestMethod]
        public async Task RunTool_CreateFile_ThenDelete_ReturnsJsonAsync()
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3)))
            {
                EnsureScratchExists();
                var path = ScratchPath("e2e_probe.txt");
                CleanupFiles(path);
                var vs = await VsLauncher.StartExperimentalInstanceAsync(cts.Token);
                try
                {
                    var solutionPath = GetSolutionPath();
                    var client = await IpcClient.ConnectAsync(PipeName, TimeSpan.FromMinutes(1), cts.Token);
                    using (client)
                    {
                        Assert.AreEqual("OK", await SendAsync(client, $"OpenSolution|{solutionPath}", cts.Token));

                        // create_file
                        var rel = "LMLocal.Tests.E2E.VSIX\\Scratch\\e2e_probe.txt";
                        var content = "line1\nline2\nline3";
                        var resp = await SendAsync(client, $"RunTool|create_file|{UriEnc(rel)}|{UriEnc(content)}", cts.Token);
                        var obj = JObject.Parse(resp);
                        Assert.IsTrue((bool)obj["success"], "create_file should succeed: " + resp);
                        Assert.IsTrue(File.Exists(path), "created file should exist on disk");

                        // read back: each returned entry has explicit line_number and text fields.
                        resp = await SendAsync(client, $"RunTool|read_file_lines|{UriEnc(rel)}|1|10", cts.Token);
                        obj = JObject.Parse(resp);
                        var lines = obj["content"] as JArray;
                        Assert.IsNotNull(lines, "'content' should be an array");
                        Assert.IsGreaterThanOrEqualTo(3, lines.Count, "expected at least the 3 written lines, got: " + lines.Count);

                        var entries = lines.OfType<JObject>().ToList();
                        Assert.IsGreaterThanOrEqualTo(3, entries.Count, "expected at least 3 line objects");
                        Assert.AreEqual(1, entries[0]["line_number"].Value<int>());
                        Assert.AreEqual("line1", entries[0]["text"].Value<string>());
                        Assert.AreEqual(2, entries[1]["line_number"].Value<int>());
                        Assert.AreEqual("line2", entries[1]["text"].Value<string>());
                        Assert.AreEqual(3, entries[2]["line_number"].Value<int>());
                        Assert.AreEqual("line3", entries[2]["text"].Value<string>());

                        // delete_file
                        resp = await SendAsync(client, $"RunTool|delete_file|{UriEnc(rel)}", cts.Token);
                        obj = JObject.Parse(resp);
                        Assert.IsTrue((bool)obj["success"], "delete_file should succeed: " + resp);
                        Assert.IsFalse(File.Exists(path), "file should be removed from disk");
                    }
                }
                finally
                {
                    CleanupFiles(path);
                    TryKill(vs);
                }
            }
        }

        [TestMethod]
        public async Task RunTool_ReplaceFileContent_ThenDelete_ReturnsJsonAsync()
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3)))
            {
                EnsureScratchExists();
                var path = ScratchPath("e2e_replace.txt");
                CleanupFiles(path);
                var vs = await VsLauncher.StartExperimentalInstanceAsync(cts.Token);
                try
                {
                    var solutionPath = GetSolutionPath();
                    var client = await IpcClient.ConnectAsync(PipeName, TimeSpan.FromMinutes(1), cts.Token);
                    using (client)
                    {
                        Assert.AreEqual("OK", await SendAsync(client, $"OpenSolution|{solutionPath}", cts.Token));
                        var rel = "LMLocal.Tests.E2E.VSIX\\Scratch\\e2e_replace.txt";

                        await SendAsync(client, $"RunTool|create_file|{UriEnc(rel)}|{UriEnc("OLD_A\nOLD_B\nOLD_C")}", cts.Token);

                        var resp = await SendAsync(client, $"RunTool|replace_file_content|{UriEnc(rel)}|{UriEnc("NEW_CONTENT")}", cts.Token);
                        var obj = JObject.Parse(resp);
                        Assert.IsTrue((bool)obj["success"], "replace_file_content should succeed: " + resp);
                        Assert.AreEqual("NEW_CONTENT", File.ReadAllText(path).Trim(), "content should be fully replaced");

                        await SendAsync(client, $"RunTool|delete_file|{UriEnc(rel)}", cts.Token);
                    }
                }
                finally
                {
                    CleanupFiles(path);
                    TryKill(vs);
                }
            }
        }

        [TestMethod]
        public async Task RunTool_InsertFileLines_ThenDelete_ReturnsJsonAsync()
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3)))
            {
                EnsureScratchExists();
                var path = ScratchPath("e2e_insert.txt");
                CleanupFiles(path);
                var vs = await VsLauncher.StartExperimentalInstanceAsync(cts.Token);
                try
                {
                    var solutionPath = GetSolutionPath();
                    var client = await IpcClient.ConnectAsync(PipeName, TimeSpan.FromMinutes(1), cts.Token);
                    using (client)
                    {
                        Assert.AreEqual("OK", await SendAsync(client, $"OpenSolution|{solutionPath}", cts.Token));
                        var rel = "LMLocal.Tests.E2E.VSIX\\Scratch\\e2e_insert.txt";

                        await SendAsync(client, $"RunTool|create_file|{UriEnc(rel)}|{UriEnc("A\nB\nC")}", cts.Token);

                        // insert after line 1 (0-based semantics in tool: position=1 means after line 1)
                        var resp = await SendAsync(client, $"RunTool|insert_file_lines|{UriEnc(rel)}|1|{UriEnc("INSERTED")}", cts.Token);
                        var obj = JObject.Parse(resp);
                        Assert.IsTrue((bool)obj["success"], "insert_file_lines should succeed: " + resp);
                        Assert.IsGreaterThan(0, (int)obj["lines_inserted"], "lines_inserted should be positive");

                        var full = File.ReadAllText(path);
                        Assert.Contains("INSERTED", full, "inserted line should be present");

                        await SendAsync(client, $"RunTool|delete_file|{UriEnc(rel)}", cts.Token);
                    }
                }
                finally
                {
                    CleanupFiles(path);
                    TryKill(vs);
                }
            }
        }

        [TestMethod]
        public async Task RunTool_ReplaceFileLines_ThenDelete_ReturnsJsonAsync()
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3)))
            {
                EnsureScratchExists();
                var path = ScratchPath("e2e_rpl_l.txt");
                CleanupFiles(path);
                var vs = await VsLauncher.StartExperimentalInstanceAsync(cts.Token);
                try
                {
                    var solutionPath = GetSolutionPath();
                    var client = await IpcClient.ConnectAsync(PipeName, TimeSpan.FromMinutes(1), cts.Token);
                    using (client)
                    {
                        Assert.AreEqual("OK", await SendAsync(client, $"OpenSolution|{solutionPath}", cts.Token));
                        var rel = "LMLocal.Tests.E2E.VSIX\\Scratch\\e2e_rpl_l.txt";

                        await SendAsync(client, $"RunTool|create_file|{UriEnc(rel)}|{UriEnc("aaa\nbbb\nccc")}", cts.Token);

                        // replace lines 2..2 ("bbb") with "REPLACED"
                        var resp = await SendAsync(client, $"RunTool|replace_file_lines|{UriEnc(rel)}|2|{UriEnc("bbb")}|{UriEnc("REPLACED")}", cts.Token);
                        var obj = JObject.Parse(resp);
                        Assert.IsTrue((bool)obj["success"], "replace_file_lines should succeed: " + resp);

                        var full = File.ReadAllText(path);
                        Assert.Contains("REPLACED", full, "replacement should be present");
                        Assert.DoesNotContain("bbb", full, "old line should be gone");

                        await SendAsync(client, $"RunTool|delete_file|{UriEnc(rel)}", cts.Token);
                    }
                }
                finally
                {
                    CleanupFiles(path);
                    TryKill(vs);
                }
            }
        }

        /// <summary>
        /// read → replace → read must be symmetric (1:1): a verbatim no-op replace rewrites the file
        /// without changing a single byte, and a second read returns exactly the same lines.
        /// Scope: raw file I/O through the real tool pipeline. The Scratch file is not opened as a
        /// VS document, so editor-side effects (format_document, get_changes_diff, document buffer)
        /// are deliberately out of scope; the encoding/EOL matrix lives in the unit fixture
        /// FileToolsReadReplaceRoundTripTests.
        /// </summary>
        [TestMethod]
        public async Task RunTool_ReadReplaceRead_RoundTripsOneToOneAsync()
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3)))
            {
                EnsureScratchExists();
                var path = ScratchPath("e2e_roundtrip.txt");
                CleanupFiles(path);
                var vs = await VsLauncher.StartExperimentalInstanceAsync(cts.Token);
                try
                {
                    var solutionPath = GetSolutionPath();
                    var client = await IpcClient.ConnectAsync(PipeName, TimeSpan.FromMinutes(1), cts.Token);
                    using (client)
                    {
                        Assert.AreEqual("OK", await SendAsync(client, $"OpenSolution|{solutionPath}", cts.Token));
                        var rel = "LMLocal.Tests.E2E.VSIX\\Scratch\\e2e_roundtrip.txt";

                        // Cyrillic and typographic characters are intentional test payload (the real
                        // incident involved a README with Russian text and '->' / em-dash characters);
                        // it is data, not prose, so the English-only convention does not apply here.
                        var original = "первая строка\r\narrow → and dash — здесь\r\nvar s = \"q\"; path = C:\\tmp\r\n";
                        var created = await SendAsync(client, $"RunTool|create_file|{UriEnc(rel)}|{UriEnc(original)}", cts.Token);
                        Assert.IsTrue((bool)JObject.Parse(created)["success"], "create_file should succeed: " + created);

                        // read #1 — exactly what the model receives
                        var read1 = await ReadAllTextsAsync(client, rel, cts.Token);
                        Assert.HasCount(3, read1, "expected 3 lines, got: " + string.Join(" | ", read1));
                        Assert.AreEqual("arrow → and dash — здесь", read1[1], "non-ASCII line must survive read");

                        var bytesBefore = File.ReadAllBytes(path);

                        // replace #1 — no-op: the content copied verbatim from the read is passed back
                        var oldText = string.Join("\n", read1);
                        var resp = await SendAsync(client,
                            $"RunTool|replace_file_lines|{UriEnc(rel)}|1|{UriEnc(oldText)}|{UriEnc(oldText)}", cts.Token);
                        var obj = JObject.Parse(resp);
                        Assert.IsTrue((bool)obj["success"], "no-op replace must succeed: " + resp);
                        Assert.IsNull(obj["auto_corrected"], "verbatim old_lines must not need auto-correction: " + resp);
                        Assert.IsNull(obj["matched_ignoring_first_line_indent"], "first-line indentation must match: " + resp);
                        CollectionAssert.AreEqual(bytesBefore, File.ReadAllBytes(path),
                            "a no-op replace must not change a single byte (encoding, BOM and EOL preserved)");

                        // read #2 — must be identical to read #1
                        var read2 = await ReadAllTextsAsync(client, rel, cts.Token);
                        CollectionAssert.AreEqual(read1.ToArray(), read2.ToArray(),
                            "read → replace → read must be symmetric (1:1)");

                        // replace #2 — a real edit: only the targeted line changes
                        resp = await SendAsync(client,
                            $"RunTool|replace_file_lines|{UriEnc(rel)}|2|{UriEnc(read2[1])}|{UriEnc("правка → строка")}", cts.Token);
                        obj = JObject.Parse(resp);
                        Assert.IsTrue((bool)obj["success"], "targeted replace must succeed: " + resp);

                        var read3 = await ReadAllTextsAsync(client, rel, cts.Token);
                        Assert.AreEqual("правка → строка", read3[1], "the edited line must be the new one");
                        Assert.AreEqual(read1[0], read3[0], "line 1 must be untouched");
                        Assert.AreEqual(read1[2], read3[2], "line 3 must be untouched");

                        await SendAsync(client, $"RunTool|delete_file|{UriEnc(rel)}", cts.Token);
                    }
                }
                finally
                {
                    CleanupFiles(path);
                    TryKill(vs);
                }
            }
        }

        /// <summary>
        /// Calls read_file_lines over IPC with pagination until has_more_results is false and returns the raw line
        /// texts (no line numbers) — i.e. exactly the strings the model is expected to copy into old_lines.
        /// </summary>
        private async Task<System.Collections.Generic.IList<string>> ReadAllTextsAsync(
            IpcClient client, string relPath, CancellationToken ct)
        {
            var texts = new System.Collections.Generic.List<string>();
            var start = 1;

            while (true)
            {
                var resp = await SendAsync(client, $"RunTool|read_file_lines|{UriEnc(relPath)}|{start}|9999", ct);
                var obj = JObject.Parse(resp);
                Assert.IsTrue((bool)obj["success"], "read_file_lines should succeed: " + resp);

                var entries = (obj["content"] as JArray)?.OfType<JObject>().ToList()
                              ?? new System.Collections.Generic.List<JObject>();

                foreach (var entry in entries)
                    texts.Add(entry["text"].Value<string>());

                if (!(obj["has_more_results"]?.Value<bool>() ?? false) || entries.Count == 0)
                    return texts;

                start = entries[entries.Count - 1]["line_number"].Value<int>() + 1;
            }
        }

        [TestMethod]
        public async Task RunTool_FormatDocument_FormatsCSharpAsync()
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3)))
            {
                EnsureScratchExists();
                var path = ScratchPath("e2e_format.cs");
                CleanupFiles(path);
                var vs = await VsLauncher.StartExperimentalInstanceAsync(cts.Token);
                try
                {
                    var solutionPath = GetSolutionPath();
                    var client = await IpcClient.ConnectAsync(PipeName, TimeSpan.FromMinutes(1), cts.Token);
                    using (client)
                    {
                        Assert.AreEqual("OK", await SendAsync(client, $"OpenSolution|{solutionPath}", cts.Token));
                        var rel = "LMLocal.Tests.E2E.VSIX\\Scratch\\e2e_format.cs";

                        var code = "namespace Demo { public class C { public void M( ) { int x=1; } } }";
                        await SendAsync(client, $"RunTool|create_file|{UriEnc(rel)}|{UriEnc(code)}", cts.Token);

                        var resp = await SendAsync(client, $"RunTool|format_document|{UriEnc(rel)}|false", cts.Token);
                        var obj = JObject.Parse(resp);
                        Assert.IsTrue((bool)obj["success"], "format_document should succeed: " + resp);
                        Assert.IsTrue(File.Exists(path), "formatted file should still exist");

                        await SendAsync(client, $"RunTool|delete_file|{UriEnc(rel)}", cts.Token);
                    }
                }
                finally
                {
                    CleanupFiles(path);
                    TryKill(vs);
                }
            }
        }

        [TestMethod]
        public async Task RunTool_FormatDocument_Markdown_IsSkippedNotFailedAsync()
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3)))
            {
                EnsureScratchExists();
                var path = ScratchPath("e2e_format.md");
                CleanupFiles(path);
                var vs = await VsLauncher.StartExperimentalInstanceAsync(cts.Token);
                try
                {
                    var client = await IpcClient.ConnectAsync(PipeName, TimeSpan.FromMinutes(1), cts.Token);
                    using (client)
                    {
                        Assert.AreEqual("OK", await SendAsync(client, $"OpenSolution|{GetSolutionPath()}", cts.Token));
                        var rel = "LMLocal.Tests.E2E.VSIX\\Scratch\\e2e_format.md";

                        var content = "# Title\n\nSome   text  with   spaces.\n";
                        await SendAsync(client, $"RunTool|create_file|{UriEnc(rel)}|{UriEnc(content)}", cts.Token);
                        var before = File.ReadAllBytes(path);

                        // VS has no Markdown formatter: previously COM E_FAIL, now a clear no-op.
                        var resp = await SendAsync(client, $"RunTool|format_document|{UriEnc(rel)}|false", cts.Token);
                        var obj = JObject.Parse(resp);
                        Assert.IsTrue((bool)obj["success"], "format_document on .md should not fail: " + resp);
                        Assert.IsTrue(obj["skipped"]?.Value<bool>() ?? false, "format_document on .md should report skipped: " + resp);
                        Assert.Contains(".md", obj["note"]?.Value<string>() ?? "", "note should name the file type: " + resp);
                        CollectionAssert.AreEqual(before, File.ReadAllBytes(path), "a skipped format must not change the file");

                        await SendAsync(client, $"RunTool|delete_file|{UriEnc(rel)}", cts.Token);
                    }
                }
                finally
                {
                    CleanupFiles(path);
                    TryKill(vs);
                }
            }
        }

        [TestMethod]
        public async Task RunTool_SearchSolutionKnowledge_ReturnsJsonAsync()
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3)))
            {
                var vs = await VsLauncher.StartExperimentalInstanceAsync(cts.Token);
                try
                {
                    var solutionPath = GetSolutionPath();
                    var client = await IpcClient.ConnectAsync(PipeName, TimeSpan.FromMinutes(1), cts.Token);
                    using (client)
                    {
                        Assert.AreEqual("OK", await SendAsync(client, $"OpenSolution|{solutionPath}", cts.Token));

                        var resp = await SendAsync(client, $"RunTool|search_solution_knowledge|{UriEnc("subagents")}|5", cts.Token);
                        Assert.IsFalse(string.IsNullOrEmpty(resp), "Response should not be empty");
                        Assert.StartsWith("{", resp, $"Response should be JSON, but got: {resp}");

                        var obj = JObject.Parse(resp);
                        Assert.IsTrue(obj.ContainsKey("success"), "Response should contain 'success' key");
                        Assert.IsTrue(obj.ContainsKey("results"), "Response should contain 'results' key");
                        Assert.IsTrue(obj.ContainsKey("total_matches"), "Response should contain 'total_matches' key");
                    }
                }
                finally
                {
                    TryKill(vs);
                }
            }
        }

        [TestMethod]
        public async Task RunTool_SearchChatHistory_ReturnsJsonAsync()
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3)))
            {
                var vs = await VsLauncher.StartExperimentalInstanceAsync(cts.Token);
                try
                {
                    var solutionPath = GetSolutionPath();
                    var client = await IpcClient.ConnectAsync(PipeName, TimeSpan.FromMinutes(1), cts.Token);
                    using (client)
                    {
                        Assert.AreEqual("OK", await SendAsync(client, $"OpenSolution|{solutionPath}", cts.Token));

                        var resp = await SendAsync(client, $"RunTool|search_chat_history|{UriEnc("tools")}|5", cts.Token);
                        Assert.IsFalse(string.IsNullOrEmpty(resp), "Response should not be empty");
                        Assert.StartsWith("{", resp, $"Response should be JSON, but got: {resp}");

                        var obj = JObject.Parse(resp);
                        Assert.IsTrue(obj.ContainsKey("success"), "Response should contain 'success' key");
                        Assert.IsTrue(obj.ContainsKey("results"), "Response should contain 'results' key");
                        Assert.IsTrue(obj.ContainsKey("total_matches"), "Response should contain 'total_matches' key");
                    }
                }
                finally
                {
                    TryKill(vs);
                }
            }
        }

        [TestMethod]
        public async Task RunTool_InspectType_ReturnsJsonAsync()
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3)))
            {
                var vs = await VsLauncher.StartExperimentalInstanceAsync(cts.Token);
                try
                {
                    var solutionPath = GetSolutionPath();
                    var client = await IpcClient.ConnectAsync(PipeName, TimeSpan.FromMinutes(1), cts.Token);
                    using (client)
                    {
                        Assert.AreEqual("OK", await SendAsync(client, $"OpenSolution|{solutionPath}", cts.Token));

                        var resp = await SendAsync(client, $"RunTool|inspect_type|{UriEnc("System.IO.Path")}", cts.Token);
                        Assert.IsFalse(string.IsNullOrEmpty(resp), "Response should not be empty");
                        Assert.StartsWith("{", resp, $"Response should be JSON, but got: {resp}");

                        var obj = JObject.Parse(resp);
                        Assert.IsTrue(obj.ContainsKey("success"), "Response should contain 'success' key");
                        Assert.IsTrue(obj.ContainsKey("type_name"), "Response should contain 'type_name' key");
                        Assert.IsTrue(obj.ContainsKey("data") || obj.ContainsKey("matches"),
                            "Response should contain 'data' or 'matches'");
                    }
                }
                finally
                {
                    TryKill(vs);
                }
            }
        }

        [TestMethod]
        public async Task RunTool_GetSymbolInfoJs_ReturnsJsonAsync()
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3)))
            {
                var vs = await VsLauncher.StartExperimentalInstanceAsync(cts.Token);
                try
                {
                    var solutionPath = GetSolutionPath();
                    var client = await IpcClient.ConnectAsync(PipeName, TimeSpan.FromMinutes(1), cts.Token);
                    using (client)
                    {
                        Assert.AreEqual("OK", await SendAsync(client, $"OpenSolution|{solutionPath}", cts.Token));

                        var resp = await SendAsync(client, $"RunTool|get_symbol_info_js|{UriEnc("lmInit")}", cts.Token);
                        Assert.IsFalse(string.IsNullOrEmpty(resp), "Response should not be empty");
                        Assert.StartsWith("{", resp, $"Response should be JSON, but got: {resp}");

                        var obj = JObject.Parse(resp);
                        Assert.IsTrue(obj.ContainsKey("success"), "Response should contain 'success' key");
                        Assert.IsTrue(obj.ContainsKey("symbol_name"), "Response should contain 'symbol_name' key");
                        Assert.IsTrue(obj.ContainsKey("files"), "Response should contain 'files' key");
                    }
                }
                finally
                {
                    TryKill(vs);
                }
            }
        }

        [TestMethod]
        public async Task RunTool_SetFileProjectStatus_UnknownProject_ReturnsJsonErrorAsync()
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3)))
            {
                var vs = await VsLauncher.StartExperimentalInstanceAsync(cts.Token);
                try
                {
                    var solutionPath = GetSolutionPath();
                    var client = await IpcClient.ConnectAsync(PipeName, TimeSpan.FromMinutes(1), cts.Token);
                    using (client)
                    {
                        Assert.AreEqual("OK", await SendAsync(client, $"OpenSolution|{solutionPath}", cts.Token));

                        // Nonexistent project path → tool returns JSON error payload.
                        var resp = await SendAsync(client,
                            $"RunTool|set_file_project_status|{UriEnc("LMLocal.Tests.E2E.VSIX\\Scratch\\nope.cs")}|{UriEnc("BenchScratch\\NoProject.csproj")}|true",
                            cts.Token);
                        Assert.IsFalse(string.IsNullOrEmpty(resp), "Response should not be empty");
                        Assert.StartsWith("{", resp, $"Response should be JSON, but got: {resp}");

                        var obj = JObject.Parse(resp);
                        Assert.IsTrue(obj.ContainsKey("success"), "Response should contain 'success' key");
                        Assert.IsFalse((bool)obj["success"], "set_file_project_status should fail for a missing project");
                        Assert.IsTrue(obj.ContainsKey("error_message"), "Response should contain 'error_message' key");
                    }
                }
                finally
                {
                    TryKill(vs);
                }
            }
        }

        // ---- search_file_content: project_filter with a path, OR / quote fallback ---------------
        // Terms are assembled at runtime so this source file never contains the full search text
        // literally (the VSIX test project is part of the solution and is searched too).
        private const string TermA = "CompactForced" + "Async";
        private const string TermB = "ConsolidateLast" + "ExchangeAsync";

        /// <summary>
        /// Positional RunTool order for search_file_content: text, group_by, extension_filter,
        /// project_filter, page_token, max_results.
        /// </summary>
        private async Task<JObject> SearchAsync(IpcClient client, string text, string projectFilter, CancellationToken ct)
        {
            var resp = await SendAsync(client,
                $"RunTool|search_file_content|{UriEnc(text)}|file||{UriEnc(projectFilter)}||500", ct);
            Assert.StartsWith("{", resp, "search_file_content should return JSON: " + resp);
            var obj = JObject.Parse(resp);
            Assert.IsTrue((bool)obj["success"], "search_file_content should succeed: " + resp);
            return obj;
        }

        private static System.Collections.Generic.List<string> FilePaths(JObject searchResponse) =>
            (searchResponse["results"] as JArray ?? new JArray())
                .Select(r => r["file_path"].Value<string>())
                .ToList();

        [TestMethod]
        public async Task RunTool_SearchFileContent_PathProjectFilter_LimitsToFolderAsync()
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3)))
            {
                var vs = await VsLauncher.StartExperimentalInstanceAsync(cts.Token);
                try
                {
                    var client = await IpcClient.ConnectAsync(PipeName, TimeSpan.FromMinutes(1), cts.Token);
                    using (client)
                    {
                        Assert.AreEqual("OK", await SendAsync(client, $"OpenSolution|{GetSolutionPath()}", cts.Token));

                        // Unfiltered baseline: the term also occurs outside LMLocal\Application (tests) and in the
                        // sibling folder LMLocal\Application\ChatSession, which a folder filter must exclude.
                        var all = FilePaths(await SearchAsync(client, TermA, "", cts.Token));
                        Assert.Contains(p => !p.StartsWith(@"LMLocal\Application\", StringComparison.OrdinalIgnoreCase), all,
                            "baseline should include files outside LMLocal\\Application: " + string.Join(", ", all));
                        Assert.Contains(p => p.StartsWith(@"LMLocal\Application\ChatSession\", StringComparison.OrdinalIgnoreCase), all,
                            "baseline should include LMLocal\\Application\\ChatSession: " + string.Join(", ", all));

                        foreach (var filter in new[] { @"LMLocal\Application\Chat", "LMLocal/Application/Chat", @"LMLocal\\Application\\Chat", @"Application\Chat\" })
                        {
                            var obj = await SearchAsync(client, TermA, filter, cts.Token);
                            var paths = FilePaths(obj);
                            Assert.IsGreaterThan(0, paths.Count, $"filter '{filter}' should find matches: {obj}");
                            Assert.IsTrue(paths.All(p => p.StartsWith(@"LMLocal\Application\Chat\", StringComparison.OrdinalIgnoreCase)),
                                $"filter '{filter}' should keep only the LMLocal\\Application\\Chat folder (not ChatSession): " + string.Join(", ", paths));
                            Assert.Contains(p => p.EndsWith("HistoryCompactor.cs", StringComparison.OrdinalIgnoreCase), paths,
                                $"filter '{filter}' should include HistoryCompactor.cs: " + string.Join(", ", paths));
                            Assert.IsNull(obj["note"], "a literal search with hits must not carry a fallback note");
                        }

                        // A trailing separator means the folder itself: 'LMLocal\' must not pull in LMLocal.Tests.*.
                        var lmLocalOnly = FilePaths(await SearchAsync(client, TermA, @"LMLocal\", cts.Token));
                        Assert.IsTrue(lmLocalOnly.Count > 0 && lmLocalOnly.All(p => p.StartsWith(@"LMLocal\", StringComparison.OrdinalIgnoreCase)),
                            "'LMLocal\\' should keep only the LMLocal folder: " + string.Join(", ", lmLocalOnly));

                        // A separator-free filter keeps the original per-component semantics.
                        var byProject = FilePaths(await SearchAsync(client, TermA, "LMLocal.Tests.Unit", cts.Token));
                        Assert.IsTrue(byProject.Count > 0 && byProject.All(p => p.StartsWith(@"LMLocal.Tests.Unit\", StringComparison.OrdinalIgnoreCase)),
                            "project-name filter should keep only LMLocal.Tests.Unit: " + string.Join(", ", byProject));
                    }
                }
                finally
                {
                    TryKill(vs);
                }
            }
        }

        [TestMethod]
        public async Task RunTool_SearchFileContent_OrQuery_FallsBackToSplitTermsAsync()
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3)))
            {
                var vs = await VsLauncher.StartExperimentalInstanceAsync(cts.Token);
                try
                {
                    var client = await IpcClient.ConnectAsync(PipeName, TimeSpan.FromMinutes(1), cts.Token);
                    using (client)
                    {
                        Assert.AreEqual("OK", await SendAsync(client, $"OpenSolution|{GetSolutionPath()}", cts.Token));

                        var obj = await SearchAsync(client, TermA + " OR " + TermB, @"LMLocal\Application", cts.Token);

                        var note = obj["note"]?.Value<string>();
                        Assert.IsNotNull(note, "an OR query without a literal match should carry a fallback note: " + obj);
                        Assert.Contains("does not support OR", note);
                        Assert.IsGreaterThan(0, obj["total_matches"].Value<int>(), "split terms should match: " + obj);

                        var matchedTerms = (obj["results"] as JArray)
                            .SelectMany(r => r["matches"] as JArray)
                            .Select(m => m["matched_term"]?.Value<string>())
                            .Distinct()
                            .ToList();
                        CollectionAssert.Contains(matchedTerms, TermA, "matched_term should report " + TermA);
                        CollectionAssert.Contains(matchedTerms, TermB, "matched_term should report " + TermB);
                        CollectionAssert.DoesNotContain(matchedTerms, null, "every fallback match must carry matched_term");

                        // Pagination/cache path: the same query again returns the cached result with the note.
                        var again = await SearchAsync(client, TermA + " OR " + TermB, @"LMLocal\Application", cts.Token);
                        Assert.AreEqual(note, again["note"]?.Value<string>(), "cached response must keep the note");

                        // No split term matches either: still a note, now stating the negative explicitly.
                        var none = await SearchAsync(client, "lmlocal-e2e-" + Guid.NewGuid().ToString("N") + " OR lmlocal-e2e-" + Guid.NewGuid().ToString("N"), "", cts.Token);
                        Assert.AreEqual(0, none["total_matches"].Value<int>(), "random terms should not match: " + none);
                        Assert.Contains("found no matches either", none["note"]?.Value<string>() ?? "", "negative fallback should say so: " + none);
                    }
                }
                finally
                {
                    TryKill(vs);
                }
            }
        }

        // Exists literally in this file, so the literal match must win over the OR-split fallback.
        private const string OrLiteralInThisFile = "lmlocal-e2e-or-literal ALPHA OR BETA";

        [TestMethod]
        public async Task RunTool_SearchFileContent_OrLiteralPresent_LiteralWinsAsync()
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3)))
            {
                var vs = await VsLauncher.StartExperimentalInstanceAsync(cts.Token);
                try
                {
                    var client = await IpcClient.ConnectAsync(PipeName, TimeSpan.FromMinutes(1), cts.Token);
                    using (client)
                    {
                        Assert.AreEqual("OK", await SendAsync(client, $"OpenSolution|{GetSolutionPath()}", cts.Token));

                        var obj = await SearchAsync(client, OrLiteralInThisFile, "LMLocal.Tests.E2E.VSIX", cts.Token);

                        Assert.IsNull(obj["note"], "a literal match must not trigger the OR fallback: " + obj);
                        var paths = FilePaths(obj);
                        Assert.Contains(p => p.EndsWith("IpcRunToolTests.cs", StringComparison.OrdinalIgnoreCase), paths,
                            "literal should be found in IpcRunToolTests.cs: " + string.Join(", ", paths));
                        var terms = (obj["results"] as JArray).SelectMany(r => r["matches"] as JArray).Select(m => m["matched_term"]).ToList();
                        Assert.IsTrue(terms.All(t => t == null), "literal matches must not carry matched_term");
                    }
                }
                finally
                {
                    TryKill(vs);
                }
            }
        }

        [TestMethod]
        public async Task RunTool_SearchFileContent_WholeTextQuoted_FallsBackToUnquotedAsync()
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3)))
            {
                var vs = await VsLauncher.StartExperimentalInstanceAsync(cts.Token);
                try
                {
                    var client = await IpcClient.ConnectAsync(PipeName, TimeSpan.FromMinutes(1), cts.Token);
                    using (client)
                    {
                        Assert.AreEqual("OK", await SendAsync(client, $"OpenSolution|{GetSolutionPath()}", cts.Token));

                        var plain = await SearchAsync(client, TermA, @"LMLocal\Application", cts.Token);
                        var quoted = await SearchAsync(client, "\"" + TermA + "\"", @"LMLocal\Application", cts.Token);

                        Assert.Contains("quotes were removed", quoted["note"]?.Value<string>() ?? "", "quoted text should fall back: " + quoted);
                        Assert.AreEqual(plain["total_matches"].Value<int>(), quoted["total_matches"].Value<int>(),
                            "unquoted fallback should find the same matches as the plain term");
                    }
                }
                finally
                {
                    TryKill(vs);
                }
            }
        }

        private string GetSolutionPath()
        {
            var solutionPath = Path.GetFullPath(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "LMLocal.sln")
            );

            if (!System.IO.File.Exists(solutionPath))
                throw new InvalidOperationException($"Test solution not found at '{solutionPath}'");
            return solutionPath;
        }

        private static void TryKill(System.Diagnostics.Process process)
        {
            try
            {
                if (process != null && !process.HasExited)
                    process.Kill();

                process.Dispose();
            }
            catch
            {
                // Best effort cleanup.
            }
        }

        public TestContext TestContext { get; set; }

    }
}
