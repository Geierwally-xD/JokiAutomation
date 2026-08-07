using System.Threading.Tasks;

namespace CanonPtzCommon
{
    /// <summary>
    /// Interface for Canon RA-AT001 Auto Tracking control
    /// Uses /cgi-addon/Auto_Tracking_RA-AT001/app_ctrl/ endpoints
    /// </summary>
    public interface IAutoTrackingService
    {
        /// <summary>
        /// Check if auto tracking is currently enabled
        /// </summary>
        Task<bool> IsEnabledAsync();

        /// <summary>
        /// Enable auto tracking
        /// </summary>
        Task<CommandResult> EnableAsync();

        /// <summary>
        /// Disable auto tracking
        /// </summary>
        Task<CommandResult> DisableAsync();

        /// <summary>
        /// Get current auto tracking status and detection info
        /// </summary>
        Task<AutoTrackingStatus> GetStatusAsync();
    }
}
