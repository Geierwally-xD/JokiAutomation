using System;
using System.IO;

namespace JokiAutomation
{
    internal class AudioMix
    {
        public void initAudio(Form1 winForm)
        {
            _rasPi.initRasPi(winForm);
            // read in audio profiles
            if (File.Exists(JokiAutomationPath + "Audio.cfg")) // read audio teach file
            {
                using (Stream stream = File.Open(JokiAutomationPath + "Audio.cfg", FileMode.Open))
                {
                    var binaryFormatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
                    audioProfile = (byte[,])binaryFormatter.Deserialize(stream);
                }
            }
            else
            {
                for (int i = 0; i < 9; i++)
                {
                    for (int j = 0; j < 4; j++)
                    {
                        audioProfile[i, j] = 0;
                    }

                }
            }
        }

        //executes audio mix command
        public void executeAudio(int ID)
        {
            _rasPi.rasPiExecute(AM_EXECUTE, ID);
        }

        //teach audio mix profile
        public void teachAudio(int ID)
        {
            int teachSequence = 0;
            using (Stream stream = File.Open(JokiAutomationPath + "Audio.cfg", FileMode.Create))
            {
                var binaryFormatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
                binaryFormatter.Serialize(stream, audioProfile);
            }
            teachSequence |= audioProfile[ID, 0];
            teachSequence |= audioProfile[ID, 1]<<4;
            teachSequence |= audioProfile[ID, 2]<<8;
            teachSequence |= audioProfile[ID, 3]<<12;
            teachSequence |= ID<<16;
            _rasPi.rasPiExecute(AM_TEACH,teachSequence);
        }

        public bool[] channelActive_ = new bool[] {true,true,true,true };
        public const int AM_ACTIVE       = 0x10;
        public const int AM_VOLUME       = 0x20;
        public const int AM_FADEUP       = 0x30;
        public const int AM_FADEDOWN     = 0x40;
        public const int AM_FADESTOP     = 0x50;
        public const int AM_AUDIO_RESET  = 0x60;
        public const int AM_PROFILE      = 0x70; // execute audio profile 
        public RasPi _rasPi = new RasPi();      // Raspberry Pi functionality
        private const int AM_EXECUTE     = 30;  // Audio Mix command
        private const int AM_TEACH       = 31;  // Audio Mix teach profile

        // IDs for command line interpreter
        public const int AM_SEQUENCE = 50;       // command sequence raspberry pi

        public byte[,] audioProfile = new byte[10, 4];
        private string JokiAutomationPath = Environment.GetEnvironmentVariable("JokiAutomation");
    }
}