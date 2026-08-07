namespace CanonPtzCommon
{
    public sealed class CameraStatus
    {
        public string PanStatus { get; set; }
        public string TiltStatus { get; set; }
        public string ZoomStatus { get; set; }
        public bool IsMoving { get; set; }

        public override string ToString()
        {
            string status = IsMoving ? "MOVING" : "IDLE";
            return $"Status: {status} (Pan: {PanStatus}, Tilt: {TiltStatus}, Zoom: {ZoomStatus})";
        }
    }
}
