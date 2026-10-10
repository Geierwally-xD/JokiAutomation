using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace JokiAutomation
{
    /// <summary>
    /// Remote control for SHARP 55GJ4225E Roku TV via LAN
    /// Provides power on/off and HDMI input switching functionality
    /// Uses Roku External Control Protocol (ECP) over HTTP
    /// </summary>
    internal class ROKU_TV_Remote : IDisposable
    {
        private readonly string _ipAddress;
        private readonly int _port;
        private readonly HttpClient _httpClient;
        private bool _disposed = false;
        private readonly string _configPath;

        // Roku ECP Key codes
        private const string KEY_POWER = "Power";
        private const string KEY_POWER_ON = "PowerOn";
        private const string KEY_POWER_OFF = "PowerOff";
        private const string KEY_HOME = "Home";
        private const string KEY_INPUT_HDMI1 = "InputHDMI1";
        private const string KEY_INPUT_HDMI2 = "InputHDMI2";
        private const string KEY_INPUT_HDMI3 = "InputHDMI3";
        private const string KEY_INPUT_HDMI4 = "InputHDMI4";

        // Roku launch targets (often more reliable for wake/input switch)
        private const string LAUNCH_TVINPUT_HDMI1 = "tvinput.HDMI1";
        private const string LAUNCH_TVINPUT_HDMI2 = "tvinput.HDMI2";
        private const string LAUNCH_TVINPUT_HDMI3 = "tvinput.HDMI3";
        private const string LAUNCH_TVINPUT_HDMI4 = "tvinput.HDMI4";
        private const string LAUNCH_TVINPUT_HDMI1_LOWER = "tvinput.hdmi1";
        private const string LAUNCH_TVINPUT_HDMI2_LOWER = "tvinput.hdmi2";
        private const string LAUNCH_TVINPUT_HDMI3_LOWER = "tvinput.hdmi3";
        private const string LAUNCH_TVINPUT_HDMI4_LOWER = "tvinput.hdmi4";

        // Default configuration values
        private const string DEFAULT_IP = "192.168.178.45";
        private const int DEFAULT_PORT = 8060;
        private const string CONFIG_FILENAME = "rokuTV.cfg";

        /// <summary>
        /// Initialize Roku TV remote control with IP address from configuration file
        /// Reads configuration from rokuTV.cfg in JokiAutomation environment path
        /// </summary>
        public ROKU_TV_Remote()
        {
            string jokiPath =
                Environment.GetEnvironmentVariable("JokiAutomation", EnvironmentVariableTarget.Process) ??
                Environment.GetEnvironmentVariable("JokiAutomation", EnvironmentVariableTarget.User) ??
                Environment.GetEnvironmentVariable("JokiAutomation", EnvironmentVariableTarget.Machine);
            _configPath = string.IsNullOrEmpty(jokiPath)
                ? CONFIG_FILENAME
                : Path.Combine(jokiPath, CONFIG_FILENAME);

            // Load configuration
            LoadConfiguration(out string ipAddress, out int port);

            _ipAddress = ipAddress;
            _port = port;

            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(5)
            };
        }

        /// <summary>
        /// Initialize Roku TV remote control with explicit IP address
        /// </summary>
        /// <param name="ipAddress">IP address of Roku TV (e.g., "192.168.178.45")</param>
        /// <param name="port">ECP port (default: 8060)</param>
        public ROKU_TV_Remote(string ipAddress, int port = 8060)
        {
            if (string.IsNullOrWhiteSpace(ipAddress))
                throw new ArgumentException("IP address cannot be empty", nameof(ipAddress));

            _ipAddress = ipAddress;
            _port = port;

            string jokiPath =
                Environment.GetEnvironmentVariable("JokiAutomation", EnvironmentVariableTarget.Process) ??
                Environment.GetEnvironmentVariable("JokiAutomation", EnvironmentVariableTarget.User) ??
                Environment.GetEnvironmentVariable("JokiAutomation", EnvironmentVariableTarget.Machine);
            _configPath = string.IsNullOrEmpty(jokiPath)
                ? CONFIG_FILENAME
                : Path.Combine(jokiPath, CONFIG_FILENAME);

            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(5)
            };
        }

        /// <summary>
        /// Load configuration from rokuTV.cfg file
        /// File format (line by line):
        /// Line 1: IP address (e.g., 192.168.178.45)
        /// Line 2: Port (optional, default: 8060)
        /// </summary>
        /// <param name="ipAddress">Output: IP address read from config or default</param>
        /// <param name="port">Output: Port read from config or default</param>
        private void LoadConfiguration(out string ipAddress, out int port)
        {
            ipAddress = DEFAULT_IP;
            port = DEFAULT_PORT;

            try
            {
                if (File.Exists(_configPath))
                {
                    string[] lines = File.ReadAllLines(_configPath);

                    if (lines.Length > 0 && !string.IsNullOrWhiteSpace(lines[0]))
                    {
                        ipAddress = lines[0].Trim();
                    }

                    if (lines.Length > 1 && !string.IsNullOrWhiteSpace(lines[1]))
                    {
                        if (int.TryParse(lines[1].Trim(), out int configPort))
                        {
                            port = configPort;
                        }
                    }
                }
                else
                {
                    // Create default configuration file
                    CreateDefaultConfiguration();
                }
            }
            catch (Exception ex)
            {
                // Log error and use default values
                System.Diagnostics.Debug.WriteLine($"Error loading Roku TV configuration: {ex.Message}");
            }
        }

        /// <summary>
        /// Create default configuration file with default values
        /// </summary>
        private void CreateDefaultConfiguration()
        {
            try
            {
                string directoryPath = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(directoryPath) && !Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                File.WriteAllLines(_configPath, new[]
                {
                    DEFAULT_IP,
                    DEFAULT_PORT.ToString(),
                    "# Roku TV Configuration",
                    "# Line 1: IP Address",
                    "# Line 2: Port (default: 8060)"
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating Roku TV configuration: {ex.Message}");
            }
        }

        /// <summary>
        /// Sends a key press command to the Roku TV
        /// </summary>
        /// <param name="key">The key code to send (e.g., "PowerOn", "PowerOff", "InputHDMI2")</param>
        private async Task SendKeyAsync(string key)
        {
            var url = $"http://{_ipAddress}:{_port}/keypress/{key}";
            var response = await _httpClient.PostAsync(url, null);
            response.EnsureSuccessStatusCode();
        }

        /// <summary>
        /// TV einschalten bzw. aus Standby holen (Async Version)
        /// </summary>
        public async Task PowerOnAsync()
        {
            await SendKeyAsync("PowerOn");
        }

        /// <summary>
        /// TV einschalten bzw. aus Standby holen (Synchronous - Returns bool)
        /// </summary>
        public bool PowerOn()
        {
            try
            {
                PowerOnAsync().Wait();
                return true;
            }
            catch (HttpRequestException)
            {
                return false;
            }
            catch (TaskCanceledException)
            {
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// TV ausschalten (Async Version)
        /// </summary>
        public async Task PowerOffAsync()
        {
            await SendKeyAsync("PowerOff");
        }

        /// <summary>
        /// TV ausschalten (Synchronous - Returns bool)
        /// </summary>
        public bool PowerOff()
        {
            try
            {
                PowerOffAsync().Wait();
                return true;
            }
            catch (HttpRequestException)
            {
                return false;
            }
            catch (TaskCanceledException)
            {
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Auf HDMI 2 umschalten (Async Version)
        /// </summary>
        public async Task SwitchToHdmi2Async()
        {
            await SendKeyAsync("InputHDMI2");
        }

        /// <summary>
        /// Auf HDMI 2 umschalten (Synchronous - Returns bool)
        /// </summary>
        public bool SwitchToHdmi2()
        {
            try
            {
                SwitchToHdmi2Async().Wait();
                return true;
            }
            catch (HttpRequestException)
            {
                return false;
            }
            catch (TaskCanceledException)
            {
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Auf HDMI 2 umschalten - Alternative Casing (Async Version)
        /// </summary>
        public async Task SwitchToHDMI2Async()
        {
            await SendKeyAsync("InputHDMI2");
        }

        /// <summary>
        /// Auf HDMI 2 umschalten - Alternative Casing (Synchronous - Returns bool)
        /// </summary>
        public bool SwitchToHDMI2()
        {
            try
            {
                SwitchToHDMI2Async().Wait();
                return true;
            }
            catch (HttpRequestException)
            {
                return false;
            }
            catch (TaskCanceledException)
            {
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Dispose implementation for IDisposable
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Protected virtual dispose method
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _httpClient?.Dispose();
                }
                _disposed = true;
            }
        }
    }
}
