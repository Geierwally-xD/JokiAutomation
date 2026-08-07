namespace CanonPtzCommon
{
    /// <summary>
    /// Status information from Canon RA-AT001 Auto Tracking add-on
    /// </summary>
    public sealed class AutoTrackingStatus
    {
        /// <summary>
        /// Whether auto tracking is enabled
        /// </summary>
        public bool TrackingEnabled { get; set; }

        /// <summary>
        /// Whether auto zoom is enabled
        /// </summary>
        public bool AutoZoomEnabled { get; set; }

        /// <summary>
        /// Whether zoom control is enabled
        /// </summary>
        public bool ZoomControlEnabled { get; set; }

        /// <summary>
        /// Sensitivity level of tracking
        /// </summary>
        public int Sensitivity { get; set; }

        /// <summary>
        /// Target selection mode
        /// </summary>
        public int TargetSelection { get; set; }

        /// <summary>
        /// Whether tracking restart is enabled
        /// </summary>
        public bool TrackingRestartEnabled { get; set; }

        /// <summary>
        /// Whether multi-target assist is supported (may require license/upgrade)
        /// </summary>
        public bool MultiTargetSupported { get; set; }

        /// <summary>
        /// Whether face direction assist is supported (may require license/upgrade)
        /// </summary>
        public bool FaceDirectionSupported { get; set; }

        /// <summary>
        /// Whether sit/stand assist is supported (may require license/upgrade)
        /// </summary>
        public bool SitStandSupported { get; set; }

        /// <summary>
        /// Number of detected subjects
        /// </summary>
        public int DetectionCount { get; set; }

        /// <summary>
        /// Current tracking status code
        /// </summary>
        public int TrackStatus { get; set; }

        /// <summary>
        /// Tracking result code
        /// </summary>
        public int TrackResult { get; set; }

        /// <summary>
        /// Current pan position
        /// </summary>
        public int PanPosition { get; set; }

        /// <summary>
        /// Current tilt position
        /// </summary>
        public int TiltPosition { get; set; }

        /// <summary>
        /// Current zoom position
        /// </summary>
        public int ZoomPosition { get; set; }

        /// <summary>
        /// Target ID being tracked (-1 if none)
        /// </summary>
        public int TargetId { get; set; }

        // Backward compatibility alias
        /// <summary>
        /// Whether auto tracking is enabled (alias for TrackingEnabled)
        /// </summary>
        public bool Enabled
        {
            get => TrackingEnabled;
            set => TrackingEnabled = value;
        }
    }
}
