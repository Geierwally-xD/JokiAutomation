using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

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
            string jokiPath = Environment.GetEnvironmentVariable("JokiAutomation");
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
            
            string jokiPath = Environment.GetEnvironmentVariable("JokiAutomation");
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
                System.Diagnostics.Debug.WriteLine($"Error creating default Roku TV configuration: {ex.Message}");
            }
        }

        /// <summary>
        /// Save current configuration to file
        /// </summary>
        /// <returns>True if save successful, false otherwise</returns>
        public bool SaveConfiguration()
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
                    _ipAddress,
                    _port.ToString(),
                    "# Roku TV Configuration",
                    "# Line 1: IP Address",
                    "# Line 2: Port (default: 8060)"
                });
                
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Get current IP address
        /// </summary>
        public string IPAddress => _ipAddress;

        /// <summary>
        /// Get current port
        /// </summary>
        public int Port => _port;

        /// <summary>
        /// Get configuration file path
        /// </summary>
        public string ConfigPath => _configPath;

        /// <summary>
        /// Power on the Roku TV
        /// Sends PowerOn command via ECP
        /// </summary>
        /// <returns>True if command sent successfully, false otherwise</returns>
        public async Task<bool> PowerOnAsync()
        {
            return await SendKeyPressAsync(KEY_POWER_ON);
        }

        /// <summary>
        /// Power on the Roku TV (synchronous)
        /// </summary>
        /// <returns>True if command sent successfully, false otherwise</returns>
        public bool PowerOn()
        {
            return PowerOnAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Power off the Roku TV
        /// Sends PowerOff command via ECP
        /// </summary>
        /// <returns>True if command sent successfully, false otherwise</returns>
        public async Task<bool> PowerOffAsync()
        {
            return await SendKeyPressAsync(KEY_POWER_OFF);
        }

        /// <summary>
        /// Power off the Roku TV (synchronous)
        /// </summary>
        /// <returns>True if command sent successfully, false otherwise</returns>
        public bool PowerOff()
        {
            return PowerOffAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Toggle power state (on/off)
        /// </summary>
        /// <returns>True if command sent successfully, false otherwise</returns>
        public async Task<bool> TogglePowerAsync()
        {
            return await SendKeyPressAsync(KEY_POWER);
        }

        /// <summary>
        /// Toggle power state (on/off) - synchronous
        /// </summary>
        /// <returns>True if command sent successfully, false otherwise</returns>
        public bool TogglePower()
        {
            return TogglePowerAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Switch TV input to HDMI 2
        /// Sends InputHDMI2 command via ECP
        /// </summary>
        /// <returns>True if command sent successfully, false otherwise</returns>
        public async Task<bool> SwitchToHDMI2Async()
        {
            return await SendKeyPressAsync(KEY_INPUT_HDMI2);
        }

        /// <summary>
        /// Switch TV input to HDMI 2 (synchronous)
        /// </summary>
        /// <returns>True if command sent successfully, false otherwise</returns>
        public bool SwitchToHDMI2()
        {
            return SwitchToHDMI2Async().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Switch TV input to HDMI 1
        /// </summary>
        /// <returns>True if command sent successfully, false otherwise</returns>
        public async Task<bool> SwitchToHDMI1Async()
        {
            return await SendKeyPressAsync(KEY_INPUT_HDMI1);
        }

        /// <summary>
        /// Switch TV input to HDMI 1 (synchronous)
        /// </summary>
        /// <returns>True if command sent successfully, false otherwise</returns>
        public bool SwitchToHDMI1()
        {
            return SwitchToHDMI1Async().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Switch TV input to HDMI 3
        /// </summary>
        /// <returns>True if command sent successfully, false otherwise</returns>
        public async Task<bool> SwitchToHDMI3Async()
        {
            return await SendKeyPressAsync(KEY_INPUT_HDMI3);
        }

        /// <summary>
        /// Switch TV input to HDMI 3 (synchronous)
        /// </summary>
        /// <returns>True if command sent successfully, false otherwise</returns>
        public bool SwitchToHDMI3()
        {
            return SwitchToHDMI3Async().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Switch TV input to HDMI 4
        /// </summary>
        /// <returns>True if command sent successfully, false otherwise</returns>
        public async Task<bool> SwitchToHDMI4Async()
        {
            return await SendKeyPressAsync(KEY_INPUT_HDMI4);
        }

        /// <summary>
        /// Switch TV input to HDMI 4 (synchronous)
        /// </summary>
        /// <returns>True if command sent successfully, false otherwise</returns>
        public bool SwitchToHDMI4()
        {
            return SwitchToHDMI4Async().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Go to Roku home screen
        /// </summary>
        /// <returns>True if command sent successfully, false otherwise</returns>
        public async Task<bool> GoHomeAsync()
        {
            return await SendKeyPressAsync(KEY_HOME);
        }

        /// <summary>
        /// Go to Roku home screen (synchronous)
        /// </summary>
        /// <returns>True if command sent successfully, false otherwise</returns>
        public bool GoHome()
        {
            return GoHomeAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Get device information from Roku TV
        /// </summary>
        /// <returns>XML string with device info, or null if failed</returns>
        public async Task<string> GetDeviceInfoAsync()
        {
            try
            {
                string url = $"http://{_ipAddress}:{_port}/query/device-info";
                HttpResponseMessage response = await _httpClient.GetAsync(url);
                
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }
                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Get device information from Roku TV (synchronous)
        /// </summary>
        /// <returns>XML string with device info, or null if failed</returns>
        public string GetDeviceInfo()
        {
            return GetDeviceInfoAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Check if Roku TV is reachable on network
        /// </summary>
        /// <returns>True if TV responds, false otherwise</returns>
        public async Task<bool> IsReachableAsync()
        {
            string deviceInfo = await GetDeviceInfoAsync();
            return !string.IsNullOrEmpty(deviceInfo);
        }

        /// <summary>
        /// Check if Roku TV is reachable on network (synchronous)
        /// </summary>
        /// <returns>True if TV responds, false otherwise</returns>
        public bool IsReachable()
        {
            return IsReachableAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Send keypress command to Roku TV via ECP
        /// </summary>
        /// <param name="key">Key code to send (e.g., "Power", "InputHDMI2")</param>
        /// <returns>True if command sent successfully, false otherwise</returns>
        private async Task<bool> SendKeyPressAsync(string key)
        {
            try
            {
                string url = $"http://{_ipAddress}:{_port}/keypress/{key}";
                HttpResponseMessage response = await _httpClient.PostAsync(url, null);
                return response.IsSuccessStatusCode;
            }
            catch (HttpRequestException)
            {
                return false;
            }
            catch (TaskCanceledException)
            {
                // Timeout
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Release all resources used by the ROKU_TV_Remote
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
            if (!_disposed)
            {
                if (disposing)
                {
                    _httpClient?.Dispose();
                }
                _disposed = true;
            }
        }

        /// <summary>
        /// Finalizer for ROKU_TV_Remote
        /// </summary>
        ~ROKU_TV_Remote()
        {
            Dispose(false);
        }
    }
}
