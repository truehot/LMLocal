namespace LMLocal.Models
{
    public class SetAiToolsRequest
    {
        /// <summary>
        /// Target mode: "none", "readonly", or "readwrite".
        /// </summary>
        public string Mode { get; set; }
    }
}
