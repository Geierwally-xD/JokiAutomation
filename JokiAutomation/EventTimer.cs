using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace JokiAutomation
{
    internal sealed class EventTimerStartResult
    {
        public bool Started { get; set; }
        public bool AlreadyRunning { get; set; }
        public int? ProcessId { get; set; }
        public int? ExitCode { get; set; }
        public string ErrorMessage { get; set; }
    }

    /// <summary>
    /// Controls the external EventTimer application for pause and event time management
    /// Handles starting the EventTimer.exe with appropriate command line arguments
    /// </summary>
    internal class EventTimer : IDisposable
    {
        private const string ENV_VAR_NAME = "JokiAutomation";
        private const string EXECUTABLE_NAME = "EventTimer.exe";
        private const int EventTimerAlreadyRunningExitCode = 8;

        /// <summary>
        /// Start the EventTimer application in pause mode
        /// Displays two text messages for pause screen
        /// </summary>
        /// <param name="text1">First pause text to display</param>
        /// <param name="text2">Second pause text to display</param>
        /// <returns>True if start was successful, false otherwise</returns>
        /// <exception cref="ArgumentException">Thrown when text1 or text2 are empty</exception>
        public EventTimerStartResult sendPause(string text1, string text2, string requestId = null, string source = "UNKNOWN")
        {
            if (string.IsNullOrWhiteSpace(text1))
                throw new ArgumentException("Text1 must not be empty", nameof(text1));
            if (string.IsNullOrWhiteSpace(text2))
                throw new ArgumentException("Text2 must not be empty", nameof(text2));

            string effectiveRequestId = string.IsNullOrWhiteSpace(requestId) ? Guid.NewGuid().ToString("N") : requestId;
            string arguments = $"Pause {EscapeArgument(text1)} {EscapeArgument(text2)}";
            return StartEventTimer(arguments, "Pause", "Pause", effectiveRequestId, source);
        }

        /// <summary>
        /// Start the EventTimer application in timer mode
        /// Displays countdown timer to specified event time
        /// </summary>
        /// <param name="eventTime">Event time in format HH:mm or "HH:mm"</param>
        /// <returns>True if start was successful, false otherwise</returns>
        /// <exception cref="ArgumentException">Thrown when eventTime is empty or invalid format</exception>
        public EventTimerStartResult sendEventTime(string eventTime, string requestId = null, string source = "UNKNOWN")
        {
            if (string.IsNullOrWhiteSpace(eventTime))
                throw new ArgumentException("EventTime must not be empty", nameof(eventTime));

            // Remove surrounding quotes if present
            eventTime = eventTime.Trim('"');

            // Validate format (optional, but recommended)
            if (!Regex.IsMatch(eventTime, @"^\d{2}:\d{2}$"))
                throw new ArgumentException($"EventTime must be in format HH:mm. Received: {eventTime}", nameof(eventTime));

            string effectiveRequestId = string.IsNullOrWhiteSpace(requestId) ? Guid.NewGuid().ToString("N") : requestId;
            string arguments = $"Timer \"{eventTime}\"";
            return StartEventTimer(arguments, "Timer", "Timer", effectiveRequestId, source);
        }

        /// <summary>
        /// Start the EventTimer application with given arguments
        /// Validates environment, checks file existence, and launches process
        /// </summary>
        /// <param name="arguments">Command line arguments to pass to EventTimer.exe</param>
        /// <param name="errorContext">Context for error messages (e.g., "Pause", "Timer")</param>
        /// <returns>True if start was successful, false otherwise</returns>
        private EventTimerStartResult StartEventTimer(string arguments, string errorContext, string command, string requestId, string source)
        {
            var result = new EventTimerStartResult();

            try
            {
                string normalizedSource = string.IsNullOrWhiteSpace(source) ? "UNKNOWN" : source;
                LogData.LogLifecycle(
                    normalizedSource,
                    "EVENTTIMER_REQUEST_BEGIN",
                    $"RequestId={requestId} Source={normalizedSource} Command={command} PID={Process.GetCurrentProcess().Id} Args=\"{arguments}\"");

                // Check environment variable
                string basePath = Environment.GetEnvironmentVariable(ENV_VAR_NAME);
                if (string.IsNullOrWhiteSpace(basePath))
                {
                    ShowError(
                        $"The environment variable '{ENV_VAR_NAME}' is not set.\n\n" +
                        "Please check the installation.",
                        "Configuration Error",
                        MessageBoxIcon.Warning
                    );
                    result.ErrorMessage = $"Environment variable '{ENV_VAR_NAME}' is not set.";
                    LogData.LogLifecycle(normalizedSource, "EVENTTIMER_REQUEST_END", $"RequestId={requestId} Started=False AlreadyRunning=False ExitCode=3 Error=EnvVarMissing");
                    return result;
                }

                // Check if directory exists
                if (!Directory.Exists(basePath))
                {
                    ShowError(
                        $"The directory was not found:\n{basePath}\n\n" +
                        "Please check the environment variable and installation.",
                        "Directory Not Found",
                        MessageBoxIcon.Error
                    );
                    result.ErrorMessage = $"Base directory not found: {basePath}";
                    LogData.LogLifecycle(normalizedSource, "EVENTTIMER_REQUEST_END", $"RequestId={requestId} Started=False AlreadyRunning=False ExitCode=3 Error=BasePathMissing");
                    return result;
                }

                // Check if executable exists
                string exePath = Path.Combine(basePath, EXECUTABLE_NAME);
                if (!File.Exists(exePath))
                {
                    ShowError(
                        $"{EXECUTABLE_NAME} was not found:\n{exePath}\n\n" +
                        "Please check the installation.",
                        "File Not Found",
                        MessageBoxIcon.Error
                    );
                    result.ErrorMessage = $"Executable not found: {exePath}";
                    LogData.LogLifecycle(normalizedSource, "EVENTTIMER_REQUEST_END", $"RequestId={requestId} Started=False AlreadyRunning=False ExitCode=3 Error=ExeMissing");
                    return result;
                }

                // Start process
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = false,
                    WorkingDirectory = basePath
                };

                LogData.LogLifecycle(
                    normalizedSource,
                    "EVENTTIMER_START_BEGIN",
                    $"RequestId={requestId} Exe=\"{exePath}\" Args=\"{arguments}\" ParentPid={Process.GetCurrentProcess().Id}");

                using (Process process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        ShowError(
                            $"{EXECUTABLE_NAME} could not be started.\n\n" +
                            "No additional information available.",
                            "Start Error",
                            MessageBoxIcon.Error
                        );

                        result.ErrorMessage = "Process.Start returned null.";
                        LogData.LogLifecycle(normalizedSource, "EVENTTIMER_START_FAILED", $"RequestId={requestId} Reason=ProcessStartReturnedNull");
                        LogData.LogLifecycle(normalizedSource, "EVENTTIMER_REQUEST_END", $"RequestId={requestId} Started=False AlreadyRunning=False ExitCode=1");
                        return result;
                    }

                    result.ProcessId = process.Id;
                    result.Started = true;

                    if (process.WaitForExit(200))
                    {
                        result.ExitCode = process.ExitCode;
                        if (process.ExitCode == EventTimerAlreadyRunningExitCode)
                        {
                            result.Started = false;
                            result.AlreadyRunning = true;
                        }
                    }

                    LogData.LogLifecycle(normalizedSource, "EVENTTIMER_PROCESS_CREATED", $"RequestId={requestId} ChildPid={process.Id}");
                    LogData.LogLifecycle(normalizedSource, "EVENTTIMER_REQUEST_END", $"RequestId={requestId} Started={result.Started} AlreadyRunning={result.AlreadyRunning} ChildPid={result.ProcessId} ExitCode={(result.ExitCode.HasValue ? result.ExitCode.Value.ToString() : "null")}");
                }

                return result;
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                ShowError(
                    $"{EXECUTABLE_NAME} could not be started ({errorContext}):\n\n" +
                    $"Error: {ex.Message}\n" +
                    $"Error code: {ex.NativeErrorCode}",
                    "Start Error",
                    MessageBoxIcon.Error
                );
                result.ErrorMessage = ex.Message;
                LogData.LogLifecycle(string.IsNullOrWhiteSpace(source) ? "UNKNOWN" : source, "EVENTTIMER_START_FAILED", $"RequestId={requestId} Exception={ex.GetType().Name} Message=\"{ex.Message}\"");
                LogData.LogLifecycle(string.IsNullOrWhiteSpace(source) ? "UNKNOWN" : source, "EVENTTIMER_REQUEST_END", $"RequestId={requestId} Started=False AlreadyRunning=False ExitCode=1");
                return result;
            }
            catch (Exception ex)
            {
                ShowError(
                    $"Unexpected error when starting {EXECUTABLE_NAME} ({errorContext}):\n\n" +
                    $"{ex.GetType().Name}: {ex.Message}",
                    "Error",
                    MessageBoxIcon.Error
                );
                result.ErrorMessage = ex.Message;
                LogData.LogLifecycle(string.IsNullOrWhiteSpace(source) ? "UNKNOWN" : source, "EVENTTIMER_START_FAILED", $"RequestId={requestId} Exception={ex.GetType().Name} Message=\"{ex.Message}\"");
                LogData.LogLifecycle(string.IsNullOrWhiteSpace(source) ? "UNKNOWN" : source, "EVENTTIMER_REQUEST_END", $"RequestId={requestId} Started=False AlreadyRunning=False ExitCode=1");
                return result;
            }
        }

        /// <summary>
        /// Escape a command line argument for safe passing
        /// Handles quotes and backslashes according to Windows command line rules
        /// </summary>
        /// <param name="argument">Argument to escape</param>
        /// <returns>Escaped argument enclosed in quotes</returns>
        private string EscapeArgument(string argument)
        {
            if (string.IsNullOrEmpty(argument))
                return "\"\"";

            // Remove already existing outer quotes
            argument = argument.Trim('"');

            // Escape backslashes before quotes
            argument = argument.Replace("\\", "\\\\").Replace("\"", "\\\"");

            // Always enclose in quotes for consistency
            return $"\"{argument}\"";
        }

        /// <summary>
        /// Display an error message with consistent formatting
        /// </summary>
        /// <param name="message">Error message text</param>
        /// <param name="title">Title of error dialog</param>
        /// <param name="icon">Icon to display (Warning, Error, etc.)</param>
        private void ShowError(string message, string title, MessageBoxIcon icon)
        {
            MessageBox.Show(
                message,
                $"JoKi Automation - {title}",
                MessageBoxButtons.OK,
                icon
            );
        }

        /// <summary>
        /// Release all resources used by the EventTimer
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Release unmanaged resources and optionally release managed resources
        /// </summary>
        /// <param name="disposing">True to release both managed and unmanaged resources; false to release only unmanaged resources</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                // EventTimer starts external processes but keeps no references
                // No resources to clean up
            }
        }
    }
}