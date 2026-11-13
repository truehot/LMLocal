using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.Tooling.BuiltInVs.Common;
using Newtonsoft.Json;

namespace LMLocal.Infrastructure.WebView.Controllers
{
    /// <summary>
    /// Bridge class for chat file links. Exposed to the WebView2 page as the "fileLinks" host object.
    /// </summary>
    public interface IFileLinksController
    {
        /// <summary>
        /// json: { "link": "services\\app.data.service.js" } →
        /// { "status": "opened|notFound|ambiguous|error", "path"?, "via"?, "candidates"? }.
        /// </summary>
        Task<string> OpenAsync(string json);
    }

    [ComVisible(true)]
    public class FileLinksController : IFileLinksController
    {
        private readonly IFileLinkResolver _fileLinkResolver;

        internal FileLinksController(IFileLinkResolver fileLinkResolver)
        {
            _fileLinkResolver = fileLinkResolver ?? throw new ArgumentNullException(nameof(fileLinkResolver));
        }

        public async Task<string> OpenAsync(string json)
        {
            try
            {
                var request = string.IsNullOrWhiteSpace(json) ? null : json.FromJson<FileLinkOpenRequest>();
                if (request == null || string.IsNullOrWhiteSpace(request.Link))
                    return new FileLinkOpenResponse { Status = "notFound" }.ToJson();

                var result = await _fileLinkResolver.OpenAsync(request.Link).ConfigureAwait(false);
                return ToResponse(result).ToJson();
            }
            catch (Exception ex)
            {
                InternalLogger.Error("FileLinks.OpenAsync failed", ex);
                return new FileLinkOpenResponse { Status = "error" }.ToJson();
            }
        }

        private static FileLinkOpenResponse ToResponse(FileLinkOpenResult result)
        {
            if (result == null)
                return new FileLinkOpenResponse { Status = "error" };

            switch (result.Status)
            {
                case FileLinkStatus.Opened:
                    return new FileLinkOpenResponse { Status = "opened", Path = result.Path, Via = result.Via };
                case FileLinkStatus.Ambiguous:
                    return new FileLinkOpenResponse { Status = "ambiguous", Candidates = result.Candidates };
                case FileLinkStatus.NotFound:
                    return new FileLinkOpenResponse { Status = "notFound" };
                default:
                    return new FileLinkOpenResponse { Status = "error" };
            }
        }
    }

    internal sealed class FileLinkOpenRequest
    {
        [JsonProperty("link")]
        public string Link { get; set; }
    }

    /// <summary>
    /// Explicit <see cref="JsonPropertyAttribute"/> names are required: <see cref="JsonExtensions"/> uses default
    /// serializer settings, so without them the payload would be PascalCase and null-valued members would be emitted.
    /// </summary>
    internal sealed class FileLinkOpenResponse
    {
        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("path", NullValueHandling = NullValueHandling.Ignore)]
        public string Path { get; set; }

        [JsonProperty("via", NullValueHandling = NullValueHandling.Ignore)]
        public string Via { get; set; }

        [JsonProperty("candidates", NullValueHandling = NullValueHandling.Ignore)]
        public IReadOnlyList<string> Candidates { get; set; }
    }
}
