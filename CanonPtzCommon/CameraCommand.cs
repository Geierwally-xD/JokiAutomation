namespace CanonPtzCommon
{
    public enum CameraCommand
    {
        // Session
        SessionOpen,
        SessionClose,
        SessionClaim,
        SessionYield,

        // PTZ Movement
        PanLeft,
        PanRight,
        TiltUp,
        TiltDown,
        ZoomIn,
        ZoomOut,
        StopPan,
        StopTilt,
        StopZoom,
        StopAll,

        // Presets
        RecallPreset,
        StorePreset,

        // Tracking
        TrackingSingle,
        TrackingGroup,
        TrackingOff,

        // Status
        GetPosition,
        GetStatus
    }
}
