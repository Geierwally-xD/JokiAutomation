namespace JokiAutomation
{
    internal class AudioMix
    {
        public void initAudio(Form1 winForm)
        {
            _rasPi.initRasPi(winForm);
        }
        private const int AM_EXECUTE = 30;// Audio Mix command
        private RasPi _rasPi = new RasPi(); // Raspberry Pi functionality
    }
}