using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Net.Sockets; // <-- Add this using directive

namespace JokiAutomation
{
    /// <summary>
    /// Control class for Delock 11827 Network Socket Adapter
    /// Provides power on/off control for connected devices via LAN
    /// Supports multiple socket outlets with individual control
    /// Configuration must be provided by Form1.cs for multiple instances
    /// </summary>
    internal class DelockSocketAdapter : IDisposable
    {
        private readonly string _ipAddress;
        private readonly int _port;
        private TcpClient _tcpClient;
        private NetworkStream _networkStream;
        private bool _disposed = false;
        private readonly string _username;
        private readonly string _password;
        private readonly string _name;

        // Connection timeouts
        private const int CONNECT_TIMEOUT_MS = 5000;
        private const int READ_TIMEOUT_MS = 3000;
        private const int WRITE_TIMEOUT_MS = 3000;

        // HTTP commands for socket control - WITH AUTHENTICATION
        private const string CMD_SOCKET_ON = "GET /?m={0}&o=1 HTTP/1.0\r\nHost: {1}\r\nAuthorization: Basic {2}\r\nConnection: close\r\n\r\n";
        private const string CMD_SOCKET_OFF = "GET /?m={0}&o=0 HTTP/1.0\r\nHost: {1}\r\nAuthorization: Basic {2}\r\nConnection: close\r\n\r\n";
        private const string CMD_SOCKET_STATUS = "GET /?m=1 HTTP/1.0\r\nHost: {0}\r\nAuthorization: Basic {1}\r\nConnection: close\r\n\r\n";
        private const string CMD_ALL_ON = "GET /?m=99&o=1 HTTP/1.0\r\nHost: {0}\r\nAuthorization: Basic {1}\r\nConnection: close\r\n\r\n";
        private const string CMD_ALL_OFF = "GET /?m=99&o=0 HTTP/1.0\r\nHost: {0}\r\nAuthorization: Basic {1}\r\nConnection: close\r\n\r\n";

        /// <summary>
        /// Initialize Delock Socket Adapter control with explicit settings
        /// </summary>
        /// <param name="name">Name/identifier for this adapter instance (e.g., "Beamer", "Recorder")</param>
        /// <param name="ipAddress">IP address of Delock adapter (e.g., "192.168.178.100")</param>
        /// <param name="port">HTTP port (default: 80)</param>
        /// <param name="username">Username for authentication (default: "admin")</param>
        /// <param name="password">Password for authentication (default: "admin")</param>
        public DelockSocketAdapter(string name, string ipAddress, int port = 80, 
            string username = "admin", string password = "admin")
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Name cannot be empty", nameof(name));
                
            if (string.IsNullOrWhiteSpace(ipAddress))
                throw new ArgumentException("IP address cannot be empty", nameof(ipAddress));

            _name = name;
            _ipAddress = ipAddress;
            _port = port;
            _username = username;
            _password = password;
        }

        /// <summary>
        /// Get Base64 encoded credentials for HTTP Basic Auth
        /// </summary>
        private string GetAuthorizationHeader()
        {
            string credentials = $"{_username}:{_password}";
            byte[] credentialsBytes = Encoding.UTF8.GetBytes(credentials);
            return Convert.ToBase64String(credentialsBytes);
        }

