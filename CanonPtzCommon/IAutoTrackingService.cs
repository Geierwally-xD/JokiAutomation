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

        /// <summary>
        /// Get current PTZ tracking information
        /// </summary>
        Task<TrackInfo> GetTrackInfoAsync();

        /// <summary>
        /// Set the home position for auto tracking recovery
        /// </summary>
        /// <param name="homePosition">Format: "pan,tilt,zoom" e.g. "0,0,1000"</param>
        Task<CommandResult> SetHomePositionAsync(string homePosition);

        /// <summary>
        /// Enable recovery control with specified timeout
        /// </summary>
        /// <param name="recoveryTimeSeconds">Time in seconds before returning to home position (0-600)</param>
        Task<CommandResult> EnableRecoveryControlAsync(int recoveryTimeSeconds);
    }
}
