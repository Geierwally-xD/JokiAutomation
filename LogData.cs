using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Reflection;

namespace JokiAutomation
{
    public class LogData
    {
        public void initLogData(Form1 winForm)
        {
            logDatForm = winForm;
        }

        public void sendInfoMessage(string strMsg)
        {
            String time = DateTime.Now.ToString("hh:mm:ss"); // includes leading zeros
            String date = DateTime.Now.ToString("yy/MM/dd"); // includes leading zeros
            String message = date + " " + time + " ";
            message = message + strMsg;
            if (logDatForm != null)
            {
                if (logDatForm.TabControl1.SelectedIndex == 1)
                {
                    logDatForm.richTextBox1.Focus();
                    WinFormsExtensions.AppendLine(logDatForm.richTextBox1, message);
                }
                else if (logDatForm.TabControl1.SelectedIndex == 2)
                {
                   // logDatForm.richTextBox2.Focus();
                   // WinFormsExtensions.AppendLine(logDatForm.richTextBox2, message);
                }
                else if (logDatForm.TabControl1.SelectedIndex == 3)
                {
                   // logDatForm.richTextBox3.Focus();
                   // WinFormsExtensions.AppendLine(logDatForm.richTextBox3, message);
                }
            }
            else
            {   // command line application, write to logfile
                string app_path = Assembly.GetExecutingAssembly().Location;
                WinFormsExtensions.writeLog(app_path, message);
            }
        }
        private Form1 logDatForm;
    }

    // extension for appending lines on a textbox content (new line)
    public static class WinFormsExtensions
    {
        public static void AppendLine(System.Windows.Forms.RichTextBox source, String value)
        {
            if (source.Text.Length == 0)
                source.Text = value;
            else
                source.AppendText("\r\n" + value);
        }


        public static void writeLog(String path, String str)
        {
            String filename = Path.GetDirectoryName(path).ToString() + "\\RasPiAutomationLog.txt";
            if (!File.Exists(filename))
            {
                File.WriteAllText(filename, str + "\n");
            }
            else
            {
                File.AppendAllText(filename, str + "\n");
            }
            return;
        }
    }
}
