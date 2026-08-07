namespace CanonPtzCommon
{
    public sealed class CameraPosition
    {
        public int Pan { get; set; }
        public int Tilt { get; set; }
        public int Zoom { get; set; }

        public override string ToString()
        {
            return $"PAN: {Pan}, TILT: {Tilt}, ZOOM: {Zoom}";
        }
    }
}
