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
        }

        // interprets command line arguments 
        public void CommandInterpreter(string[] commands)
        {
            try
            {
                string cmd = commands[1];
                if ((cmd == "Pause") && (commands.Length == 4))
                {
                    eventTimer.sendPause("\""+commands[2]+"\"", "\"" + commands[3]+"\"");
                }
                else if (cmd == "Timer")
                {
                    eventTimer.sendEventTime("\"" + commands[2] + "\"");
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
                eventTimer.sendPause("\"" + textBox1.Text +"\"" , "\"" + textBox2.Text+"\"" );
            }
            else
            {
                string eventTime = dateTimePicker1.Value.TimeOfDay.Hours.ToString("00") + ":" + dateTimePicker1.Value.TimeOfDay.Minutes.ToString("00");
                eventTimer.sendEventTime(eventTime);
            }
        }

        private EventTimer eventTimer = new EventTimer();
        private AudioMix audioMix = new AudioMix();
        private RasPi rasPi = new RasPi();
    }
}
