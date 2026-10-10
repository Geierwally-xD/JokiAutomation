using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Renci.SshNet;
using Renci.SshNet.Sftp;
using Renci.SshNet.Common;
using System.Threading;
using System.Windows.Forms;
using System.IO;
using System.Diagnostics;

namespace JokiAutomation
{
    partial class RasPi
    {

        public void initRasPi(Form1 winForm)
        {
            _rasPiForm = winForm;
            _staticRasPiForm = winForm;

            // Determine config path (from environment variable or application directory)
            string configPath = _JokiAutomationPath;
            if (string.IsNullOrEmpty(configPath))
            {
                configPath = AppDomain.CurrentDomain.BaseDirectory;
            }
            if (!configPath.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                configPath += Path.DirectorySeparatorChar;
            }

            // Load Raspberry Pi configuration
            string raspberryPiCfgPath = configPath + "RaspberryPi.cfg";
            if (File.Exists(raspberryPiCfgPath))
            {
                string[] raspberryPiConfig = System.IO.File.ReadAllLines(raspberryPiCfgPath);
                if (raspberryPiConfig.Length >= 3)
                {
                    _rasPiConfig[1] = raspberryPiConfig[1];  // username
                    _rasPiConfig[2] = raspberryPiConfig[2];  // password
                }
            }

            // Load IP address ONLY from Network.cfg - required
            string networkCfgPath = configPath + "Network.cfg";
            if (File.Exists(networkCfgPath))
            {
                string[] networkConfig = System.IO.File.ReadAllLines(networkCfgPath);
                foreach (string line in networkConfig)
                {
                    if (!line.StartsWith("#") && line.Contains(";"))
                    {
                        string[] parts = line.Split(';');
                        if (parts.Length >= 2 && parts[0].Trim().Equals("RaspberryPi_Main", StringComparison.OrdinalIgnoreCase))
                        {
                            _rasPiConfig[0] = parts[1].Trim();
                            break;
                        }
                    }
                }
            }
            else
            {
                // Network.cfg not found - set IP to empty (will cause connection error)
                _rasPiConfig[0] = "";
            }
        }


        private static void HandleKeyEvent(Object sender, Renci.SshNet.Common.AuthenticationPromptEventArgs e)
        {
            foreach (Renci.SshNet.Common.AuthenticationPrompt prompt in e.Prompts)
            {
                if (prompt.Request.IndexOf("Password:", StringComparison.InvariantCultureIgnoreCase) != -1)
                {
                    prompt.Response = _rasPiConfig[2];
                }
            }
        }

        public void rasPiDefaultSwitch() //reset audio - to sumary signal, IR to laptop view beamer to HDMI1
        {
            SshRequestRasPi(InfraredControl.IR_SEQUENCE, InfraredControl.IR_RESET);
        }

        [Obsolete("Legacy reset adapter. Use RasPiResetAsync for productive code.")]
        public void rasPiStop()
        {
            throw new NotSupportedException("rasPiStop() is legacy. Use RasPiResetAsync().");
        }

