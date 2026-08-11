namespace CanonPtzCommon
{
    /// <summary>
    /// PTZ tracking information from RA-AT001 track_info.cgi
    /// </summary>
    public sealed class TrackInfo
    {
        public int Pan { get; set; }
        public int Tilt { get; set; }
        public int Zoom { get; set; }

        /// <summary>
        /// Converts to home position string format "pan:tilt:zoom" (with colons, not commas)
        /// </summary>
        public string ToPtzHomePosition()
        {
            return $"{Pan}:{Tilt}:{Zoom}";
        }

        public override string ToString()
        {
            return $"Pan={Pan}, Tilt={Tilt}, Zoom={Zoom}";
        }
    }
}