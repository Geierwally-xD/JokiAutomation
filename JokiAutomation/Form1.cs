using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
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
            listBox1.SelectedIndex = 0;
            listBox2.SelectedIndex = 0;
            listBox3.SelectedIndex = 0;
            listBox4.SelectedIndex = 0;
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
                else if (cmd == "BEAMER_PPP")  // switch Beamer to HDMI 1 (PPP View)
                {
                    _audioMix._rasPi.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_BEAMER_HDMI_1);
                }
                else if (cmd == "BEAMER_LiveStream") // switch Beamer to HDMI 2 (live stream View)
                {
                    _audioMix._rasPi.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_BEAMER_HDMI_2);
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
                else if (cmd == "Ausschaltsequenz") // switch Beamer, HDMI switch, Backuprecorder off and shut down Raspberry Pi
                {
                    _audioMix._rasPi.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_SHUTDOWN);
                }
                else
                {
                    throw new System.ArgumentException(" ", "original");
                }
            }
            catch (Exception)
            {
                MessageBox.Show("JokiAutomation\nFormatfehler in Kommandozeilenaufruf");
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
            _infraredControl.teachIR(listBox2.SelectedIndex);
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
            if(TabControl1.SelectedIndex == 2) // audiomix page active?
            {
                int commandID = AudioMix.AM_ACTIVE + 0x03;
                _audioMix.executeAudio(commandID); // activate audio channels 1 and 2 
            }
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
            _audioMix._rasPi.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_RESET);
        }

        private void button14_Click(object sender, EventArgs e)
        {
            _audioMix._rasPi.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_RESET);
        }

        private void button15_Click(object sender, EventArgs e)
        {
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
            _audioMix.teachAudio(listBox4.SelectedIndex);
        }

        private EventTimer _eventTimer = new EventTimer();
        private AudioMix _audioMix = new AudioMix();
        private InfraredControl _infraredControl = new InfraredControl();
        public  LogData _logDat = new LogData();
        private bool CI_test_active_ = false;

    }


}
