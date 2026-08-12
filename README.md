# 🤖 LMLocal

**LMLocal** is a local AI chat assistant for Visual Studio 2022/2026 with agentic capabilities. It works with local engines (LM Studio, Ollama, Jan, Llama.cpp) and cloud providers (OpenAI-compatible APIs). You can ask it to edit code, run builds/tests, and apply changes – all in a single, step-by-step conversational flow.

![Visual Studio](https://img.shields.io/badge/Visual%20Studio-2022%20%2F%202026-purple?style=flat&logo=visual-studio)
![License](https://img.shields.io/badge/License-MIT-green)

---

> [!NOTE]
> **Safe & Controlled:** By default, write/modify tools are **disabled** – the AI can read files but cannot change anything. Writing tools are optional, must be explicitly turned on in Settings, and all changes are tracked in the Changes panel with one‑click rollback. The codebase is open‑source – inspect it or contribute on [GitHub](https://github.com/truehot/LMLocal).

---

## 📸 Screenshots

<a href="https://raw.githubusercontent.com/truehot/LMLocal/main/Assets/2022_dark_mid.png" target="_blank">
  <img src="https://raw.githubusercontent.com/truehot/LMLocal/main/Assets/2022_dark_mid.png" alt="VS 2022 - mid dark" width="10%" />
</a>

<a href="https://raw.githubusercontent.com/truehot/LMLocal/main/Assets/2022_mid_light.png" target="_blank">
  <img src="https://raw.githubusercontent.com/truehot/LMLocal/main/Assets/2022_mid_light.png" alt="VS 2022 - mid light" width="10%" />
</a>

---

## 📖 Table of Contents

- [Core Features](#core-features)
- [Requirements](#requirements)
- [Installation](#installation)
- [Getting Started](#getting-started)
- [All Features](#all-features)
- [AI Instructions & Modes](#ai-instructions--modes)
- [Providers](#providers)
- [Chat History Dialog](#chat-history-dialog)
- [Image Attachments (Multimodal Chat)](#image-attachments-multimodal-chat)
- [Built‑in AI Tools](#built‑in-ai-tools)
- [List of built‑in tools](#list-of-built‑in-tools)
- [Smart Workflows & Best Practices](#smart-workflows--best-practices)
- [Context Menu Commands](#context-menu-commands)
- [History Optimization: Clean Whitespace](#history-optimization-clean-whitespace)
- [Auto‑Completions](#auto-completions)
- [MCP Support](#model-context-protocol-mcp-support)
- [MCP Configuration](#how-to-configure-mcp-servers)
- [Troubleshooting](#troubleshooting)
- [Data & Configuration](#data--configuration)
- [License & Compliance & Third-Party](#license--compliance--third-party)

---

## ⚡ Core Features

- **💬 Chat & Agentic Actions** – In‑IDE chat with streaming, agentic file edits, builds/tests, manual rollback via the Changes panel, **and code autocompletions** as you type.
- **🌐 Local & Cloud LLMs** – Works with Ollama, LM Studio, Jan, Llama.cpp, and any OpenAI‑compatible API.
- **🔌 MCP Extensibility** – Add external tools via Model Context Protocol (stdio/http).
- **📂 Flexible Workspace Context** – Quickly pass code to the AI: use the **`+`** button to attach the active file in the background, or right-click to send specific text selections.
- **🧩 Reasoning, Roles & Efficiency** – Expandable thoughts, collapsible tool calls, custom system presets, token optimization (summarization, whitespace cleaning, live stats).
- **⚙️ Built-in Provider Integrations** – Pre-configured internal handlers for specific AI platforms (including Ollama, Groq, OpenRouter, Google AI Studio and DeepSeek). The extension natively manages each provider's protocol variations and stream parsing — enter your target API Base URL and personal API Key to connect.
- **💾 Persistent & Reliable** – Auto‑connects on startup, restores your last session from local logs, provides a chat history dialog to browse and restore previous sessions, centralized settings.
- **🔄 Hot-Swappable LLMs** – Switch between local models or cloud providers on the fly without clearing the chat. The new model seamlessly continues the conversation using the existing history and context.

---

## 🛠 Requirements

To use LMLocal, you need:
- [x] **Visual Studio 2022 or 2026**
- [x] **An AI Backend / Provider** (choose one):
  * **Cloud Providers:** Any OpenAI-compatible API (OpenAI, DeepSeek, Groq, OpenRouter, Google AI Studio, etc. — requires an API key).
  * **Local Engines** (must be installed and running with a loaded model):
    * **LM Studio** (local server at `http://127.0.0.1:1234`)
    * **Ollama** (local server at `http://127.0.0.1:11434`)
    * **Jan** (local server at `http://127.0.0.1:1337`)
    * **Llama.cpp** (local server at `http://127.0.0.1:8080`)

---

## 🚀 Installation

### Option 1: Visual Studio Marketplace (Recommended)
1. Open Visual Studio.
2. Go to `Extensions` > `Manage Extensions`.
3. Search for **LMLocal** and click **Download**.
4. Restart Visual Studio to complete the installation.

### Option 2: Manual VSIX
1. Download the `.vsix` file from the [Marketplace](https://marketplace.visualstudio.com/items?itemName=7gsocvrdqco5wtvsp3nuym3pxrwnwxxr7yuow4ymkl5of6epygaa.LMLocal).
2. Double-click the file and follow the VSIX Installer prompts.

---

## 🏁 Getting Started

### Part 1. Initial Setup (One-Time Configuration)

1. **Launch:** Open the **LM Local Chat** tool window using one of the following methods:
   * **Method A:** Open it directly from the top **Extensions** menu.
   * **Method B:** In the top menu, go to **View ➔ Other Windows ➔ LM Local Chat**.
2. **Position the Window (Optional):** Click and drag the opened window to dock it wherever fits your workflow best — for example, right next to the **Solution Explorer**.
3. **Configure Your Provider:** 
   * Click the menu icon (**`…`**) and open **Settings...**.
   * Under the **AI Provider** section, select your preferred backend from the dropdown menu:
     * **LM Studio (local)** – Automatically targets `http://127.0.0.1:1234`
     * **Ollama (local)** – Automatically targets `http://127.0.0.1:11434`
     * **Jan (local)** – Automatically targets `http://127.0.0.1:1337`
     * **Llama.cpp (local)** – Automatically targets `http://127.0.0.1:8080`
     * **OpenAI compatible (custom)** – Allows you to supply a custom base URL and authorization keys for remote endpoints or custom gateways.
   * *Note: Choosing a local provider automatically configures the correct default port and endpoint structure. For local Microsoft Foundry, select `OpenAI compatible` and use `http://127.0.0.1:<port>` (where `<port>` is your active service port).*
   * *Tip: If you have multiple providers, it is recommended to set them up first via the "Providers..." menu option.*
4. **Verify the Connection:**
   * Click the **"Test"** button located directly to the right of the **API Base URL** input field. 
   * This pings the specified endpoint to verify if the server is active, accessible, and correctly responding.

---

### Part 2. How to Use the Chat

5. **Select an Instruction Preset (Optional):** Open the **AI Instructions...** window from the menu to select from pre-defined AI presets.
   * Each preset has its own pre-configured system prompt and temperature.
   * You can toggle individual presets or parameters on/off.
6. **Quick Tool Mode Switch (Optional):** Use the dropdown next to the message input to instantly switch between No tools (AI cannot read or write), Read only (AI can read files but not modify them), and Read & Write (full tool access). 
7. **Context (Optional):** Click the **`+`** button to include the entire content of the active document into the conversation.
8. **Chat:** Type your message and click **Send** or hit `Enter` ⌨️.

---

### 💡 Interface & Interaction Tips

* **Keyboard Shortcuts:** Standard Chromium browser hotkeys work inside the chat window:
  * **`Ctrl + C`** / **`Ctrl + V`** — Copy text and paste messages (the right-click context menu is disabled).
  * **`Ctrl + F`** — Open the browser-native search bar to find text within the active conversation.
  * **`Ctrl + P`** — Open the print dialog to export or print the current chat log.
  * **`Alt + L`** — Send to LM Local content from editor (similar to context menu command).
* **Copying AI Code:** To copy code blocks generated by the model, click the **`Copy`** button located in the top-right corner of the code block.
* **Model Reasoning:** The model's internal thinking process is neatly hidden inside the collapsible **`Thoughts`** block at the beginning of the response. Click it anytime to expand and view the full logic.
* **Token & Context Tracking:** Hover your mouse over the top connection bar (where the model name is shown). If supported by your provider (like LM Studio), a tooltip will appear showing exactly how many tokens have been consumed out of the maximum available context limit.
* **Model & Provider Selection:** Click the model name in the top header to open the **Select Model** window. Here you can switch between different AI providers, search or filter models, toggle the "Loaded only" view, and quickly change your active LLM.
* ➕ **Active Window Context:** Click the **`+`** button to include the entire content of the file currently open in the active document. 
  * *Auto-turn off:* The button automatically deactivates after the request is sent, as the document becomes part of the active chat history.
  * *UI & Logs:* The attached file content is kept hidden to avoid cluttering the chat UI, but it is tracked and visible in the extension logs.
* **Drag and Drop:** You can drag and drop a text file (e.g., .txt, .md, .csv) directly into the chat window text input – its content will be loaded automatically.
* **Attach Files (Browse file):** Pick one or more text files from disk; their contents are inserted into the chat input code blocks (same rules as drag & drop).
* **Context Menu:** Right‑click in editor window or solution explorer and choose "Send to LM Local" – this adds content from the clipboard or a selected file to the conversation.
* ⏹️ **Stop** – Cancel an active generation.
* **"Clear chat" button** – Click the clear history icon (located next to the menu button in the top-right corner) to open a confirmation dialog, allowing you to choose how to handle the current conversation context:
  * **Start fresh:** Clear everything and open a completely empty chat.
  * **Move the last prompt and response:** Copy your last message along with the AI's answer into the new chat's history.
  * **Consolidate last exchange:** Merge your last message, tool results (code lines), and AI response into a single clean starting history.
  * **Summarize and move context:** Send a quick request to the AI to summarize this conversation, then save it as the first message in the new chat.

---

## ✨ All features

**Interface & User Experience**
- ☁️ **In-IDE Chat UI** – Tool window for LLM interaction without switching applications.
- 🌊 **Streaming Responses** – Real-time token delivery for instant visual feedback.
- 🤖 **Model & Provider Hot-Swapping** – Switch between AI models or providers on the fly directly from the chat header, automatically preserving your active session context.
- 🎨 **Visual Themes** – Multi-theme support (Dark, Mid-Dark, Mid-Light, Light).
- 📋 **Quick Copy** – A button above code blocks that copies the code to your clipboard.
- ↕️ **Collapse Large Code Blocks** – Limits the height of long code snippets with a scrollbar and an expand option.
- 🎭 **Role-Based Presets (Instructions)** – A window with pre-defined AI presets. You can customize each preset's system prompt and temperature, or toggle them on/off.
- ⌨️ **AI Inline Autocompletions** – AI‑powered line completion. When your cursor is at the end of a line, the AI analyzes the current context and suggests a continuation. Press Tab to accept. Completions are available only at line endings.
- 📄 **Drag & Drop Files** – Drag source files, logs, or configs directly into the chat input area. Text files are automatically wrapped in a markdown code fence with the correct language tag and a file-name comment hint. Supports up to **10 files** at once (200 KB max each) — `.cs`, `.json`, `.js`, `.ts`, `.html`, `.css`, `.md`, `.py`, `.xml`, `.yaml`, and many more.
- 🖼️ **Image Attachments** – Paste (`Ctrl+V`) or drag-and-drop images into the chat input for vision-capable models (JPEG/PNG, up to 3 per message, ~4 MB each). Images are **not** compressed and are **not** saved to history.
- ↕️ **Resizable Panels** – Drag the grip above the input or above the Changes panel to adjust their height.
- 📜 **Chat History Dialog** – Browse and load past conversations from history logs.


**Context & Solution Awareness**
- 🛠️ **Advanced AI Tool Integration** – Allows the AI to analyze your open solution, read file contents, and execute actions like building the solution, formatting documents, or running unit tests.
- 🔁 **Tool Loop Prevention**  – Monitors tool execution and halts the process if a tool (e.g., read_file_lines) is consecutively invoked three times with identical arguments, preventing infinite loops and token waste.
- 📝 **Automated Code Editing** – Enabled tools can automatically create, delete, or modify code files directly inside Visual Studio.
- 🛡️ **Changes & Rollback Manager** – Shows all file modifications in a dedicated real-time panel above the chat, allowing you to review diffs, accept changes, or roll them back in one click.
- ➕ **Active Window Context** – Dedicated "+" button to include active editor content in the request.
- 🧠 **Thought/Reasoning Support** – Support for reasoning models; "thoughts" are displayed in expandable blocks.
- 🛡️ **Smart History Buffering** – Automatically removes the oldest messages from the chat UI once the history exceeds 200 entries to prevent rendering lag, without affecting the underlying conversation data.
- 🎯 **Context Menu Integration** – Right-click context menu command ("Send to LM Local") that copies text directly into the chat prompt without auto-submitting. If text is highlighted, it sends the selection; if nothing is selected, it falls back to sending the entire active document.
- 🔄 **Quick Tool Mode Toggle** – Dropdown next to the input box to switch between No tools, Read only, and Read & Write for the current session without opening Settings.


**Efficiency & Token Management**
- 📉 **Conversation Summarization** – Condenses older messages into a concise overview when the conversation grows long.
- 🧹 **History Optimization** – Optionally compresses redundant whitespace and trims extra lines from background history entries to save tokens.
- 📊 **Live Stats** – Status bar metrics: real-time speed (tokens/sec) and total token count.
- 🏷️ **Token Stats Badge** – After each response, a compact summary shows total tokens, cached tokens (when available), and the average generation speed (tokens/sec).

**Infrastructure & Settings**
- ⚙️ **Persistent Settings** – Centralized configuration for API URLs, stream timeouts, and history management.
- 🔌 **Connect on Startup** – Automatically connects to the LLM server on extension startup.
- 🔄 **Auto-Load History on Startup** – Automatically restores the most recent conversation from saved history.
- ⏳ **Customizable Timeout** – Adjustable streaming inactivity limit for slower local models (0 = never timeout).
- 📂 **Local Chat Logging** – Appends all conversation events to .jsonl files in `%LOCALAPPDATA%\LMLocalChat\ChatHistory\`. This persistent log serves as the data source for session recovery when Visual Studio starts.
- 🌐 **Streamable MCP Support** – Supports the Model Context Protocol to dynamically scale the AI's toolkit via both local process-based (`stdio`) and remote network-based (`http`) transports.

---

### ⚙️ Providers

The **"Providers..."** dialog allows you to create and save multiple provider profiles (servers) so you don't have to re-enter your API keys and base URLs every time. You can store as many profiles as you need, including both **local servers** (like Ollama running on your machine) and **cloud remote services** (like Groq, OpenAI, or Gemini).

Once configured, you can seamlessly switch between your saved profiles via the main settings.

> 🔒 **Privacy & Data Usage Note:** Unlike local servers which keep 100% of your data offline on your machine, **cloud remote providers** process your requests on external servers. Data retention policies vary significantly by provider - some services may use your prompt history and codebase context for model training by default. Always verify the provider's privacy policy and terms of service before transmitting proprietary or sensitive source code.

Here is a quick end-to-end example of how to configure a custom remote endpoint and activate it inside the extension.

#### Step 1: Create the Provider Profile
1. Click the menu icon (**`…`**) and select **"Providers..."**.
2. Click **"+ Add Profile"** and fill in the fields:
   * **Profile name:** `Ollama cloud`
   * **Provider type:** Select **OpenAI compatible** from the dropdown.
   * **API base URL:** `https://ollama.com/`
   * **API key:** Enter your cloud provider API key.
   * > 💡 **Note:** The extension allows any profile names, but if you create multiple profiles with completely identical fields, the system will always use the first one.*
3. Click **Apply**, then click **Save Changes** to close the window.

#### Step 2: Switch to the New Provider
1. Open the menu (**`…`**) again and select **"Settings..."**.
2. Under the **AI Provider** dropdown, select your newly created **`Ollama cloud`** profile.
3. Save settings, and you are ready to chat!

#### Step 3: Select Your Model
1. Click the model name (or **"Select model..."** placeholder) in the top header.
2. Search, filter, and select your desired model from the window to activate it.

#### 🆓 Free to Try (Free Limited Tiers / Credits Available)

| Provider | Provider Type | API Base URL |
| :--- | :--- | :--- |
| **Ollama** | OpenAI compatible | `https://ollama.com/` |
| **OpenRouter** | OpenAI compatible | `https://openrouter.ai/api/` |
| **Siliconflow** | OpenAI compatible | `https://api.siliconflow.com/` |
| **Doubleword AI** | OpenAI compatible | `https://api.doubleword.ai/` |
| **Hugging Face** | OpenAI compatible | `https://router.huggingface.co/` |
| **Alibaba Cloud Model Studio** | OpenAI compatible | `https://[*].eu-central-1.maas.aliyuncs.com/compatible-mode/` |
| **Parasail** | OpenAI compatible | `https://api.parasail.io/` |
| **Perplexity.ai** | OpenAI compatible | `https://api.perplexity.ai/router/` |
| **Mistral** | OpenAI compatible | `https://api.mistral.ai/` |
| **Cohere** | OpenAI compatible | `https://api.cohere.ai/compatibility/` |
| **Google AI Studio** | Gemini (cloud) | `https://generativelanguage.googleapis.com` |
| **Groq** | OpenAI compatible | `https://api.groq.com/openai/` |
| **GitHub Models** | Github Models via Azure (cloud) | `No longer available, will be removed in next releases` |

#### 💳 Pay to Try (Commercial / Premium)

| Provider | Provider Type | API Base URL |
| :--- | :--- | :--- |
| **DeepSeek** | DeepSeek (cloud) | `https://api.deepseek.com` |
| **Together AI** | Together AI (cloud) | `https://api.together.ai/` |
| **Fireworks AI** | OpenAI compatible | `https://api.fireworks.ai/inference/` |
| **OpenAI** | OpenAI compatible | `https://api.openai.com` |

---

## Built‑in AI Tools

The built‑in tools let the AI read, edit, build, and test your code. You control which tools are enabled, can review all changes before accepting them and manually roll back any change.

### What you can control

**In Settings (two checkboxes):**
- **`Enable built‑in AI tools (read‑only)`** – The AI can open and read files, but cannot change anything.
- **`Enable built‑in AI tools (write/modify)`** – The AI can create, change, or delete files.

> 💡 **Tip 1: Version Control Recommended**
> If you enable write/modify tools, using version control (e.g., Git) is strongly advised – it makes it easy to track, diff, and revert automated changes.
>
> 🧠 **Tip 2: Model Size & Capabilities**
> Built-in AI tools and MCP extensions heavily rely on advanced **Tool Calling (Function Calling)**. For a stable experience, it is recommended to use larger or specialized models (e.g., 14B+ parameters). Smaller models (like 7B/8B) may occasionally struggle with JSON formatting, hallucinate arguments, or trigger loop prevention.

**In the Built‑in Tools… dialog (list of built‑in tools):**  
Open this from the extension's main menu. You’ll see all built‑in tools (for example, `delete_file`, `replace_file_content`). Each tool can be enabled or disabled individually. Even if the global write/modify checkbox is on, you can still turn off specific tools like `delete_file`. Use “Enable All” or “Disable All” to change many at once, then click Save.

### Changes panel – see what was changed and revert if needed

When a tool edits a file, the changes are applied to the actual files in your solution. LMLocal tracks all modified files and shows them in a collapsible **Changes** panel inside the chat window. This list persists across solution reloads and Visual Studio restarts, so you can always review what the AI did.

The panel lets you:
- Click any file to see a diff of the changes.
- Click on a file's icon to open it in the Visual Studio editor.
- See labels: `New`, `Modified`, or `Deleted` next to each file.
- Hover over any file to access individual quick actions on the right side:
  - Click the **`✕`** (**Discard modifications**) button to revert changes for that specific file only.
  - Click the **`✓`** (**Accept modifications**) button to confirm changes for that specific file and remove it from the list.
- Switch between List view and Tree view.
- Click **`Review all`** – opens a side‑by‑side diff window for all changed files.
- Click **`Open all`** – opens all changed files in Visual Studio editor tabs.
- Click **`Discard all`** – reverts all changes using internal backups (files are restored to their state before the AI edits).
- Click **`Accept all`** – confirms the changes, removes the internal backups, and clears the list (you can no longer revert them afterward).

---

## List of built‑in tools

### Files and projects
- **`create_file`** – Creates a new file with initial content.
- **`delete_file`** – Deletes a file from the solution.
- **`find_files`** – Searches for files by name.
- **`list_directory`** – Lists files and folders in a given path.
- **`get_solution_overview`** – Returns a summary of projects, folders, and files.
- **`set_file_project_status`** – Includes or excludes a file from a project.

### Reading file content
- **`read_file_lines`** – Reads a specific range of lines.
- **`search_file_content`** – Searches for a text string (case‑insensitive) inside solution files.
- **`get_active_document`** – Returns the path and full text of the currently open document.

### Editing and formatting code
- **`replace_file_content`** – Replaces the entire file with new text.
- **`replace_file_lines`** – Replaces a range of lines (by numbers) with new content.
- **`insert_file_lines`** – Inserts lines at a specific position.
- **`format_document`** – Applies Visual Studio’s code formatting to the file.
- **`optimize_usings`** – Removes unused `using` statements and sorts the rest in C# files.

### Analysing code
- **`get_symbol_info`** – Finds declarations and references to a symbol (class, method, etc.) across the solution, with line numbers and context (uses Roslyn).

### Build and tests
- **`build_solution`** – Builds the whole solution.
- **`run_tests`** – Runs `dotnet test` for a specific `.csproj` and shows live output.

---

### 🎭 AI Instructions & Modes

The **"AI Instructions..."** window allows you to define specialized **System Prompts (roles)** and creativity levels (temperature) for different development tasks. The extension comes with pre-configured behavior templates like **Default**, **Improve**, **Review**, **Plan**, **Bugfix**, **Explain**, and **Tests**.

> [!TIP]
> **Prompt Caching Optimization:** Select your desired mode (e.g., Bugfix, Review, Explain) before sending your message. Changing the mode mid-conversation modifies the system prompt, which invalidates the server's prompt cache, increasing token costs and latency.

Once configured, you can instantly switch between these system roles using the dropdown menu directly in the main chat bar. Selecting a preset updates the AI's system prompt and temperature for that conversation.

#### How to Customize Modes:
1. Click the menu icon (**`…`**) and select **"AI Instructions..."**.
2. Select a target mode/role from the left panel (e.g., `Review` or `Bugfix`).
3. Configure its behavior in the right panel:
   * **Mode Toggle Checkbox:** Check or uncheck this box to show or hide this specific mode in your main chat bar dropdown.
   * **System Prompt:** Enter the base instructions that define the AI's role, processing rules, and operational constraints (e.g., telling the `Tests` mode to act as a QA Engineer and strictly generate xUnit tests in C#).
   * **Temperature:** Set the randomness/creativity threshold. Use values closer to `0` (e.g., `0.1` or `0.2`) for rigid, deterministic tasks like compiling and bug fixing, and closer to `1` for architectural planning or brainstorming.
   > 💡 **Note:** Always check your specific model's official documentation for recommended temperature settings, as some local models require strict defaults or a value of `0` to function properly without breaking formatting or structure.
4. Click **Save** to apply the changes to your chat environment.

---

## 📉 History Optimization: Clean Whitespace

When the **"Clean whitespace in history"** option is enabled in the extension settings, LMLocal automatically runs a cleanup pass on previous conversation turns before forwarding the payload to your AI backend. This reduces token overhead for local models by stripping redundant spaces, tabs, and excess newlines.

> [!NOTE]  
> **Under the Hood Only:** This optimization is **invisible** in the user interface. Your active chat window will always display responses with full formatting. The cleanup process only alters the raw background history array sent to the model to save context tokens.

### 🧹 What Gets Processed:

* **Collapses Whitespace:** Merges multiple spaces and tabs into a single space.
* **Compresses Newlines:** Limits consecutive newlines to a maximum of 2 (`\n\n`).
* **Trims Boundaries:** Removes trailing/leading spaces on every line and trims the overall payload.
* **Preserves Markdown:** All Markdown tags, headers, and code blocks remain completely intact.

---

## 💬 Chat History Dialog

The **Chat History** dialog (accessible from the top‑right menu **`…`** → **Chat History**) lets you browse and restore past conversations from the local chat logs.

### How it works

- The dialog lists the **last 200 chat sessions** found in the local `.jsonl` log files (up to **50 hourly log files** are scanned).
- Each entry shows the first user message (truncated to 200 characters), a timestamp, and the total number of messages in that session.
- Click **Load** on any session to restore it into the chat window.

### Important details

| Detail | Description |
|---|---|
| **Chat logging must be ON** | The dialog reads from `%LOCALAPPDATA%\LMLocalChat\ChatHistory\`. If **Enable Chat Logging** is turned off in Settings, no sessions will appear. |
| **Loading a session forks it** | When you load a past session, the extension **creates a brand‑new session** initialized with that session's message history. Your continuation is saved under a new session ID — the original session remains untouched. |

---

## 🖼️ Image Attachments (Multimodal Chat)

LMLocal lets you attach images to a message so vision-capable models can "see" screenshots, diagrams, or designs. Paste (`Ctrl+V`) or drag-and-drop an image into the chat input — a thumbnail appears above the field, where you can review or remove it before sending.

### How it works

- Images are sent to the model in the OpenAI multimodal format (`image_url` with a base64 data URL), together with your text prompt.

### ⚠️ Limitations

| Limitation | Details |
|---|---|
| **Vision model required** | The active model must support vision (e.g. LLaVA, Qwen2-VL, GPT-4o, Claude 3+). Otherwise the API returns an error. |
| **Max 3 images per message** | A 4th image is rejected with a warning. |
| **Max ~4 MB per image** | Larger files are rejected. |
| **Allowed formats** | JPEG and PNG only. |
| **Not saved in history** | base64 payloads are **never** written to the `.jsonl` chat log. When a session is restored, images are **not available** — only a placeholder (`[N images attached - not available in this session]`) is shown and sent to the model. |
| **Session restore** | Restoring a session from history forks it into a new session; image attachments from the original session are not recoverable. |
| **Consolidating an exchange loses images** | The "Reset → consolidate last exchange" command rewrites the user message as plain text; attached images are dropped from the in-memory history after consolidation. |
| **History compaction can drop images** | When `EnableHistoryCompaction` is enabled, the early (summarized) portion of a long conversation is stripped of image attachments. |

> [!TIP]
> Images live in memory only for the current session — they won't survive a restart or loading a session from history.

---

## 💡 Smart Workflows & Best Practices


### 🧠 Which model series are worth trying right now?
If you are new to local LLMs and don't know where to start, trying these model families:

* **Qwen 3.x series**
* **Gemma 4.x series**
* **Mistral 3.x series**
* **Nemotron 3.x series**
* **GPT OSS series**

---

### 🤔 Model can't find what you're looking for?

The AI doesn't have a full filesystem index — it relies on the context you provide. If the model fails to find a specific file, class, or code block:

- **Use the "+" button** – Click the `+` button to include the entire active document in the request. This gives the AI immediate access to the current file.
- **Use "Send to LM Local"** – Right-click in the editor and select **"Send to LM Local"**. If you highlight code, it sends only the selection; if nothing is highlighted, it sends the entire active document.
- **Be specific in your prompt** – Mention the exact file name and extension. For example: `"Find the CalculateTotal method in OrderService.cs"` — this gives the AI a clear target, making it easier to locate the relevant code.

---

### 🎯 How can I improve my development plans by hot-swapping models?

LMLocal allows you to switch the active LLM on the fly **without clearing the chat**. You can use this to pass the conversation history from one model to another **sequentially** – each model builds on or critiques the previous output, refining the plan step by step.

It's simple. Just ask one AI model to write a plan, then switch to a different model in the same chat and tell it: "Critique this and update the plan." The new model reads the whole conversation and gives you a fresh perspective, catching edge cases the first one missed.

**Why it works:**  It’s like bringing in two experts one after another. The first drafts a strategy. The second walks into the room, reads the finished document, and says: "Here’s where you went wrong," without ever seeing the first expert’s rough drafts. By swapping models, you collect the best from each perspective — and you never have to copy-paste or start a new chat, because the entire history stays right there.

---

#### 📉 Hitting Context Limits

**Method 1: Use LMLocal's UI cleanup**
If the chat history grows too long during this multi-model review loop and you start hitting token limits, use LMLocal's cleanup feature instead of losing your work:
* Click the **"Clear chat"** button next to the menu.
* Choose **"Summarize and move context"** or **"Consolidate last exchange"** or **Move last prompt and response**. 
* This will automatically compress the entire debate or pull just your final refined plan into a fresh, clean chat session, resetting model context usage back to the baseline.

**Method 2: Externalize state to a file** *(Requires built-in tools enabled)*
* Tell the model: `"Save this finalized plan to docs/plan.md"`.
* Clear the chat to reset the context window.
* In the new clean chat, ask the model to review or implement the plan:
  * To refine: `"Read docs/plan.md, review it, and suggest improvements."`
  * To implement: `"Read docs/plan.md and implement Phase 1."`

---

### 💰 How can I reduce token usage and lower API costs?
Context window accumulation can lead to high API costs or local performance drops. You can optimize your budget by applying these patterns:

* **The "Smart Context Collector" Tiering:** Don't waste your expensive cloud tokens on reading massive files or building initial context. Instead, start the session with a lighter, cheaper model (like *GPT-4o-mini* or a local *Ministral 3.x*) to read your code files, list directories, and pull together the initial workspace data. Once the heavy context is captured and a baseline draft is formed in the history, hot-swap to a premium OpenAI-compatible model (like *DeepSeek V4-Pro* or *GPT-5.5*) to run the high-level analysis and critical edits.
* **Choose Providers with Prompt Caching:** When working in the cloud, pick providers that natively support **Prompt Caching** (like *DeepSeek* or *OpenRouter*). Because LMLocal continuously appends conversation history with each turn, prompt caching can slash your recurring token costs.
* **Offload Context via RAG MCP Servers:** Instead of attaching whole codebases or giant documents directly to the prompt, hook up an external **RAG (Retrieval-Augmented Generation) MCP server**. This allows LMLocal to fetch only the highly relevant code snippets or documentation chunks dynamically when needed. You get full project awareness while keeping your active context window lean and cheap.
* **Use a lightweight project map (no RAG):** Create a context.md file (manually or ask the model to generate it) describing your project structure, main classes, and patterns. Attach it to the first message to give the model a "project map" without attaching the whole codebase. This saves tokens and reduces context size.
* **Enable History Whitespace Cleaning:** Toggle **"Clean whitespace in history"** in the settings. This compresses redundant spaces, tabs, and excess newlines in background turns—slightly reducing context size and saving tokens without altering your rich-text UI.

---

### ⚡ How do I maximize model speed (even with a quality drop)?
When you just need to generate straightforward boilerplate, repetitive CRUD methods, or standard unit tests at maximum speed:
* **Hardware VRAM Optimization:** Choose quantized weights (e.g., `Q4_K_M` GGUF formats) that **fully fit into your GPU VRAM**. As soon as layers spill over into system RAM (CPU fallback), streaming speed drops significantly.
* **Tweak Backend Inference Settings:** Open your server configurations and check the parameters that directly impact processing and generation speed. Pay close attention to:
    * **GPU Offload** & **CPU Thread Pool Size**
    * **Flash Attention** & **Unified KV Cache**
    * **K/V Cache Quantization Type** & **Offload KV Cache to GPU Memory**
    * **Evaluation / Physical Batch Size**
    * **Keep Model in Memory**
* **Drop the Temperature:** Lower your active preset temperature closer to `0.0` or `0.1`. This stops the model from creatively wandering around, forcing it to stream short, direct, and deterministic code structures.

---

## Auto-Completions

Provides fast, **single-line ghost completions** (inline grey text code suggestions) that appear when your cursor is at the end of a line, powered by Fill-in-the-Middle (FIM) prompting on local base models.

**How to Enable:**
Click the **`...`** menu in the extension panel and select **Autocompletions...** to open the configuration window. From there, check the enable box, select your provider, and assign a model.

> ⚠️ **Conflict Warning:** If you have **another autocompletion extension active** (e.g., GitHub Copilot), its suggestions may visually **overlap** with LMLocal’s ghost text. To avoid a confusing double‑suggestion experience, **it is recommend enabling only one autocompletion provider at a time** – either the built‑in LMLocal one or your external tool, but not both simultaneously.

#### Supported Providers & Models

Works with local providers: LM Studio, Ollama, llama.cpp, Jan.

For optimal results, use base models trained natively on Fill-in-the-Middle (FIM) tokens. The following models or series have been verified to work:

*   **Qwen2.5-Coder (1.5B)**
*   **DeepSeek-Coder (1.3B-Base)**
*   **CodeGemma (2B)**
*   **StarCoderBase (1B)**
*   **Refact (1.6B-FIM)**
*   **Stable-Code (3B)**

> 💡 **Tip:** After selecting your provider and model in the settings, you can click the **Test** button to easily verify the connection and ensure the model is responding correctly.

> ⚠️ **Note:** Make sure you are using the **Base** versions of these models rather than the Instruct/Chat versions, as base models are specifically optimized for raw code completion.

---

## Context Menu Commands

LM Local adds two groups of commands to Visual Studio — in the **code editor** context menu
and in the **Solution Explorer** context menu. All commands open the LM Local chat window
(if not already visible) and inject the relevant content.

---

### Editor Context Menu (right-click inside a code file)

| Command | Behavior |
|---|---|
| **Send to LM Local** | Injects the selected text (or the entire file if nothing is selected) into the chat input as a code-fenced markdown block. The prompt is **not** sent automatically — you can edit or add instructions before sending. |
| **LM Local Commands → Review Code** | Auto-sends the selected code with a review prompt: _"Identify potential bugs, security issues, performance problems, and code smells. Provide specific, actionable recommendations."_ Selects the **Review** instruction tab. |
| **LM Local Commands → BugFix Code** | Auto-sends the selected code with a bugfix prompt. Selects the **Bugfix** instruction tab. |
| **LM Local Commands → Add Unit Tests** | Auto-sends the selected code with a test-generation prompt. Selects the **Tests** instruction tab. |
| **LM Local Commands → Add Summary** | Auto-sends the selected code with a prompt to add `///` XML documentation comments. No instruction tab is selected. |
| **LM Local Commands → Improve Code** | Auto-sends the selected code with an improvement prompt focused on performance, readability, and C# best practices. Selects the **Improve** instruction tab. |
| **LM Local Commands → Explain Code** | Auto-sends the selected code with a detailed explanation prompt. Selects the **Explain** instruction tab. |

All editor commands are **disabled** while a chat session is running. If no text is selected, the entire document content is used.

---

### Solution Explorer Context Menu (right-click any node)

| Selection | Command | What Gets Injected |
|---|---|---|
| **1 file** | Send to LM Local | Full file content in a code fence with its absolute path |
| **2–10 files** (Ctrl+Click) | Send to LM Local | Each file's content, up to **200 KB** total |
| **Folder (≤20 files recursively)** | Send to LM Local Folder | Content of all files (recursively through subfolders) |
| **Folder (>20 files recursively)** | Send to LM Local Folder | Hierarchical tree of folders and files (no content) |
| **Project node** | Send to LM Local Project | Hierarchical tree of folders and files (no content) |
| **Solution node** | Send to LM Local Solution | Aggregated tree of all projects |

#### Limits & Safety

- **Max 10 files** with full content per operation  
- **Max 200 KB** total content; excess files appear as `(content truncated)`  
- **Folder with ≤20 files** (recursive count) → file contents are sent  
- **Folder with >20 files** → hierarchical directory tree is sent instead (no content)  
- Directories automatically excluded: `bin`, `obj`, `.vs`, `.git`, `CopilotBaseline`, `node_modules`, `packages`  
- Binary / image files (`.exe`, `.dll`, `.pdb`, `.png`, `.jpg`, `.gif`, etc.) are skipped  
- Button is disabled while a chat session is in progress 

> **Note:** Solution Explorer commands do **not** auto-send — content is injected into the chat input so you can review or add instructions before submitting. Files are read directly from disk and do not need to be open in the editor.

---

## 🌐 Model Context Protocol (MCP) Support

LMLocal supports external tool integration via the **Model Context Protocol (MCP)**. This allows you to hook up custom or third-party servers to give your local AI even more capabilities.

> [!WARNING]
> **Security Notice & Trusted Sources Only**
> * **Trust Infrastructure:** Only connect to MCP servers and URLs that you fully trust or host locally yourself. 
> * **Review Third-Party Tools:** Before enabling a public or third-party MCP endpoint, review its exposed tools and documentation to ensure it does not execute unauthorized commands or compromise sensitive project data.
> * **No Execution Restrictions:** Currently, the extension does not restrict, sandbox, or prompt for manual confirmation when the AI invokes an MCP tool. Connected tools execute automatically when called by the model.

### ⚠️ Scope & Supported Transports
* **Protocol Version:** Compatible with the **MCP `2025-11-25`** specification standard.
* **Tools-Only Support:** LMLocal **exclusively** loads and registers **Tools** exposed by your MCP servers. These are separate from the built‑in tools and are configured independently. Other MCP features like custom *Prompts* or *Resources* are currently ignored and will not be utilized by the assistant.
* **Transports:** Supports both local process-based (stdio) and network-based streamable (http) transports.
* **NOT Supported:** Legacy `sse` (Server-Sent Events) transports are unsupported (no plans).



---

## ⚙️ How to Configure MCP Servers

You can set up and manage connections to external MCP servers directly inside the configuration dialog:

1. Open the **LM Local Chat** tool window.
2. Click the menu icon (**`…`**) in the top-right corner.
3. Select **"MCP Extensions..."** from the dropdown menu.
4. In the dialog:
   - Check **"Enable Model Context Protocol (MCP)"** to turn the feature on.
   - Paste or edit your JSON configuration directly into the built-in text editor.
   - Click the **"Discover Tools"** button to validate your settings and instantly verify connection availability.

The extension saves your settings locally to `%LOCALAPPDATA%\LMLocalChat\mcp.json`.

### 📝 Configuration Examples

You can organize your configuration using either the `servers` or `mcpServers` root keys.

> [!NOTE]
> **LMLocal Custom Extensions**
> The following parameters are custom LMLocal properties and are not part of the official MCP specification:
> - `"disabled"` *(boolean)*: Temporarily deactivates an entire server process or HTTP connection without deleting its configuration block.
> - `"permissions"` *(object)*: Used to mute specific tools discovered on the server.

**Example 1: Public HTTP Server (with Tool Permissions)**

```json
{
  "mcpServers": {
    "microsoft-learn": {
      "type": "http",
      "url": "https://learn.microsoft.com/api/mcp",
      "permissions": {
        "microsoft_code_sample_search": "disable"
      }
    }
  }
}
```

**Example 2: Demonstrates how to configure endpoints requiring a GitHub Personal Access Token (PAT).**
```json
{
  "servers": {
    "github-copilot": {
      "type": "http",
      "url": "https://api.githubcopilot.com/mcp/",
      "headers": {
        "Authorization": "Bearer ghp_your_personal_access_token_here"
      },
      "disabled": false
    }
  }
}
```
**Example 3: Illustrates the required schema structure for connecting local executable-based MCP servers**
```json
{
  "servers": {
    "OmniToolBox": {
      "type": "stdio",
      "command": "C:\\MyMCP\\OmniToolBox.exe"
    }
  }
}
```

---

### 🔗 Developer Resources

Model Context Protocol .NET SDK — Use this official Microsoft SDK to build and compile your own custom MCP servers compatible with LMLocal.
`https://github.com/modelcontextprotocol/csharp-sdk`

---

## 🔧 Troubleshooting

| Issue | Solution |
| :--- | :--- |
| **No model shown** | Ensure a model is fully loaded in the LM Studio "Server" tab. |
| **Connection Error** | Check if the LM Studio Server is **ON** at `http://127.0.0.1:1234`. Click **`↻`** to retry. |
| **UI Lag** | Restart the tool window or check your local machine resources (CPU/GPU). |
| **MCP server not detected** | Verify that MCP is enabled in **MCP Extensions…** dialog, check your JSON configuration syntax, and ensure the server process or URL is accessible. |
| **Autocompletions not showing** | Make sure autocompletions are enabled in **Autocompletions…** dialog, and that you have selected a model that supports Fill‑in‑the‑Middle (FIM) – see the list of verified models in the Auto‑Completions section. |
| **Built‑in tools not being invoked** | Check that the global **Enable built‑in AI tools (read‑only)** and/or **Enable built‑in AI tools (write/modify)** checkboxes are ticked in Settings. Also verify that the specific tool is not disabled in the **Built‑in Tools…** dialog. |
| **Response blocked by content filter** | Rephrase the request. |
| **Response truncated - token limit reached** | Increase Context Length in LM Studio model settings or split the task into smaller parts. |
| **`image_url` variant error** ("unknown variant `image_url`, expected `text`") | The loaded model does **not** support vision/multimodal inputs – it only accepts `text` messages. **Do not include image attachments or `image_url` fields** in your request. If you need image understanding, switch to a multimodal model (e.g., LLaVA, Qwen-VL, or any model with vision capabilities). |
---

## 💾 Data & Configuration

LMLocal keeps things simple and stores your preferences locally. Configuration files are maintained in:

`%LOCALAPPDATA%\LMLocalChat\`

---

## 📜 License & Compliance & Third-Party

- **License:** MIT License. See [LICENSE.txt](./LICENSE.txt) for details.
- **Compliance:** See [COMPLIANCE.md](./COMPLIANCE.md) for EU AI Act and data handling information
- **Components:**
  - `marked` v15.0.12 (MIT)
  - `highlight.js` v11.9.0 (BSD-3-Clause) 
