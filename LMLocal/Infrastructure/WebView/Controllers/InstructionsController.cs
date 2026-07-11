using System;
using System.Threading.Tasks;
using LMLocal.Core.Common;
using LMLocal.Infrastructure.Instructions;

namespace LMLocal.Infrastructure.WebView.Controllers
{
    /// <summary>
    /// Bridge class for communication between WebView2 and backend instructions logic.
    /// </summary>
    public interface IInstructionsController
    {
        Task<string> GetInstructionsAsync();
        Task<bool> UpdateInstructionsAsync(string newInstructionsJson);
        Task<bool> UpdateInstructionsSelectedTabAsync(string selectedTabId);
    }

    [System.Runtime.InteropServices.ComVisible(true)]
    public class InstructionsController : IInstructionsController
    {
        private readonly IInstructionsManager _instructionsManager;

        public InstructionsController(IInstructionsManager instructionsManager)
        {
            _instructionsManager = instructionsManager ?? throw new ArgumentNullException(nameof(instructionsManager));
        }

        public async Task<string> GetInstructionsAsync()
        {
            try
            {
                return await _instructionsManager.GetAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                InternalLogger.Error("GetInstructionsAsync failed", ex);
                return "{}";
            }
        }

        public async Task<bool> UpdateInstructionsAsync(string newInstructionsJson)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(newInstructionsJson))
                {
                    return false;
                }

                await _instructionsManager.UpdateAsync(newInstructionsJson).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                InternalLogger.Error("UpdateInstructionsAsync failed", ex);
                return false;
            }
        }

        public async Task<bool> UpdateInstructionsSelectedTabAsync(string selectedTabId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(selectedTabId))
                {
                    return false;
                }

                await _instructionsManager.UpdateSelectedTabAsync(selectedTabId).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                InternalLogger.Error("UpdateInstructionsSelectedTabAsync failed", ex);
                return false;
            }
        }
    }
}
