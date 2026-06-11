using System;
using System.IO;
using System.Net.Http;

namespace LMLocal.Infrastructure.LlmApi.Responses
{
    /// <summary>
    /// Owns the HTTP response and its stream for a streaming chat request.
    /// </summary>
    internal sealed class StreamingResponse : IDisposable
    {
        private readonly HttpResponseMessage _response;
        private readonly HttpRequestMessage _request;
        private readonly StringContent _content;
        private bool _disposed;

        public Stream Stream { get; }

        public StreamingResponse(Stream stream, HttpResponseMessage response, HttpRequestMessage request, StringContent content)
        {
            Stream = stream;
            _response = response;
            _request = request;
            _content = content;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stream.Dispose();
            _response.Dispose();
            _request.Dispose();
            _content.Dispose();
        }
    }
}
