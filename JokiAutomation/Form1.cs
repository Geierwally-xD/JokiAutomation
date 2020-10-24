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
            listBox1.SelectedIndex = 0;
            listBox2.SelectedIndex = 0;
        }

        // interprets command line arguments 
        public void CommandInterpreter(string[] commands)
        {
            try
            {
                string cmd = commands[1];
                if ((cmd == "Pause") && (commands.Length == 4))
                {
                    _eventTimer.sendPause("\""+commands[2]+"\"", "\"" + commands[3]+"\"");
                }
                else if (cmd == "Timer")
                {
                    _eventTimer.sendEventTime("\"" + commands[2] + "\"");
                }
                else if (cmd == "Band")
                {
                    _infraredControl.executeIR(0);
                }
                else if (cmd == "Altar")
                {
                    _infraredControl.executeIR(1);
                }
                else if (cmd == "Predigt")
                {
                    _infraredControl.executeIR(2);
                }
                else if (cmd == "GoPro")
                {
                    _infraredControl.executeIR(3);
                }
                else if (cmd == "Gebet")
                {
                    _infraredControl.executeIR(4);
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
            if(this.listBox1.Text == "Pause")
            {
                _eventTimer.sendPause("\"" + textBox1.Text +"\"" , "\"" + textBox2.Text+"\"" );
            }
            else
            {
                string eventTime = dateTimePicker1.Value.TimeOfDay.Hours.ToString("00") + ":" + dateTimePicker1.Value.TimeOfDay.Minutes.ToString("00");
                _eventTimer.sendEventTime(eventTime);
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

        private EventTimer _eventTimer = new EventTimer();
        private AudioMix _audioMix = new AudioMix();
        private InfraredControl _infraredControl = new InfraredControl();
        public  LogData _logDat = new LogData();

    }


}
