using System.Threading.Tasks;

namespace CanonPtzCommon
{
    public enum PtzDirection
    {
        Up,
        Down,
        Left,
        Right
    }

    public interface ICanonPtzController
    {
        bool IsConnected { get; }

        Task<CommandResult> ConnectAsync();
        Task<CommandResult> DisconnectAsync();

        Task<CommandResult> StartPanLeftAsync();
        Task<CommandResult> StartPanRightAsync();
        Task<CommandResult> StartTiltUpAsync();
        Task<CommandResult> StartTiltDownAsync();
        Task<CommandResult> StartZoomInAsync();
        Task<CommandResult> StartZoomOutAsync();

        Task<CommandResult> StopPanAsync();
        Task<CommandResult> StopTiltAsync();
        Task<CommandResult> StopZoomAsync();
        Task<CommandResult> StopAllAsync();

        Task<CommandResult> RecallPresetAsync(int presetNumber);
        Task<CommandResult> StorePresetAsync(int presetNumber);

        Task<CommandResult> EnableTrackingSingleAsync();
        Task<CommandResult> EnableTrackingGroupAsync();
        Task<CommandResult> DisableTrackingAsync();

        Task<CameraPosition> GetPositionAsync();
        Task<CameraStatus> GetStatusAsync();

        /// <summary>
        /// Sets the camera standby/idle mode
        /// </summary>
        /// <param name="standby">True to enter standby mode, false to enter idle (active) mode</param>
        /// <returns>CommandResult with success status and details</returns>
        Task<CommandResult> SetStandbyAsync(bool standby);
    }
}
