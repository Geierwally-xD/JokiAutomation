using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace JokiAutomation
{
    internal class EventTimer
    {
        // starts eventtimer App with parameter 'Pause' and two break texts
        public void sendPause(string text1, string text2)
        {
            try
            {
                System.Diagnostics.ProcessStartInfo eventTimer = new ProcessStartInfo();
                eventTimer.FileName = Environment.GetEnvironmentVariable("JokiAutomation") + "EventTimer.exe";
                eventTimer.Arguments = "Pause " + text1 + " " + text2;
                Process.Start(eventTimer);
            }
            catch (Exception)
            {
                MessageBox.Show("JoKiAutomation\nFehler in 'Pause' Kommandozeilenaufruf");
            }
        }

        // starts eventtimer App with parameter Timer and eventtime hours:minutes
        public void sendEventTime(string eventTime)
        {
            try
            {
                System.Diagnostics.ProcessStartInfo eventTimer = new ProcessStartInfo();
                eventTimer.FileName = Environment.GetEnvironmentVariable("JokiAutomation") + "EventTimer.exe";
                eventTimer.Arguments = "Timer " + eventTime;
                Process.Start(eventTimer);
            }
            catch (Exception)
            {
                MessageBox.Show("JoKiAutomation\nFehler in 'Timer' Kommandozeilenaufruf");
            }
        }
    }    
}