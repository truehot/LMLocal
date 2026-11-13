using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using LMLocal.Core.Models;

namespace LMLocal.Application.Abstractions.Ports
{
    /// <summary>
    /// Parses an SSE response stream.
    /// </summary>
    internal interface IStreamProcessor
    {
        Task<StreamCompletionResult> ProcessStreamAsync(Stream stream, CancellationToken cancellationToken, Func<TextStreamChunk, TokenGenerationStats, Task> onChunk, int batchIntervalMs = 50);
    }
}
