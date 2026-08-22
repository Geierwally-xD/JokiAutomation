namespace JokiAutomation
{
    /// <summary>
    /// Result of a PTZ/RasPi position move operation.
    /// </summary>
    internal sealed class PositionMoveResult
    {
        public bool   Success       { get; }
        public int    RequestedId   { get; }
        public int    CanonPreset   { get; }
        public string Message       { get; }

        private PositionMoveResult(bool success, int requestedId, int canonPreset, string message)
        {
            Success     = success;
            RequestedId = requestedId;
            CanonPreset = canonPreset;
            Message     = message;
        }

        public static PositionMoveResult Ok(int requestedId, int canonPreset, string message = "OK")
            => new PositionMoveResult(true,  requestedId, canonPreset, message);

        public static PositionMoveResult Fail(int requestedId, string message)
            => new PositionMoveResult(false, requestedId, 0, message);
    }
}
