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
            ["deepseek-v4"] = 1_048_576,
            ["deepseek-v4-pro"] = 1_048_576,
            ["deepseek-v4-flash"] = 1_048_576,

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

            // ========== Google Gemini (2026) ==========
            ["gemini-3.6-flash"] = 1_048_576,
            ["gemini-3.5-flash-cyber"] = 1_048_576,
            ["gemini-3.5-flash-lite"] = 1_048_576,
            ["gemini-3.1-pro"] = 2_000_000,
            ["gemini-3.1-flash"] = 1_048_576,
            ["gemini-3-flash"] = 1_048_576,

            // ========== GLM (Zhipu AI, 2026) ==========
            ["glm-5.2"] = 1_048_576,
            ["glm-5.2-1m"] = 1_048_576,
            ["glm-5.1"] = 200_000,
            ["glm-5"] = 200_000,
            ["glm-4.5"] = 128_000,
            ["glm-4.5-x"] = 128_000,

            // ========== Gemma 4 (Google, 2026) ==========
            ["gemma-4-26b-a4b"] = 256_000,
            ["gemma-4-31b"] = 256_000,
            ["gemma-4-e2b"] = 128_000,
            ["gemma-4-e4b"] = 128_000,

            // ========== OpenAI GPT (2025–2026) ==========
            ["gpt-5.6-sol"] = 1_048_576,
            ["gpt-5.6-terra"] = 1_048_576,
            ["gpt-5.6-luna"] = 1_048_576,
            ["gpt-5.2-codex"] = 1_048_576,
            ["gpt-oss-120b"] = 131_072,
            ["gpt-oss-20b"] = 131_072,

            // ========== Meta Llama 4 (2026) ==========
            ["llama-4-scout"] = 10_000_000,
            ["llama-4-maverick"] = 10_000_000,

            // ========== NVIDIA Nemotron 3 (2025–2026) ==========
            ["nemotron-3-super-120b"] = 1_000_000,
            ["nemotron-3-nano-30b"] = 256_000,
            ["nemotron-3-nano-omni-30b"] = 256_000,

            // ========== Qwen 3.5 (Alibaba, 2026) ==========
            ["qwen3.5-plus"] = 1_000_000,
            ["qwen3.5-flash"] = 1_000_000,
            ["qwen3.5-35b-a3b"] = 262_144,
            ["qwen3.5-2b"] = 262_144,

            // ========== Qwen 3.6 (Alibaba, 2026) ==========
            ["qwen3.6-plus"] = 1_000_000,
            ["qwen3.6-flash"] = 1_000_000,
            ["qwen3.6-35b-a3b"] = 262_144,
            ["qwen3.6-27b"] = 262_144,

            // ========== Additional 2026 models ==========
            ["mistral-large-3"] = 128_000,      // Mistral Large 3
            ["cohere-command-a"] = 128_000,      // Cohere Command A
            ["xai-grok-4.5"] = 1_000_000,    // xAI Grok 4.5
            ["seed-2.1-pro"] = 1_000_000,    // Seed 2.1 Pro
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
