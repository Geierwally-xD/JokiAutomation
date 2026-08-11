namespace CanonPtzCommon
{
    public sealed class CameraConfig
    {
        public string DeviceName { get; set; }
        public string IpAddress { get; set; }
        public int Port { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string Protocol { get; set; }
        public bool UseHttps { get; set; }
        public int PanSpeed { get; set; }
        public int TiltSpeed { get; set; }
        public int ZoomSpeed { get; set; }

        // AutoTracking configuration
        public int AutoTrackingHomePreset { get; set; }
        public int AutoTrackingStartupDelayMs { get; set; }
        public int AutoTrackingRecoveryTimeSeconds { get; set; }
        
        /// <summary>
        /// Optional: Stored home position in format "pan:tilt:zoom" (e.g., "0:0:1000")
        /// If configured, this will be used instead of AutoTrackingHomePreset
        /// </summary>
        public string AutoTrackingHomePosition { get; set; }
    }
}
