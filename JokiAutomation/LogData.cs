using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Threading;

namespace JokiAutomation
{
    /// <summary>
    /// Logging data manager for application messages and events
    /// Handles message output to RichTextBox controls or log files
    /// </summary>
    public class LogData
    {
        private static LogData activeInstance;

        /// <summary>
        /// Initialize log data system with parent form reference
        /// </summary>
        /// <param name="winForm">Parent form containing RichTextBox controls for logging</param>
        public void initLogData(Form1 winForm)
        {
            logDatForm = winForm; // Kann null sein im Kommandozeilen-Modus
            activeInstance = this;
        }

        public static void LogLifecycle(string mode, string eventName, string details = null)
        {
            try
            {
                int pid = Process.GetCurrentProcess().Id;
                int tid = Thread.CurrentThread.ManagedThreadId;
                string timestamp = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss.fff");
                string normalizedMode = string.IsNullOrWhiteSpace(mode) ? "UNKNOWN" : mode;
                string normalizedEvent = string.IsNullOrWhiteSpace(eventName) ? "UNKNOWN" : eventName;

                string message =
                    $"{timestamp} [LIFECYCLE] PID={pid} TID={tid} Mode={normalizedMode} Event={normalizedEvent}";

                if (!string.IsNullOrWhiteSpace(details))
                {
                    message += $" {details}";
                }

                LogData current = activeInstance;
                if (current != null)
                {
                    current.sendInfoMessage(message);
                    return;
                }

                string appPath = Assembly.GetExecutingAssembly().Location;
                WinFormsExtensions.writeLog(appPath, message);
                Console.WriteLine(message);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LIFECYCLE-LOGGING-ERROR] {ex.GetType().Name}: {ex.Message}");
            }
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
                Console.WriteLine(message);
            }
            else
            {
                // Check if we need to invoke (are we on a different thread?)
                if (logDatForm.InvokeRequired)
                {
                    // Marshal the call to the UI thread
                    logDatForm.BeginInvoke(new Action(() => sendInfoMessage(strMsg)));
                    return;
                }
                
                // GUI-Modus (now guaranteed to be on UI thread)
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
        private static readonly object LogSync = new object();

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
            try
            {
                string filename = Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, "RasPiAutomationLog.txt");

                lock (LogSync)
                {
                    const int maxAttempts = 3;
                    for (int attempt = 1; attempt <= maxAttempts; attempt++)
                    {
                        try
                        {
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
                        catch (IOException ioEx)
                        {
                            if (attempt == maxAttempts)
                            {
                                Debug.WriteLine($"writeLog failed after retries: {ioEx.Message}");
                                return;
                            }

                            Thread.Sleep(30);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"writeLog unexpected error: {ex.Message}");
            }
        }
    }
}