        /// <summary>
        /// Connect to Delock Socket Adapter
        /// </summary>
        /// <returns>True if connection successful, false otherwise</returns>
        public bool Connect()
        {
            try
            {
                if (_tcpClient != null && _tcpClient.Connected)
                    return true;

                _tcpClient = new TcpClient
                {
                    ReceiveTimeout = READ_TIMEOUT_MS,
                    SendTimeout = WRITE_TIMEOUT_MS
                };

                IAsyncResult result = _tcpClient.BeginConnect(_ipAddress, _port, null, null);
                bool success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(CONNECT_TIMEOUT_MS));

                if (!success)
                {
                    _tcpClient.Close();
                    return false;
                }

                _tcpClient.EndConnect(result);
                _networkStream = _tcpClient.GetStream();
                
                return true;
            }
            catch (Exception)
            {
                Disconnect();
                return false;
            }
        }

        /// <summary>
        /// Disconnect from Delock Socket Adapter
        /// </summary>
        public void Disconnect()
        {
            try
            {
                _networkStream?.Close();
                _tcpClient?.Close();
            }
            catch (Exception)
            {
                // Ignore disconnect errors
            }
            finally
            {
                _networkStream = null;
                _tcpClient = null;
            }
        }

        /// <summary>
        /// Check if connected to adapter
        /// </summary>
        public bool IsConnected => _tcpClient != null && _tcpClient.Connected;

        /// <summary>
        /// Get adapter instance name
        /// </summary>
        public string Name => _name;

        /// <summary>
        /// Get current IP address
        /// </summary>
        public string IPAddress => _ipAddress;

        /// <summary>
        /// Get current port
        /// </summary>
        public int Port => _port;

        /// <summary>
        /// Turn on specific socket outlet
        /// </summary>
        /// <param name="outlet">Outlet number (1-based, typically 1-4 or 1-8 depending on model)</param>
        /// <returns>True if command sent successfully, false otherwise</returns>
        public bool TurnOnSocket(int outlet)
        {
            if (outlet < 1)
                throw new ArgumentOutOfRangeException(nameof(outlet), "Outlet number must be 1 or greater");

            string auth = GetAuthorizationHeader();
            string command = string.Format(CMD_SOCKET_ON, outlet, _ipAddress, auth);
            
            // ✅ DEBUG: Zeige gesendetes Kommando
            System.Diagnostics.Debug.WriteLine($"[Delock] Sending ON command to outlet {outlet}:");
            System.Diagnostics.Debug.WriteLine(command.Replace("\r\n", "\\r\\n"));
            
            return SendCommandWithResponse(command, $"TurnOn Socket {outlet}");
        }

        /// <summary>
        /// Turn off specific socket outlet
        /// </summary>
        /// <param name="outlet">Outlet number (1-based, typically 1-4 or 1-8 depending on model)</param>
        /// <returns>True if command sent successfully, false otherwise</returns>
        public bool TurnOffSocket(int outlet)
        {
            if (outlet < 1)
                throw new ArgumentOutOfRangeException(nameof(outlet), "Outlet number must be 1 or greater");

            string auth = GetAuthorizationHeader();
            string command = string.Format(CMD_SOCKET_OFF, outlet, _ipAddress, auth);
            
            // ✅ DEBUG: Zeige gesendetes Kommando
            System.Diagnostics.Debug.WriteLine($"[Delock] Sending OFF command to outlet {outlet}:");
            System.Diagnostics.Debug.WriteLine(command.Replace("\r\n", "\\r\\n"));
            
            return SendCommandWithResponse(command, $"TurnOff Socket {outlet}");
        }

        /// <summary>
        /// Turn on all socket outlets
        /// </summary>
        /// <returns>True if command sent successfully, false otherwise</returns>
        public bool TurnOnAll()
        {
            string auth = GetAuthorizationHeader();
            string command = string.Format(CMD_ALL_ON, _ipAddress, auth);
            return SendCommand(command);
        }

        /// <summary>
        /// Turn off all socket outlets
        /// </summary>
        /// <returns>True if command sent successfully, false otherwise</returns>
        public bool TurnOffAll()
        {
            string auth = GetAuthorizationHeader();
            string command = string.Format(CMD_ALL_OFF, _ipAddress, auth);
            return SendCommand(command);
        }

        /// <summary>
        /// Toggle socket outlet state (on to off, or off to on)
        /// </summary>
        /// <param name="outlet">Outlet number (1-based)</param>
        /// <returns>True if command sent successfully, false otherwise</returns>
        public bool ToggleSocket(int outlet)
        {
            try
            {
                bool isOn = GetSocketStatus(outlet);
                return isOn ? TurnOffSocket(outlet) : TurnOnSocket(outlet);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Get status of all sockets
        /// </summary>
        /// <returns>Status string from adapter, or null if failed</returns>
        public string GetStatus()
        {
            string auth = GetAuthorizationHeader();
            string command = string.Format(CMD_SOCKET_STATUS, _ipAddress, auth);
            // Fix: Use a method that returns string, not bool
            return SendCommandWithResponseForStatus(command, "GetStatus");
        }

        /// <summary>
        /// Get status of specific socket outlet
        /// </summary>
        /// <param name="outlet">Outlet number (1-based)</param>
        /// <returns>True if socket is on, false if off</returns>
        public bool GetSocketStatus(int outlet)
        {
            try
            {
                string status = GetStatus();
                if (string.IsNullOrEmpty(status))
                    return false;

                // Parse status response to determine if socket is on
                // Format depends on Delock model, typically JSON or simple text
                return status.Contains($"outlet{outlet}:on") || status.Contains($"\"outlet{outlet}\":true");
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Check if Delock adapter is reachable on network
        /// </summary>
        /// <returns>True if adapter responds, false otherwise</returns>
        public bool IsReachable()
        {
            try
            {
                using (TcpClient testClient = new TcpClient())
                {
                    IAsyncResult result = testClient.BeginConnect(_ipAddress, _port, null, null);
                    bool success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(2000));
                    
                    if (success)
                    {
                        testClient.EndConnect(result);
                        return true;
                    }
                    
                    return false;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Send HTTP command to Delock adapter
        /// </summary>
        /// <param name="command">HTTP command string</param>
        /// <returns>True if command sent successfully, false otherwise</returns>
        private bool SendCommand(string command)
        {
            return SendCommandWithResponse(command, "Command");
        }

        /// <summary>
        /// Send HTTP command and read response - WITH DEBUG OUTPUT
        /// </summary>
        /// <param name="command">HTTP command string</param>
        /// <param name="action">Description of action for logging</param>
        /// <returns>True if successful, false otherwise</returns>
        private bool SendCommandWithResponse(string command, string action)
        {
            try
            {
                if (!EnsureConnected())
                {
                    System.Diagnostics.Debug.WriteLine($"[Delock] {action} FEHLGESCHLAGEN: Nicht verbunden");
                    return false;
                }

                byte[] data = Encoding.ASCII.GetBytes(command);
                _networkStream.Write(data, 0, data.Length);
                _networkStream.Flush();
                
                System.Diagnostics.Debug.WriteLine($"[Delock] {action}: Befehl gesendet ({data.Length} bytes)");
                
                // ✅ Warte darauf, dass Daten verfügbar werden (max 3 Sekunden)
                int waitCount = 0;
                while (!_networkStream.DataAvailable && waitCount < 30)
                {
                    Thread.Sleep(100);
                    waitCount++;
                }
                
                if (!_networkStream.DataAvailable)
                {
                    System.Diagnostics.Debug.WriteLine($"[Delock] {action}: Keine Daten nach {waitCount * 100}ms - als ERFOLG gewertet");
                    Thread.Sleep(200);
                    return true; // Viele IoT-Geräte senden keine Antwort
                }
                
                // ✅ Antwort lesen
                byte[] buffer = new byte[4096];
                int bytesRead = _networkStream.Read(buffer, 0, buffer.Length);
                
                if (bytesRead > 0)
                {
                    string response = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                    System.Diagnostics.Debug.WriteLine($"[Delock] {action} Antwort ({bytesRead} bytes):");
                    System.Diagnostics.Debug.WriteLine(response);
                    
                    // ✅ Prüfe auf Erfolgs-Indikatoren
                    bool success = response.Contains("200 OK") || response.Contains("success") || response.Contains("OK");
                    System.Diagnostics.Debug.WriteLine($"[Delock] {action}: {(success ? "ERFOLG" : "FEHLGESCHLAGEN")}");
                    
                    Thread.Sleep(100);
                    return success;
                }
                
                System.Diagnostics.Debug.WriteLine($"[Delock] {action}: Keine Antwort empfangen");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Delock] {action} EXCEPTION: {ex.Message}");
                Disconnect();
                return false;
            }
        }

        /// <summary>
        /// Send HTTP command and read response, returning the response string (for status queries)
        /// </summary>
        /// <param name="command">HTTP command string</param>
        /// <param name="action">Description of action for logging</param>
        /// <returns>Response string if successful, null otherwise</returns>
        private string SendCommandWithResponseForStatus(string command, string action)
        {
            try
            {
                if (!EnsureConnected())
                {
                    System.Diagnostics.Debug.WriteLine($"[Delock] {action} FEHLGESCHLAGEN: Nicht verbunden");
                    return null;
                }

                byte[] data = Encoding.ASCII.GetBytes(command);
                _networkStream.Write(data, 0, data.Length);
                _networkStream.Flush();

                System.Diagnostics.Debug.WriteLine($"[Delock] {action}: Befehl gesendet ({data.Length} bytes)");

                // ✅ Warte auf Daten
                int waitCount = 0;
                while (!_networkStream.DataAvailable && waitCount < 30)
                {
                    Thread.Sleep(100);
                    waitCount++;
                }

                if (!_networkStream.DataAvailable)
                {
                    System.Diagnostics.Debug.WriteLine($"[Delock] {action}: Keine Daten nach {waitCount * 100}ms");
                    return null;
                }

                // ✅ Antwort lesen
                byte[] buffer = new byte[4096];
                int bytesRead = _networkStream.Read(buffer, 0, buffer.Length);

                if (bytesRead > 0)
                {
                    string response = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                    System.Diagnostics.Debug.WriteLine($"[Delock] {action} Antwort ({bytesRead} bytes):");
                    System.Diagnostics.Debug.WriteLine(response);
                    Thread.Sleep(100);
                    return response;
                }

                System.Diagnostics.Debug.WriteLine($"[Delock] {action}: Keine Antwort empfangen");
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Delock] {action} EXCEPTION: {ex.Message}");
                Disconnect();
                return null;
            }
        }

        /// <summary>
        /// Ensure connection is established, attempt reconnection if not
        /// </summary>
        /// <returns>True if connected, false otherwise</returns>
        private bool EnsureConnected()
        {
            // ✅ Für IoT-Geräte: Immer neu verbinden
            // Viele billige Geräte schließen die Verbindung nach jedem Request
            Disconnect();
            return Connect();
        }

        /// <summary>
        /// Release all resources used by the DelockSocketAdapter
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
                    Disconnect();
                    _networkStream?.Dispose();
                    _tcpClient?.Dispose();
                }
                _disposed = true;
            }
        }

        /// <summary>
        /// Finalizer for DelockSocketAdapter
        /// </summary>
        ~DelockSocketAdapter()
        {
            Dispose(false);
        }
    }
}