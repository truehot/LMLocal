# EU AI Act Compliance for LMLocal

**Last Updated:** August 2026  
**Project:** LMLocal  
**License:** MIT

---

## 1. Project Status and Classification

LMLocal is an open-source orchestration tool (MIT License) that connects to various Large Language Model (LLM) backends (local or cloud). It does **not** develop, train, or distribute any AI models itself.

Under the EU AI Act (Regulation (EU) 2024/1689):

- **Role**: LMLocal is classified as a **provider of an AI system** (an orchestration layer that exposes LLM capabilities to the end user). It is not a provider of GPAI models and does not place models on the market.

- **Risk Level**: The project is classified as **minimal risk**. It does not fall under the prohibited practices (Article 5) and does not constitute a high-risk system within the meaning of Annex III.

- **Open‑Source Exemption**: As an open‑source project released under the MIT License, LMLocal benefits from the component‑level exemption under **Article 2(12)** of the AI Act. This exemption applies to AI systems released under free and open‑source licenses, unless they are high‑risk systems or fall under Article 5 or 50. While this exempts LMLocal from most provider obligations, the **transparency obligations of Article 50** still apply because the system directly interacts with users.

---

## 2. Compliance Checklist

LMLocal voluntarily adheres to the following key obligations:

- **Article 50 (Transparency)**: The user interface clearly indicates interaction with an AI system via the **"AI Assistant"** label in the window title and chat UI.

- **Article 12 (Record‑Keeping)**: LMLocal logs conversations and AI tool actions (file reads/writes, snapshots) locally to enable full auditability.

- **Article 14 (Human Oversight)**: Users can interrupt AI execution at any time (**Stop** button) and review or roll back all file modifications via the **Changes** panel (snapshot + discard functionality).

---

## 3. Data Handling Policy

LMLocal is designed with privacy in mind:

- **Local Backends (LM Studio, Ollama, Jan, Llama.cpp)**: All prompts and data stay on the user's local machine. No information is transmitted externally.

- **Cloud Backends (OpenAI‑compatible, DeepSeek, Gemini, Together AI, etc.)**: Whether selecting a built-in provider or adding a custom one in the settings (or Providers configuration), any non-localhost API base URL will transmit prompts and workspace data to that remote endpoint. The UI explicitly alerts users when entering or using external URLs with an in-place notice ("Sending requests to external endpoints (non-localhost) will transmit your prompts and data to that provider. Make sure you trust their privacy policy."). Users are responsible for reviewing and agreeing to the privacy policies of any third-party endpoints they configure.

- **Telemetry**: LMLocal does **not** collect any telemetry, usage statistics, or personal data.

- **API Keys**: API keys are stored locally in the user settings file and are never transmitted or shared by LMLocal itself. They are sent only to the configured provider's API endpoint (as an `Authorization: Bearer` header) when making requests.

- **GDPR Notice**: LMLocal runs entirely on the client side and does not process, store, or transmit personal data to any central server maintained by the developers. The end‑user or their organization acts as the **Data Controller** under GDPR for any personal data processed through the software. When using cloud backends, users are responsible for establishing a lawful basis for data transfer to the chosen AI provider.

---

## 4. User Responsibilities

By using LMLocal, the end‑user acknowledges and agrees that:

- They are solely responsible for choosing and configuring their AI backend (local or cloud).

- They are responsible for ensuring their use of LMLocal and any connected AI services complies with their organization's policies and applicable data protection laws (e.g., GDPR).

- They are responsible for reviewing the privacy policies of any cloud AI provider before submitting data, including code, prompts, or other potentially sensitive content.

- They understand that LMLocal does not filter or redact code, prompts, or file contents before sending them to cloud providers.

---

## 5. Disclaimer

LMLocal is provided "AS IS", without warranty of any kind, express or implied. The authors and contributors are not liable for any damages or loss arising from the use of this software. This disclaimer does not affect the provider's regulatory obligations under the EU AI Act.