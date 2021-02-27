using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;



namespace JokiAutomation
{

    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
            _logDat.initLogData(this);
            _infraredControl.initIR(this);
            _audioMix.initAudio(this);
            _positionControl.initPC(this);
            _Inputtimer.Interval = 1000;  // check rich text box each 1000ms
            _Inputtimer.Tick += new System.EventHandler(Inputtimer_Elapsed);
            listBox1.SelectedIndex = 0;
            listBox2.SelectedIndex = 0;
            listBox3.SelectedIndex = 0;
            listBox4.SelectedIndex = 0;
            listBoxCamPosControl.SelectedIndex = 0;
            trackBar1.Value = _audioMix.audioProfile[listBox4.SelectedIndex, 0];
            trackBar2.Value = _audioMix.audioProfile[listBox4.SelectedIndex, 1];
            trackBar3.Value = _audioMix.audioProfile[listBox4.SelectedIndex, 2];
            trackBar4.Value = _audioMix.audioProfile[listBox4.SelectedIndex, 3];
        }

        // interprets command line arguments 

        public void CommandInterpreter(string[] commands)
        {
            try
            {
                string cmd = commands[1];
                if ((cmd == "Pause") && (commands.Length == 4))
                {
                    _audioMix._rasPi.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_PAUSE);
                    _eventTimer.sendPause("\""+commands[2]+"\"", "\"" + commands[3]+"\""); // start slide show
                }
                else if (cmd == "Timer")
                {
                    _audioMix._rasPi.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_TIMER);
                    _eventTimer.sendEventTime("\"" + commands[2] + "\"");                // start event timer
                }
                else if (cmd == "Band")
                {
                    _audioMix._rasPi.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_PPP_VIEW);
                }
                else if (cmd == "Text")
                {
                    _audioMix._rasPi.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_TEXT_VIEW); // 20 power point view with audio profile text
                }
                else if (cmd == "GoPro")
                {
                    _audioMix._rasPi.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_GOPRO_VIEW);
                }
                else if (cmd == "Altar")
                {
                    _audioMix._rasPi.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_POSCAM_VIEW);
                }
                else if (cmd == "Predigt")
                {
                    _audioMix._rasPi.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_PREACHER_VIEW);
                }
                else if (cmd == "Gebet")
                {
                    _audioMix._rasPi.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_PRAYER_VIEW);
                }
                else if (cmd == "LesungMulti")
                {
                    _audioMix._rasPi.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_READER_VIEW);
                }
                else if (cmd == "BandMulti")
                {
                    _audioMix._rasPi.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_SONG_VIEW);
                }
                else if (cmd == "BEAMER_LiveVideo")  // switch HDMI laptop and audio to VideoClip (laptop out)
                {
                    _audioMix._rasPi.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_LIVE_VIDEO);
                }
                else if (cmd == "BEAMER_LiveStream") // toggle Beamer between HDMI 1 (only PPP) and HDMI 2 (live stream View)
                {
                    _audioMix._rasPi.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_BEAMER_TOGGLE);
                }
                else if (cmd == "BEAMER_VideoClip") // switch Beamer to analog input (video from CD)
                {
                    _audioMix._rasPi.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_BEAMER_ANALOG);
                    _eventTimer.sendPause("\"" + commands[2] + "\"", "\"" + commands[3] + "\""); // start slide show
                }
                else if (cmd == "BEAMER_Mute")   // mute / demute Beamer
                {
                    _audioMix._rasPi.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_BEAMER_MUTE);
                }
                else if (cmd == "Backup_Start")   // starts the backup recorder
                {
                    _audioMix._rasPi.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_START_BACKUP); // 18 starts the backup - recorder
                }
                else if (cmd == "Backup_Stop")   // stopps the backup recorder
                {
                    _audioMix._rasPi.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_STOP_BACKUP);
                }
                else if (cmd == "Ausschaltsequenz") // switch Beamer, HDMI switch, Backuprecorder off and shut down Raspberry Pi
                {
                    _audioMix._rasPi.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_SHUTDOWN);
                }
                else if (cmd == "RasPi_Reset")   // kills all activ RasPiAutomation processes on raspberry pi
                {
                    _audioMix._rasPi.rasPiStop();
                }
                /*
                 *call position control sequence 
                 * param 2 must be equal to Camcorder position text in listbox= eg. "Taufstein" 
                 * param 3 A,G,K,L followed by sound profile D,G,P,T,B 
                 */
                else if ((cmd == "PositionControl") && (commands.Length == 4))
                {
                    _positionControl.sequence(commands[2], commands[3]);
                }
                else
                {
                    throw new System.ArgumentException(" ", "original");
                }
            }
            catch (Exception e)
            {
                _logDat.sendInfoMessage("JokiAutomation\nFormatfehler in Kommandozeilenaufruf\n" + e.Message);
            }
        }


        // eventhandler Start button, start timer or pause slide show depending on selected listbox item
        private void button1_Click(object sender, EventArgs e)
        {
            int commandID = AudioMix.AM_ACTIVE + 0x03;
            _audioMix.executeAudio(commandID); // activate audio channels 1 and 2 
            if (this.listBox1.Text == "Pause")
            {
                _eventTimer.sendPause("\"" + textBox1.Text +"\"" , "\"" + textBox2.Text+"\"" );
                _audioMix._rasPi.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_PAUSE);
            }
            else
            {
                string eventTime = dateTimePicker1.Value.TimeOfDay.Hours.ToString("00") + ":" + dateTimePicker1.Value.TimeOfDay.Minutes.ToString("00");
                _eventTimer.sendEventTime(eventTime);
                _audioMix._rasPi.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_TIMER);
            }
        }


        // eventhandler Start button InfraredControl
        private void button2_Click(object sender, EventArgs e)
        {
            _infraredControl.executeIR(listBox2.SelectedIndex);
        }

        // eventhandler Teach buton InfraredControl
        private void button3_Click(object sender, EventArgs e)
        {
            if (loginUser("Admin") == true) // admin login necessary
            {
                _infraredControl.teachIR(listBox2.SelectedIndex);
            }
            else
            {
                _requestedFunction = 3;
            }
        }

        // buttonhandler fade down <<< AudioControl
        private void button7_Click(object sender, EventArgs e)
        {
            int commandID = AudioMix.AM_FADEDOWN;
            if (_audioMix.channelActive_[listBox3.SelectedIndex] == true)
            {
                commandID += 1 << listBox3.SelectedIndex;
                _audioMix.executeAudio(commandID);
            }
        }
        // buttonhandler fade up >>>  AudioControl
        private void button8_Click(object sender, EventArgs e)
        {
            int commandID = AudioMix.AM_FADEUP;
            if (_audioMix.channelActive_[listBox3.SelectedIndex] == true)
            {
                commandID += 1 << listBox3.SelectedIndex;
                _audioMix.executeAudio(commandID);
            }
        }
        // buttonhandler activate Audiochannel Audiocontrol  
        private void button4_Click(object sender, EventArgs e)
        {
            _audioMix.channelActive_[listBox3.SelectedIndex] = !_audioMix.channelActive_[listBox3.SelectedIndex];
            if (_audioMix.channelActive_[listBox3.SelectedIndex] == true)
            {
                button4.BackColor = Color.Green;
            }
            else
            {
                button4.BackColor = Color.Red;
            }
        }

        // buttonhandler init Audiocontrol  enables activated audiochannels and sets volume to maximum  
        private void button5_Click(object sender, EventArgs e)
        {
            int commandID = AudioMix.AM_ACTIVE;
            for(int i = 0; i<4; i++ ) // add active channels to ID
            {
                if (_audioMix.channelActive_[i] == true)
                {
                    commandID += 1<<i;
                }
            }
            _audioMix.executeAudio(commandID);
        }

        // buttonhandler reset Audiocontrol resets volume, fader and mutes all audio channels
        private void button6_Click(object sender, EventArgs e)
        {
            _audioMix.executeAudio(AudioMix.AM_AUDIO_RESET);
        }

        // active channel listbox index changed
        private void listBox3_SelectedIndexChanged(object sender, EventArgs e)
        {
           if( _audioMix.channelActive_[listBox3.SelectedIndex] == true)
            {
                button4.BackColor = Color.Green;
            }
            else
            {
                button4.BackColor = Color.Red;
            }
        }

        // execute Audio - profile
        private void button9_Click(object sender, EventArgs e)
        {
            int commandID = AudioMix.AM_PROFILE + listBox4.SelectedIndex;
            _audioMix.executeAudio(commandID);
        }

        // tab control index changed initialize Audiomix for channel 1 and 2 if page opens
        private void TabControl1_SelectedIndexChanged(Object sender, EventArgs e)
        {
            //nothing to doe here
        }

        // reset raspberry pi 1 stopps RaspiAutomation App on raspberry from audiomix menu page
        private void button11_Click(object sender, EventArgs e)
        {
            _audioMix._rasPi.rasPiStop();
        }
        // reset raspberry pi 2 stopps RaspiAutomation App on raspberry from infrared menu page
        private void button10_Click(object sender, EventArgs e)
        {
            _audioMix._rasPi.rasPiStop();
        }
        // button handler start/stop sequencer test
        private void button12_Click(object sender, EventArgs e)
        {
            CI_test_active_ = !CI_test_active_; // start stop test IR sequencer
            if(CI_test_active_)
            {
                button12.BackColor = Color.Green;
            }
            else
            {
                button12.BackColor = Color.Transparent;
            }
            _infraredControl.IRTest(CI_test_active_);
        }
        // button handler reset
        private void button13_Click(object sender, EventArgs e)
        {
            _audioMix._rasPi.rasPiStop();
            Thread.Sleep(1000);
            _audioMix._rasPi.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_RESET);
        }

        private void button14_Click(object sender, EventArgs e)
        {
            _audioMix._rasPi.rasPiStop();
            Thread.Sleep(1000);
            _audioMix._rasPi.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_RESET);
        }

        private void button15_Click(object sender, EventArgs e)
        {
            _audioMix._rasPi.rasPiStop();
            Thread.Sleep(1000);
            _audioMix._rasPi.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_RESET);
        }
        //slider Laptop audio
        private void trackBar1_Scroll(object sender, EventArgs e)
        {
            _audioMix.audioProfile[listBox4.SelectedIndex, 0] = (byte)trackBar1.Value;
        }
        //slider sumary signal amplifier audio
        private void trackBar2_Scroll(object sender, EventArgs e)
        {
            _audioMix.audioProfile[listBox4.SelectedIndex, 1] = (byte)trackBar2.Value;
        }

        //slider room microphone audio
        private void trackBar3_Scroll(object sender, EventArgs e)
        {
            _audioMix.audioProfile[listBox4.SelectedIndex, 2] = (byte)trackBar3.Value;
        }

        //slider channel 4 adio
        private void trackBar4_Scroll(object sender, EventArgs e)
        {
            _audioMix.audioProfile[listBox4.SelectedIndex, 3] = (byte)trackBar4.Value;
        }

        // change audio profile depending listbox index
        private void listBox4_SelectedIndexChanged(object sender, EventArgs e)
        {
            trackBar1.Value = _audioMix.audioProfile[listBox4.SelectedIndex, 0];
            trackBar2.Value = _audioMix.audioProfile[listBox4.SelectedIndex, 1];
            trackBar3.Value = _audioMix.audioProfile[listBox4.SelectedIndex, 2];
            trackBar4.Value = _audioMix.audioProfile[listBox4.SelectedIndex, 3];
        }

        // eventhandler audio teach
        private void button16_Click(object sender, EventArgs e)
        {
            if (loginUser("SuperUser") == true) // super user login necessary
            {
                _audioMix.teachAudio(listBox4.SelectedIndex);
            }
            else
            {
                _requestedFunction = 2;
            }
        }

        // eventhandler position control calibrate
        private void calibrate_Click(object sender, EventArgs e)
        {
            _requestedFunction = 0;
            richTextBox3.Clear();
            if (loginUser("Admin") == true) // super user login necessary
            {
                _positionControl.calibratePC(0);
            }
            else
            {
                _requestedFunction = 1; 
            }
        }
        // eventhandler reset button position control 
        private void resetCamPos_Click(object sender, EventArgs e)
        {
            _requestedFunction = 0;
            _audioMix._rasPi.rasPiStop();
            Thread.Sleep(1000);
            _audioMix._rasPi.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_RESET);
        }

        private bool loginUser(string userString)
        {
            bool retVal = false;
            _requestedUser = userString;
            if ((_User == _requestedUser) ||(_User == "Admin"))
            {
                retVal = true;
            }
            else
            {
                _logDat.sendInfoMessage("Für diese Funktion ist ein " + _requestedUser + " login erforderlich, bitte Passwort in Textbox eingeben\nPasswort: ");
                _InputTimerloop = 0;
                _Inputtimer.Start();
            }

            return retVal;
        }

        // event handler Input timer elapsed for user handling
        private void Inputtimer_Elapsed(object sender, System.EventArgs e)
        {
            string userString = null;
            if(++_InputTimerloop < 30) // 30 seconds time for log in
            {
                _Inputtimer.Start();
                switch(TabControl1.SelectedIndex)
                {
                    case 1:
                        userString = richTextBox1.Text;
                        richTextBox1.Select(richTextBox1.Text.Length - 1, 0);
                        richTextBox1.ScrollToCaret();
                    break;
                    case 2:
                        userString = richTextBox2.Text;
                        richTextBox2.Select(richTextBox2.Text.Length - 1, 0);
                        richTextBox2.ScrollToCaret();
                        break;
                    case 3:
                        userString = richTextBox3.Text;
                        richTextBox3.Select(richTextBox3.Text.Length - 1, 0);
                        richTextBox3.ScrollToCaret();
                        break;
                }
                if(userString.Contains("6691ikoJ"))
                {
                    _User = "Admin";
                }
                else if(userString.Contains("Joki1966"))
                {
                    _User = "SuperUser";
                }
                else
                {
                    _User = "DefaultUser";
                }
            }
            else
            {
                _Inputtimer.Stop();
                _logDat.sendInfoMessage("Passwort falsch!!! Funktion nicht möglich. Für diese Funktion ist ein " + _requestedUser + " login erforderlich");
                _requestedFunction = 0;
            }

            if ((_requestedUser == _User)||(_User == "Admin"))
            {
                _Inputtimer.Stop();
                switch (TabControl1.SelectedIndex)
                {
                    case 1:
                        richTextBox1.Clear();
                    break;
                    case 2:
                        richTextBox2.Clear();
                        break;
                    case 3:
                        richTextBox3.Clear();
                        break;
                }
                switch (_requestedFunction)
                {
                    case 1:
                        _positionControl.calibratePC(0);                   // calibrate magnetometer of position control
                        _requestedFunction = 0;
                    break;
                    case 2:
                        _audioMix.teachAudio(listBox4.SelectedIndex);      // teach selected audio profile
                        _requestedFunction = 0;
                    break;
                    case 3:
                        _infraredControl.teachIR(listBox2.SelectedIndex);  // teach selected infrared sequence
                        _requestedFunction = 0;
                    break;
                    case 4:
                        _positionControl.teachPos(listBoxCamPosControl.SelectedIndex); // teach selected camcorder position
                    break;
                    case 5:
                        _positionControl.teachPos(20);  // teach park position camcorder
                        _requestedFunction = 0;
                    break;
                    case 6:
                        _positionControl.teachPos(21);  // teach null position camcorder
                        _requestedFunction = 0;
                    break;
                }
            }
        }
 
        // teach selected position of camcorder; this method needs super user log in
        private void teachCamPos_Click(object sender, EventArgs e)
        {
            richTextBox3.Clear();
            _infraredControl.executeIR(2);      // switch HDMI to camcorder with position control
            _requestedFunction = 4;
            if (loginUser("SuperUser") == true) // super user login necessary
            {
                _positionControl.teachPos(listBoxCamPosControl.SelectedIndex);
            }
        }

        // teach park position of camcorder; this method needs administrator log in
        private void teachParkPos_Click(object sender, EventArgs e)
        {
            richTextBox3.Clear();
            _infraredControl.executeIR(2);      // switch HDMI to camcorder with position control
            _requestedFunction = 5;
            if (loginUser("Admin") == true)     // admin login necessary
            {
                _positionControl.teachPos(20);  // teach park position camcorder
            }
        }
        
        // move camcorder to selected position
        private void moveCamPos_Click(object sender, EventArgs e)
        {
            _requestedFunction = 0;
            _infraredControl.executeIR(2);      // switch HDMI to camcorder with position control
            _positionControl.moveToPos(listBoxCamPosControl.SelectedIndex);
        }

        // key pressed event handler checks <ENTER> pressed for take over position description for position control
        void rtb3KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (_requestedFunction == 4) // teach position take over position text
                {
                    // write adapted position text into listbox 
                    listBoxCamPosControl.Items[listBoxCamPosControl.SelectedIndex] = richTextBox3.Lines[richTextBox3.Lines.Length - 1];
                    _positionControl.writeConfigFile();
                    _requestedFunction = 0;
                }
            }
        }

        private void listBoxCamPosControl_SelectedIndexChanged(object sender, EventArgs e)
        {
            _requestedFunction = 0;
        }

        // eventhandler teach null position
        private void teachNullPos_Click(object sender, EventArgs e)
        {
            richTextBox3.Clear();
            _infraredControl.executeIR(2);      // switch HDMI to camcorder with position control
            _requestedFunction = 6;
            if (loginUser("Admin") == true)     // admin login necessary
            {
                _positionControl.teachPos(21);  // teach null position camcorder
            }
        }
        // eventhandler move up
        private void moveUpHandler(object sender, EventArgs e)
        {
            _positionControl.moveButtonPressed(PositionControl.PC_BUTTON_UP);
        }
        // eventhandler move down
        private void moveDownHandler(object sender, EventArgs e)
        {
            _positionControl.moveButtonPressed(PositionControl.PC_BUTTON_DOWN);
        }
        // eventhandler move left
        private void moveLeftHandler(object sender, EventArgs e)
        {
            _positionControl.moveButtonPressed(PositionControl.PC_BUTTON_LEFT);
        }
        // eventhandler move right
        private void moveRightHandler(object sender, EventArgs e)
        {
            _positionControl.moveButtonPressed(PositionControl.PC_BUTTON_RIGHT);
        }
        // eventhandler move button released handler
        private void moveDoneHandler(object sender, EventArgs e)
        {
            _positionControl.moveButtonPressed(PositionControl.PC_BUTTON_RELEASED);
        }

        private EventTimer _eventTimer = new EventTimer();
        private AudioMix _audioMix = new AudioMix();
        private InfraredControl _infraredControl = new InfraredControl();
        private PositionControl _positionControl = new PositionControl();
        private string _User = "DefaultUser"; // set user
        private string _requestedUser = "DefaultUser"; // requestet user
        private  static uint _requestedFunction = 0;   
        private System.Windows.Forms.Timer _Inputtimer = new System.Windows.Forms.Timer(); // input sequence of password 
        private uint _InputTimerloop = 0;
        public  LogData _logDat = new LogData();
        private bool CI_test_active_ = false;
    }


}
