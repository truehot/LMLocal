using System;
using System.Collections.Generic;

namespace LMLocal.Core.Models
{
    /// <summary>
    /// Provides known context lengths for models that don't report them via API.
    /// </summary>
    internal static class KnownModelContexts
    {
        private static readonly Dictionary<string, int> KnownContexts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            // ========== DeepSeek (2026) ==========
            ["deepseek-v4.1-flash"] = 1_048_576,
            ["deepseek-v4.1-flash-1017"] = 1_048_576,
            ["deepseek/deepseek-v4.1-flash"] = 1_048_576,
            ["deepseek-v4"] = 1_048_576,
            ["deepseek-v4-pro"] = 1_048_576,
            ["deepseek-v4-flash"] = 1_048_576,
            ["deepseek-flash"] = 1_048_576,
            ["deepseek-ai/DeepSeek-V4-Flash"] = 1_048_576,
            ["deepseek-ai/DeepSeek-V4-Pro"] = 1_048_576,
            // ========== Kimi (Moonshot AI, 2026) ==========
            ["kimi-k3"] = 1_048_576,
            ["moonshot/kimi-k3"] = 1_048_576,
            ["kimi-k2.7"] = 256_000,
            ["kimi-k2.7-code"] = 256_000,
            ["moonshot/kimi-k2.7"] = 256_000,
            ["moonshot/kimi-k2.7-code"] = 256_000,
            ["kimi-k2.6"] = 262_144,
            ["moonshot/kimi-k2.6"] = 262_144,
            ["kimi-k2.5"] = 256_000,
            ["moonshot/kimi-k2.5"] = 256_000,

            // ========== MiniMax (2026) ==========
            ["minimax-m3"] = 1_000_000,
            ["minimax-m2.7"] = 204_800,
            ["minimax-m2.7-highspeed"] = 204_800,
            ["minimax-m2.5"] = 204_800,
            ["minimax-m2.5-highspeed"] = 204_800,

            ["MiniMaxAI/MiniMax-M3"] = 1_000_000,

            // ========== Google Gemini (2026) ==========
            ["gemini-3.8-flash"] = 1_048_576,
            ["gemini-3.6-flash"] = 1_048_576,
            ["gemini-3.5-flash-cyber"] = 1_048_576,
            ["gemini-3.5-flash-lite"] = 1_048_576,
            ["gemini-3.1-pro"] = 2_000_000,
            ["gemini-3.1-flash"] = 1_048_576,
            ["gemini-3-flash"] = 1_048_576,

            // ========== GLM (Zhipu / Z.ai) ==========
            ["glm-5.3"] = 1_048_576,
            ["glm-5.2"] = 1_048_576,
            ["glm-5.2-1m"] = 1_048_576,
            ["glm-5.1"] = 200_000,
            ["glm-5"] = 200_000,
            ["glm-4.7-flash"] = 198_000,
            ["glm-4.5"] = 128_000,
            ["glm-4.5-x"] = 128_000,

            ["zai-org/GLM-5.3-Flash"] = 1_048_576,

            // ========== Gemma 4 (Google, 2026) ==========
            ["gemma-4-26b-a4b"] = 256_000,
            ["gemma-4-31b"] = 256_000,
            ["gemma-4-12b"] = 256_000,
            ["gemma-4-e2b"] = 128_000,
            ["gemma-4-e4b"] = 128_000,

            // ========== OpenAI GPT (2025–2026) ==========
            ["gpt-6-astra"] = 1_050_000,
            ["gpt-6.1-sol"] = 1_050_000,
            ["gpt-6-luna"] = 1_050_000,
            ["gpt-5.6-sol"] = 1_050_000,
            ["gpt-5.6-terra"] = 1_048_576,
            ["gpt-5.6-luna"] = 1_048_576,
            ["gpt-5.2-codex"] = 1_048_576,
            ["gpt-oss-120b"] = 131_072,
            ["gpt-oss-20b"] = 131_072,

            // ========== Meta Llama 4 (2026) ==========
            ["llama-4-scout"] = 10_000_000,
            ["llama-4-maverick"] = 1_000_000,

            // ========== NVIDIA Nemotron 3 (2025–2026) ==========
            ["nemotron-3-super-120b"] = 1_000_000,
            ["nemotron-3-nano-30b"] = 256_000,
            ["nemotron-3-nano-omni-30b"] = 256_000,

            // ========== Qwen (Alibaba, 2026) ==========
            ["qwen3.5-plus"] = 1_000_000,
            ["qwen3.5-flash"] = 1_000_000,
            ["qwen3.5-35b-a3b"] = 262_144,
            ["qwen3.5-2b"] = 262_144,

            ["qwen3.6-plus"] = 1_000_000,
            ["qwen3.6-flash"] = 1_000_000,
            ["qwen3.6-35b-a3b"] = 262_144,
            ["qwen3.6-27b"] = 262_144,

            ["qwen3.7-plus"] = 1_000_000,
            ["qwen3.7-flash"] = 1_000_000,
            ["qwen3.7-35b-a3b"] = 262_144,
            ["qwen3.7-27b"] = 262_144,

            ["Qwen/Qwen3.8-Flash"] = 1_000_000,
            ["qwen3.8-flash"] = 1_000_000,

            ["qwen3-coder-480b-a35b"] = 262_144,
            ["qwen3-coder-next"] = 262_144,
            ["qwen3-coder-30b"] = 262_144,

            // ========== Xiaomi MiMo ==========
            ["mimo-v2.5-pro"] = 1_000_000,

            // ========== xAI Grok (coding-focused) ==========
            ["grok-code-fast-1"] = 256_000,
            ["grok-build-0.1"] = 256_000,
            ["grok-4.3"] = 1_000_000,
            ["grok-4.20"] = 2_000_000,

            // ========== Anthropic Claude (2026) ==========
            ["claude-fable-5-1"] = 1_000_000,
            ["claude-opus-5-5"] = 1_000_000,
            ["claude-sonnet-5-5"] = 1_000_000,
            ["claude-haiku-5-5"] = 1_000_000,
            ["claude-haiku-4-5"] = 200_000,

            // ========== Additional 2026 models ==========
            ["mistral-large-3"] = 128_000,
            ["cohere-command-a"] = 128_000,
            ["xai-grok-4.5"] = 1_000_000,
            ["seed-2.1-pro"] = 1_000_000,
        };

        /// <summary>
        /// Attempts to get a known context length for the given model ID.
        /// </summary>
        internal static int? TryGet(string modelId)
        {
            if (string.IsNullOrEmpty(modelId))
                return null;

            // 1. Exact match
            if (KnownContexts.TryGetValue(modelId, out var context))
                return context;

            // 2. Try to get the last part after '/'
            var lastSlash = modelId.LastIndexOf('/');
            if (lastSlash >= 0 && lastSlash < modelId.Length - 1)
            {
                var shortId = modelId.Substring(lastSlash + 1);
                if (!string.Equals(shortId, modelId, StringComparison.OrdinalIgnoreCase) &&
                    KnownContexts.TryGetValue(shortId, out context))
                    return context;
            }

            return null;
        }
    }
}
