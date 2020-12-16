using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
//using System.Threading.Tasks;
using System.Windows.Forms;



namespace JokiAutomation
{
    class InfraredControl
    {
        //initialize components of infrared interface
        public void initIR(Form1 winForm)
        {
            _rasPi.initRasPi(winForm);
            _IRForm = winForm;
            this.IRTesttimer.Interval = 6000;
            this.IRTesttimer.Tick += new System.EventHandler(this.IRTesttimer_Elapsed);
        }

        //executes infrared sequence
        public void executeIR (int ID) 
        {
            _rasPi.rasPiExecute(IR_EXECUTE,ID);
        }

        //teach infrared sequence
        public void teachIR (int ID)
        {
            _rasPi.rasPiExecute(IR_TEACH, ID);
        }

        //start / stop IR sequencer test
        public void IRTest(bool active)
        {
            if (active == true)
            {
                _IRtestActive = 1;
                IRTesttimer.Start();
            }
            else
            {
                IRTesttimer.Stop();
                _IRtestActive = 0;
            }
        }

        private void IRTesttimer_Elapsed(object sender, System.EventArgs e)
        {
            IRTesttimer.Start();
            switch(_IRtestActive)
            {
                case 1:
                    _rasPi.rasPiExecute(IR_SEQUENCE, IR_PPP_VIEW);
                    _IRtestActive++;
                break;
                case 2:
                    _rasPi.rasPiExecute(IR_SEQUENCE, IR_GOPRO_VIEW);
                    _IRtestActive++;
                break;
                case 3:
                    _rasPi.rasPiExecute(IR_SEQUENCE, IR_POSCAM_VIEW);
                    _IRtestActive++;
                break;
                case 4:
                    _rasPi.rasPiExecute(IR_SEQUENCE, IR_PREACHER_VIEW);
                    _IRtestActive++;
                break;
                case 5:
                    _rasPi.rasPiExecute(IR_SEQUENCE, IR_PRAYER_VIEW);
                    _IRtestActive = 1;
                break;
            }
        }

        private static RasPi _rasPi = new RasPi(); // Raspberry Pi functionality
        private static Form1 _IRForm;
        private System.Windows.Forms.Timer IRTesttimer = new System.Windows.Forms.Timer(); // test sequence timer 3 seconds
        private int _IRtestActive = 0;    // IR sequencer test step 0 = off
        private const int IR_EXECUTE = 10;// IR control command
        private const int IR_TEACH = 11;  // IR teach command
        public const int IR_SEQUENCE = 50;    // command sequence raspberry pi
        public const int IR_PAUSE = 1;        // pause sequence
        public const int IR_TIMER = 2;        // timer sequence
        public const int IR_PPP_VIEW = 3;     // power point view
        public const int IR_GOPRO_VIEW = 4;   // gopro action cam view
        public const int IR_POSCAM_VIEW = 5;  // camcorder with position control view
        public const int IR_PREACHER_VIEW = 6;// camcorder 2 control view
        public const int IR_PRAYER_VIEW = 7;  // combination ppp view with gopro action cam view
        public const int IR_RESET = 11;       // reset audio - to sumary signal and IR to laptop view
        public const int IR_BEAMER_HDMI_1 = 12; // switch Beamer to HDMI 1 (PPP View)
        public const int IR_BEAMER_HDMI_2 = 13; // switch Beamer to HDMI 2 (live stream View)
        public const int IR_BEAMER_ANALOG = 14; // switch Beamer to analog input (video from CD)
        public const int IR_SHUTDOWN = 15;      // switch Beamer, HDMI Switch and Backuprecorder off and shut down Raspberry Pi
        public const int IR_BEAMER_MUTE = 16;   // mute / demute Beamer
        public const int IR_STOP_BACKUP = 17;   // stopps the backup - recorder
    }
}
