using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Reflection;

namespace JokiAutomation
{
    /// <summary>
    /// Logging data manager for application messages and events
    /// Handles message output to RichTextBox controls or log files
    /// </summary>
    public class LogData
    {
        /// <summary>
        /// Initialize log data system with parent form reference
        /// </summary>
        /// <param name="winForm">Parent form containing RichTextBox controls for logging</param>
        public void initLogData(Form1 winForm)
        {
            logDatForm = winForm; // Kann null sein im Kommandozeilen-Modus
        }

        /// <summary>
        /// Send an informational message to the appropriate output
        /// Outputs to RichTextBox if GUI is available, otherwise writes to log file
        /// Message is automatically prefixed with current date and time
        /// </summary>
        /// <param name="strMsg">Message text to log</param>
        public void sendInfoMessage(string strMsg)
        {
            String time = DateTime.Now.ToString("HH:mm:ss");
            String date = DateTime.Now.ToString("yy/MM/dd");
            String message = date + " " + time + " " + strMsg;
            
            // Wenn keine Form vorhanden: Log-Datei
            if (logDatForm == null || !logDatForm.Visible)
            {
                string app_path = Assembly.GetExecutingAssembly().Location;
                WinFormsExtensions.writeLog(app_path, message);
                Console.WriteLine(message); // Auch in Console ausgeben
            }
            else
            {
                // GUI-Modus
                var richTextBox = GetRichTextBoxForSelectedTab();
                if (richTextBox != null)
                {
                    richTextBox.Focus();
                    WinFormsExtensions.AppendLine(richTextBox, message);
                }
            }
        }

        /// <summary>
        /// Get the appropriate RichTextBox control for the currently selected tab
        /// </summary>
        /// <returns>RichTextBox control for active tab, or null if tab has no logging area</returns>
        private System.Windows.Forms.RichTextBox GetRichTextBoxForSelectedTab()
        {
            switch (logDatForm.TabControl1.SelectedIndex)
            {
                case 0: // Event Timer
                    return null; // No RichTextBox on Tab 0
                case 1: // Infrared Remote Control
                    return logDatForm.richTextBox1;
                case 2: // Audio Mix
                    return logDatForm.richTextBox2;
                case 3: // Position Camcorder
                    return logDatForm.richTextBox3;
                case 4: // AutoZoom Configuration
                    return logDatForm.richTextBoxZoomConfig;
                default:
                    return null;
            }
        }

        private Form1 logDatForm;
    }

    /// <summary>
    /// Extension methods for Windows Forms controls
    /// Provides helper methods for logging and text manipulation
    /// </summary>
    public static class WinFormsExtensions
    {
        /// <summary>
        /// Append a line to RichTextBox content with automatic newline handling
        /// If textbox is empty, sets text directly; otherwise appends with newline
        /// </summary>
        /// <param name="source">RichTextBox control to append to</param>
        /// <param name="value">Text value to append</param>
        public static void AppendLine(System.Windows.Forms.RichTextBox source, String value)
        {
            if (source.Text.Length == 0)
                source.Text = value;
            else
                source.AppendText("\r\n" + value);
        }

        /// <summary>
        /// Write log message to file
        /// Creates log file if it doesn't exist, otherwise appends to existing file
        /// Log file is created in the same directory as the application executable
        /// </summary>
        /// <param name="path">Application executable path</param>
        /// <param name="str">Log message to write</param>
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
