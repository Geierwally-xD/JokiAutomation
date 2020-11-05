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
        }

        // interprets command line arguments 
        public void CommandInterpreter(string[] commands)
        {
            try
            {
                int commandID = 0;
                string cmd = commands[1];
                if ((cmd == "Pause") && (commands.Length == 4))
                {
                    commandID = AudioMix.AM_ACTIVE + 0x03;
                    _audioMix.executeAudio(commandID); // activate audio channels 1 and 2 
                    _eventTimer.sendPause("\""+commands[2]+"\"", "\"" + commands[3]+"\""); // start slide show
                    commandID = AudioMix.AM_FADEUP + 0x01; // fade up audio channel 1 and fade down 2 - 4
                    _audioMix.executeAudio(commandID); 
                }
                else if (cmd == "Timer")
                {
                    commandID = AudioMix.AM_ACTIVE + 0x03;
                    _audioMix.executeAudio(commandID); // activate audio channels 1 and 2 
                    _eventTimer.sendEventTime("\"" + commands[2] + "\"");                // start event timer
                    commandID = AudioMix.AM_FADEUP + 0x01; // fade up audio channel 1 and fade down 2 - 4
                    _audioMix.executeAudio(commandID);
                }
                else if (cmd == "Band")
                {
                    _infraredControl.executeIR(0);
                    commandID = AudioMix.AM_FADEUP + 0x02; // fade up audio channel 2 and fade down 1, 3 and 4
                    _audioMix.executeAudio(commandID); 
                }
                else if (cmd == "Altar")
                {
                    _infraredControl.executeIR(1);
                    commandID = AudioMix.AM_FADEUP + 0x02; // fade up audio channel 2 and fade down 1, 3 and 4
                    _audioMix.executeAudio(commandID);
                }
                else if (cmd == "Predigt")
                {
                    _infraredControl.executeIR(2);
                    commandID = AudioMix.AM_FADEUP + 0x02; // fade up audio channel 2 and fade down 1, 3 and 4
                    _audioMix.executeAudio(commandID);
                }
                else if (cmd == "GoPro")
                {
                    _infraredControl.executeIR(3);
                    commandID = AudioMix.AM_FADEUP + 0x02; // fade up audio channel 2 and fade down 1, 3 and 4
                    _audioMix.executeAudio(commandID);
                }
                else if (cmd == "Gebet")
                {
                    _infraredControl.executeIR(4);
                    commandID = AudioMix.AM_FADEUP + 0x02; // fade up audio channel 2 and fade down 1, 3 and 4
                    _audioMix.executeAudio(commandID);
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
            }
            else
            {
                string eventTime = dateTimePicker1.Value.TimeOfDay.Hours.ToString("00") + ":" + dateTimePicker1.Value.TimeOfDay.Minutes.ToString("00");
                _eventTimer.sendEventTime(eventTime);
            }
            commandID = AudioMix.AM_FADEUP + 0x01; // fade up audio channel 1 and fade down 2 - 4
            _audioMix.executeAudio(commandID);
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

        // start Audio - sequence
        private void button9_Click(object sender, EventArgs e)
        {
            int commandID = AudioMix.AM_FADEUP;
            if (_audioMix.channelActive_[listBox3.SelectedIndex] == true)
            {
                commandID += 1 << listBox4.SelectedIndex;
                _audioMix.executeAudio(commandID);
            }
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

        private EventTimer _eventTimer = new EventTimer();
        private AudioMix _audioMix = new AudioMix();
        private InfraredControl _infraredControl = new InfraredControl();
        public  LogData _logDat = new LogData();

    }


}