        // Legacy fire-and-forget adapter. Use RasPiExecuteAsync for productive code.
        [Obsolete("Legacy fire-and-forget adapter. Use RasPiExecuteAsync for productive code.")]
        public void rasPiExecute(int command, int ID)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await RasPiExecuteAsync(command, ID, cancellationToken: CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _rasPiForm?._logDat?.sendInfoMessage(
                        $"JokiAutomation\n[RASPI_ADAPTER_ERROR] Command={command} ID={ID} Exception={ex.GetType().Name} Message={ex.Message}");
                }
            });
        }

        // Hilfsfunktion zum Laden der Konfiguration und IP aus Network.cfg
        private static string GetRaspberryPiIpFromNetwork()
        {
            // Suche Network.cfg in verschiedenen Locations (Priorität):
            // 1. Umgebungsvariable JokiAutomation
            // 2. AppDomain.BaseDirectory (bin-Verzeichnis)
            // 3. ParentDirectory vom bin (Projekt-Root)

            string[] searchPaths = new string[3];

            // 1. Umgebungsvariable
            string envPath =
                Environment.GetEnvironmentVariable("JokiAutomation", EnvironmentVariableTarget.Process) ??
                Environment.GetEnvironmentVariable("JokiAutomation", EnvironmentVariableTarget.User) ??
                Environment.GetEnvironmentVariable("JokiAutomation", EnvironmentVariableTarget.Machine);
            if (!string.IsNullOrEmpty(envPath) && !envPath.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                envPath += Path.DirectorySeparatorChar;
            }
            searchPaths[0] = envPath;

            // 2. AppDomain.BaseDirectory (bin-Verzeichnis)
            string basePath = AppDomain.CurrentDomain.BaseDirectory;
            if (!basePath.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                basePath += Path.DirectorySeparatorChar;
            }
            searchPaths[1] = basePath;

            // 3. ParentDirectory vom bin (Projekt-Root)
            string parentPath = Directory.GetParent(basePath).FullName;
            if (!parentPath.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                parentPath += Path.DirectorySeparatorChar;
            }
            searchPaths[2] = parentPath;

            // Suche die Datei
            foreach (string searchPath in searchPaths)
            {
                if (string.IsNullOrEmpty(searchPath))
                    continue;

                string networkCfgPath = searchPath + "Network.cfg";
                System.Diagnostics.Debug.WriteLine($"DEBUG: Suche Network.cfg unter: {networkCfgPath}");

                if (File.Exists(networkCfgPath))
                {
                    System.Diagnostics.Debug.WriteLine($"DEBUG: Network.cfg gefunden!");
                    return ParseRaspberryPiIpFromFile(networkCfgPath);
                }
            }

            System.Diagnostics.Debug.WriteLine($"DEBUG: Network.cfg in keinem Verzeichnis gefunden!");
            return "";
        }

        // Parse die Network.cfg Datei
        private static string ParseRaspberryPiIpFromFile(string filePath)
        {
            System.Diagnostics.Debug.WriteLine($"DEBUG: Lese {filePath}");

            try
            {
                string[] networkConfig = File.ReadAllLines(filePath);
                System.Diagnostics.Debug.WriteLine($"DEBUG: {networkConfig.Length} Zeilen gelesen");

                foreach (string line in networkConfig)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    System.Diagnostics.Debug.WriteLine($"DEBUG: Zeile: '{line}'");

                    if (!line.StartsWith("#") && line.Contains(";"))
                    {
                        string[] parts = line.Split(';');
                        System.Diagnostics.Debug.WriteLine($"DEBUG: Parts Count: {parts.Length}, Part[0]: '{parts[0].Trim()}'");

                        if (parts.Length >= 2 && parts[0].Trim().Equals("RaspberryPi_Main", StringComparison.OrdinalIgnoreCase))
                        {
                            string result = parts[1].Trim();
                            System.Diagnostics.Debug.WriteLine($"DEBUG: IP gefunden: {result}");
                            return result;
                        }
                    }
                }

                System.Diagnostics.Debug.WriteLine($"DEBUG: RaspberryPi_Main nicht in Datei gefunden!");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"DEBUG: Fehler beim Lesen: {ex.Message}");
            }

            return "";
        }

        // raspberry pi request over SSH.NET
        public static void rasPiThreadStart(string commandValue, string idValue)
        {
            try
            {
                KeyboardInteractiveAuthenticationMethod keybAuth = new KeyboardInteractiveAuthenticationMethod(_rasPiConfig[1]);
                PasswordAuthenticationMethod pauth = new PasswordAuthenticationMethod(_rasPiConfig[1], _rasPiConfig[2]);
                keybAuth.AuthenticationPrompt += new EventHandler<AuthenticationPromptEventArgs>(HandleKeyEvent);

                // Lade IP immer aus Network.cfg
                string ipAddress = GetRaspberryPiIpFromNetwork();

                if (string.IsNullOrEmpty(ipAddress))
                {
                    lock (_threadResultString)
                    {
                        _threadResultString[0] = "Fehler: RaspberryPi_Main IP nicht in Network.cfg gefunden!";
                    }
                    if (_staticRasPiForm != null)
                    {
                        _staticRasPiForm._logDat.sendInfoMessage(_threadResultString[0]);
                    }
                    return;
                }

                lock (_threadResultString)
                {
                    _threadResultString[1] = $"Versuche Verbindung zu: {ipAddress}:22 mit User: {_rasPiConfig[1]}";
                }
                if (_staticRasPiForm != null)
                {
                    _staticRasPiForm._logDat.sendInfoMessage(_threadResultString[1]);
                }

                ConnectionInfo connectionInfo = new ConnectionInfo(ipAddress, 22, _rasPiConfig[1], pauth, keybAuth);
                // Erhöhe das Connect-Timeout, damit langsame Netzwerkverbindungen nicht sofort abbrechen
                connectionInfo.Timeout = TimeSpan.FromSeconds(30);
                using (SshClient sshClient = new SshClient(connectionInfo))
                {
                    sshClient.KeepAliveInterval = TimeSpan.FromSeconds(30);
                    // Stelle sicher, dass die verwendete ConnectionInfo ein großzügigeres Timeout hat
                    sshClient.ConnectionInfo.Timeout = TimeSpan.FromSeconds(30);

                    lock (_threadResultString)
                    {
                        _threadResultString[2] = "Starte SSH Connect...";
                    }
                    if (_staticRasPiForm != null)
                    {
                        _staticRasPiForm._logDat.sendInfoMessage(_threadResultString[2]);
                    }

                    sshClient.Connect();

                    lock (_threadResultString)
                    {
                        _threadResultString[3] = "SSH verbunden!";
                    }
                    if (_staticRasPiForm != null)
                    {
                        _staticRasPiForm._logDat.sendInfoMessage(_threadResultString[3]);
                    }

                    string escapedPassword = _rasPiConfig[2].Replace("'", "'\"'\"'");
                    string commandString = string.Format(
                        "echo '{2}' | sudo -S /home/pi/JokiAutomation/RasPiAutomation {0} {1}",
                        commandValue,
                        idValue,
                        escapedPassword);

                    SshCommand cmd = sshClient.CreateCommand(commandString);
                    cmd.CommandTimeout = TimeSpan.FromSeconds(75);

                    lock (_threadResultString)
                    {
                        _threadResultString[0] = "Movement started (blocking)";
                    }
                    if (_staticRasPiForm != null)
                    {
                        _staticRasPiForm._logDat.sendInfoMessage(_threadResultString[0]);
                        _staticRasPiForm._logDat.sendInfoMessage($"[RASPI_SSH_SEND] Command={commandValue} Id={idValue}");
                    }

                    cmd.Execute();

                    int exit = cmd.ExitStatus.HasValue ? cmd.ExitStatus.Value : -1;
                    string stdout = cmd.Result;
                    string stderr = cmd.Error;

                    lock (_threadResultString)
                    {
                        _threadResultString[0] = exit == 0
                            ? "Movement completed"
                            : string.Format("Movement failed (exit={0})", exit);
                    }
                    if (_staticRasPiForm != null)
                    {
                        _staticRasPiForm._logDat.sendInfoMessage(_threadResultString[0]);
                        _staticRasPiForm._logDat.sendInfoMessage($"[RASPI_SSH_RESULT] Command={commandValue} Id={idValue} Exit={exit}");
                        if (!string.IsNullOrEmpty(stdout))
                        {
                            _staticRasPiForm._logDat.sendInfoMessage("=== STDOUT ===");
                            _staticRasPiForm._logDat.sendInfoMessage(stdout);
                        }
                        if (!string.IsNullOrEmpty(stderr))
                        {
                            _staticRasPiForm._logDat.sendInfoMessage("=== STDERR ===");
                            _staticRasPiForm._logDat.sendInfoMessage(stderr);
                        }
                    }

                    sshClient.Disconnect();
                }
            }
            catch (System.Net.Sockets.SocketException sockEx)
    {
        lock (_threadResultString)
        {
            _threadResultString[0] = "Netzwerk-Fehler: Kann Raspberry Pi nicht erreichen!";
            _threadResultString[1] = $"IP: {_rasPiConfig[0]}, Port: 22";
            _threadResultString[2] = $"SocketException: {sockEx.Message}";
            _threadResultString[3] = $"ErrorCode: {sockEx.SocketErrorCode}";
            _threadResultString[4] = "Prüfen Sie:\n- Ist der RasPi eingeschaltet?\n- Ist die IP korrekt?\n- Ist SSH aktiviert?\n- Firewall-Blockierung?";
        }
        if (_staticRasPiForm != null)
        {
            _staticRasPiForm._logDat.sendInfoMessage(_threadResultString[0]);
            _staticRasPiForm._logDat.sendInfoMessage(_threadResultString[1]);
            _staticRasPiForm._logDat.sendInfoMessage(_threadResultString[2]);
            _staticRasPiForm._logDat.sendInfoMessage(_threadResultString[3]);
            _staticRasPiForm._logDat.sendInfoMessage(_threadResultString[4]);
        }
    }
    catch (Renci.SshNet.Common.SshAuthenticationException authEx)
    {
        lock (_threadResultString)
        {
            _threadResultString[0] = "SSH Authentifizierung fehlgeschlagen!";
            _threadResultString[1] = $"User: {_rasPiConfig[1]}";
            _threadResultString[2] = $"Fehler: {authEx.Message}";
            _threadResultString[4] = "Prüfen Sie Username und Passwort in RaspberryPi.cfg";
        }
        if (_staticRasPiForm != null)
        {
            _staticRasPiForm._logDat.sendInfoMessage(_threadResultString[0]);
            _staticRasPiForm._logDat.sendInfoMessage(_threadResultString[1]);
            _staticRasPiForm._logDat.sendInfoMessage(_threadResultString[2]);
            _staticRasPiForm._logDat.sendInfoMessage(_threadResultString[4]);
        }
    }
    catch (Exception e)
    {
        lock (_threadResultString)
        {
            _threadResultString[0] = "Error in Raspberry Pi thread!\n" + e.Message;
            _threadResultString[1] = $"Exception Type: {e.GetType().Name}";
            _threadResultString[4] = "Exception:\n" + e.ToString();
        }
        if (_staticRasPiForm != null)
        {
            _staticRasPiForm._logDat.sendInfoMessage(_threadResultString[0]);
            _staticRasPiForm._logDat.sendInfoMessage(_threadResultString[1]);
            _staticRasPiForm._logDat.sendInfoMessage(_threadResultString[4]);
        }
    }
}

        // Direct synchronous SSH command execution via SSH.NET.
        public void SshRequestRasPi(int command, int ID)
        {
            string commandString = command.ToString();
            string idString = ID.ToString();

            try
            {
                string ipAddress = GetRaspberryPiIpFromNetwork();
                if (string.IsNullOrEmpty(ipAddress))
                {
                    _rasPiForm?._logDat?.sendInfoMessage("Fehler: RaspberryPi_Main IP nicht in Network.cfg gefunden!");
                    return;
                }

                KeyboardInteractiveAuthenticationMethod keybAuth = new KeyboardInteractiveAuthenticationMethod(_rasPiConfig[1]);
                PasswordAuthenticationMethod pauth = new PasswordAuthenticationMethod(_rasPiConfig[1], _rasPiConfig[2]);
                keybAuth.AuthenticationPrompt += new EventHandler<AuthenticationPromptEventArgs>(HandleKeyEvent);

                ConnectionInfo connectionInfo = new ConnectionInfo(ipAddress, 22, _rasPiConfig[1], pauth, keybAuth)
                {
                    Timeout = TimeSpan.FromSeconds(30)
                };

                using (SshClient sshClient = new SshClient(connectionInfo))
                {
                    sshClient.KeepAliveInterval = TimeSpan.FromSeconds(30);
                    sshClient.Connect();

                    if (command > 0)
                    {
                        _rasPiForm?._logDat?.sendInfoMessage(
                            $"JokiAutomation\n[RASPI_SSH_SEND] Host='{ipAddress}' Command={commandString} ID={idString}");
                        string escapedPassword = _rasPiConfig[2].Replace("'", "'\"'\"'");
                        string remoteCommand = "echo '" + escapedPassword + "' | sudo -S /home/pi/JokiAutomation/RasPiAutomation " + commandString + " " + idString;

                        SshCommand cmd = sshClient.CreateCommand(remoteCommand);
                        cmd.CommandTimeout = TimeSpan.FromSeconds(75);
                        cmd.Execute();

                        int exit = cmd.ExitStatus.HasValue ? cmd.ExitStatus.Value : -1;
                        if (!string.IsNullOrWhiteSpace(cmd.Result))
                        {
                            _rasPiForm?._logDat?.sendInfoMessage(cmd.Result);
                        }
                        if (!string.IsNullOrWhiteSpace(cmd.Error))
                        {
                            _rasPiForm?._logDat?.sendInfoMessage(cmd.Error);
                        }

                        _rasPiForm?._logDat?.sendInfoMessage(
                            $"JokiAutomation\n[RASPI_SSH_RESULT] Command={commandString} ID={idString} ExitCode={exit}");
                        _rasPiForm?._logDat?.sendInfoMessage(exit == 0 ? "RasPi command completed" : $"RasPi command failed (exit={exit})");
                    }
                    else
                    {
                        throw new NotSupportedException("SshRequestRasPi(0, 0) is legacy. Use RasPiResetAsync().");
                    }

                    sshClient.Disconnect();
                }
            }
            catch (Exception ex)
            {
                _rasPiForm?._logDat?.sendInfoMessage("Error during SSH RasPi request: " + ex.Message);
            }
        }

        // write autozoom configuration binary to Raspberry Pi
        public void UploadBinary(Byte[] data, string filePath)
        {
            try
            {
                KeyboardInteractiveAuthenticationMethod keybAuth = new KeyboardInteractiveAuthenticationMethod(_rasPiConfig[1]);
                PasswordAuthenticationMethod pauth = new PasswordAuthenticationMethod(_rasPiConfig[1], _rasPiConfig[2]);
                keybAuth.AuthenticationPrompt += new EventHandler<Renci.SshNet.Common.AuthenticationPromptEventArgs>(HandleKeyEvent);

                // Load IP address from Network.cfg
                string ipAddress = GetRaspberryPiIpFromNetwork();
                if (string.IsNullOrEmpty(ipAddress))
                {
                    _rasPiForm._logDat.sendInfoMessage("Error: RaspberryPi_Main IP not found in Network.cfg\n");
                    return;
                }
                ConnectionInfo connectionInfo = new ConnectionInfo(ipAddress, 22, _rasPiConfig[1], pauth, keybAuth);
                connectionInfo.Timeout = TimeSpan.FromSeconds(30);

                var client = new SftpClient(connectionInfo);
                _rasPiForm._logDat.sendInfoMessage("upload binary data to Raspberry Pi\n");
                client.Connect();
                var stream = new MemoryStream();
                stream.Write(data, 0, data.Length);
                stream.Position = 0;
                client.UploadFile(stream, filePath);
                client.Disconnect();
            }
            catch (Exception e)
            {
                _rasPiForm._logDat.sendInfoMessage("Error on upload binary data\n" + e.Message);
            }
        }

        public Byte[] DownloadBinary(string filePath, int length)
        {
            Byte[] data = new byte[length];
            try
            {
                KeyboardInteractiveAuthenticationMethod keybAuth = new KeyboardInteractiveAuthenticationMethod(_rasPiConfig[1]);
                PasswordAuthenticationMethod pauth = new PasswordAuthenticationMethod(_rasPiConfig[1], _rasPiConfig[2]);
                keybAuth.AuthenticationPrompt += new EventHandler<Renci.SshNet.Common.AuthenticationPromptEventArgs>(HandleKeyEvent);

                // Load IP address from Network.cfg if available, otherwise use default from _rasPiConfig
                // Load IP address from Network.cfg
                string ipAddress = GetRaspberryPiIpFromNetwork();
                if (string.IsNullOrEmpty(ipAddress))
                {
                    _rasPiForm._logDat.sendInfoMessage("Error: RaspberryPi_Main IP not found in Network.cfg\n");
                    return data;
                }
                ConnectionInfo connectionInfo = new ConnectionInfo(ipAddress, 22, _rasPiConfig[1], pauth, keybAuth);
                connectionInfo.Timeout = TimeSpan.FromSeconds(30);
                var client = new SftpClient(connectionInfo);
                _rasPiForm._logDat.sendInfoMessage("download binary data from Raspberry Pi\n");
                client.Connect();
                var stream = new MemoryStream();
                client.DownloadFile( filePath, stream);
                stream.Position = 0;
                int read = stream.Read(data, 0, data.Length);
                client.Disconnect();
            }
            catch (Exception e)
            {
                _rasPiForm._logDat.sendInfoMessage("Error on download binary data\n" + e.Message);
            }


            return data;
        }

        public Thread _RasPiThread = null;
        private Form1 _rasPiForm;       
        private static Form1 _staticRasPiForm;      
        private static String[] _threadResultString = new String[5];
        private static string _JokiAutomationPath =
            Environment.GetEnvironmentVariable("JokiAutomation", EnvironmentVariableTarget.Process) ??
            Environment.GetEnvironmentVariable("JokiAutomation", EnvironmentVariableTarget.User) ??
            Environment.GetEnvironmentVariable("JokiAutomation", EnvironmentVariableTarget.Machine);
        static private string[] _rasPiConfig = { "", "pi", "raspberry" }; // IP loaded from Network.cfg, default login data
        private readonly SemaphoreSlim _rasPiResetSemaphore = new SemaphoreSlim(1, 1);
    }
}
