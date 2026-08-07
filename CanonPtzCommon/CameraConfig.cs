namespace CanonPtzCommon
{
    public sealed class CameraConfig
    {
        public string DeviceName { get; set; } = "Canon_CRN100";
        public string IpAddress { get; set; }
        public int Port { get; set; } = 80;
        public string Username { get; set; }
        public string Password { get; set; }
        public string Protocol { get; set; } = "LegacyAw";
        public bool UseHttps { get; set; }

        // PTZ Speed Settings (XC Protocol)
        public int PanSpeed { get; set; } = 1500;
        public int TiltSpeed { get; set; } = 1500;
        public int ZoomSpeed { get; set; } = 30;
    }
}
