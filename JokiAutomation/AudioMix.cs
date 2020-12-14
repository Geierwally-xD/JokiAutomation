namespace JokiAutomation
{
    internal class AudioMix
    {
        public void initAudio(Form1 winForm)
        {
            _rasPi.initRasPi(winForm);
        }

        //executes audio mix command
        public void executeAudio(int ID)
        {
            _rasPi.rasPiExecute(AM_EXECUTE, ID);
        }

        public bool[] channelActive_ = new bool[] {true,true,false,false };
        public const int AM_ACTIVE       = 0x10;
        public const int AM_VOLUME       = 0x20;
        public const int AM_FADEUP       = 0x30;
        public const int AM_FADEDOWN     = 0x40;
        public const int AM_FADESTOP     = 0x50;
        public const int AM_AUDIO_RESET  = 0x60;
        public RasPi _rasPi = new RasPi();      // Raspberry Pi functionality
        private const int AM_EXECUTE     = 30;   // Audio Mix command

        // IDs for command line interpreter
        public const int AM_AUDIO_SUMARY = 8;    // audioswitch activate sumary signal(channel 2)
        public const int AM_AUDIO_INPUT_3 = 9;   // audioswitch activate channel 3
        public const int AM_AUDIO_INPUT_4 = 10;  // audioswitch activate channel 4
        public const int AM_SEQUENCE = 50;       // command sequence raspberry pi
    }
}