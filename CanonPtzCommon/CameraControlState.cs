namespace CanonPtzCommon
{
    public sealed class CameraControlState
    {
        public bool HasSession { get; set; }
        public bool HasControl { get; set; }
        public string SessionId { get; set; }

        public bool IsOperational => HasSession && HasControl;

        public override string ToString()
        {
            return $"Session: {HasSession}, Control: {HasControl}, SessionId: {SessionId ?? "none"}";
        }
    }
}
