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
                if (raspberryPiConfig.Length >= 4)
                {
                    _rasPiConfig[1] = raspberryPiConfig[1];  // username
                    _rasPiConfig[2] = raspberryPiConfig[2];  // password
                    _rasPiConfig[3] = raspberryPiConfig[3];  // plink path
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
            PuttyRequestRasPi(InfraredControl.IR_SEQUENCE, InfraredControl.IR_RESET);
        }

        public void rasPiStop()
        {
           PuttyRequestRasPi(0,0);
        }

        // raspberry pi request over Renci ssh.Net
        public void rasPiExecute(int command, int ID)
        {
            string commandString = command.ToString();
            string idString = ID.ToString();
            int commandLineInstances = 0;
            for (UInt16 i = 0; i < 5; i++)
            {
                _threadResultString[i] = "";
            }
            try
            {
                // count running command line instances of JoKiAutomation
                Process[] localByName = Process.GetProcessesByName("JoKiAutomation");
                for (int i = 0; i < localByName.Length; i++)
                {
                    if (localByName[i].MainWindowTitle == "")
                    {
                        commandLineInstances++;
                    }
                }
                if (commandLineInstances < 2) // maximum one instance of JoKiAutomation from command line allowed
                {
                    if (_threadResultString[0] != "") // Lesen ohne Lock
                    {
                        _rasPiForm._logDat.sendInfoMessage(_threadResultString[0]);
                    }

                    // Load IP address from Network.cfg
                    string ipAddress = GetRaspberryPiIpFromNetwork();

                    if (string.IsNullOrEmpty(ipAddress))
                    {
                        _rasPiForm._logDat.sendInfoMessage("Fehler: RaspberryPi_Main IP nicht in Network.cfg gefunden!");
                        return;
                    }

                    KeyboardInteractiveAuthenticationMethod keybAuth = new KeyboardInteractiveAuthenticationMethod(_rasPiConfig[1]);
                    PasswordAuthenticationMethod pauth = new PasswordAuthenticationMethod(_rasPiConfig[1], _rasPiConfig[2]);
                    keybAuth.AuthenticationPrompt += new EventHandler<Renci.SshNet.Common.AuthenticationPromptEventArgs>(HandleKeyEvent);

                    _RasPiThread = new Thread(() => rasPiThreadStart(commandString, idString));
                    _RasPiThread.SetApartmentState(ApartmentState.STA);
                    _rasPiForm._logDat.sendInfoMessage("start Raspberry Pi RasPi-Automation-application " + commandString + " " + idString);
                    _RasPiThread.Start();
                    if (_threadResultString[0] != "") // Erneutes Lesen ohne Lock
                    {
                        _rasPiForm._logDat.sendInfoMessage(_threadResultString[0]);
                        _rasPiForm._logDat.sendInfoMessage(_threadResultString[1]);
                        _rasPiForm._logDat.sendInfoMessage(_threadResultString[2]);
                        _rasPiForm._logDat.sendInfoMessage(_threadResultString[3]);
                        _rasPiForm._logDat.sendInfoMessage(_threadResultString[4]);
                    }
                }
            }
            catch (Exception e)
            {
                MessageBox.Show("Error during Start Raspberry Pi!\n" + e.Message);
                if (_threadResultString[0] != "")
                {
                    _rasPiForm._logDat.sendInfoMessage(_threadResultString[0]);
                    _rasPiForm._logDat.sendInfoMessage(_threadResultString[1]);
                    _rasPiForm._logDat.sendInfoMessage(_threadResultString[2]);
                    _rasPiForm._logDat.sendInfoMessage(_threadResultString[3]);
                    _rasPiForm._logDat.sendInfoMessage(_threadResultString[4]);
                }
            }
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

        // raspberry pi request over putty command line
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

                    string commandString = string.Format(
                        "echo {2} | sudo -S /home/pi/JokiAutomation/RasPiAutomation {0} {1}",
                        commandValue,
                        idValue,
                        _rasPiConfig[2]);

                    SshCommand cmd = sshClient.CreateCommand(commandString);
                    cmd.CommandTimeout = TimeSpan.FromSeconds(75);

                    lock (_threadResultString)
                    {
                        _threadResultString[0] = "Movement started (blocking)";
                        _threadResultString[4] = commandString;
                    }
                    if (_staticRasPiForm != null)
                    {
                        _staticRasPiForm._logDat.sendInfoMessage(_threadResultString[0]);
                        _staticRasPiForm._logDat.sendInfoMessage("Command: " + _threadResultString[4]);
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
                        _threadResultString[4] = "=== STDOUT ===\n" + stdout + "\n\n=== STDERR ===\n" + stderr;
                    }
                    if (_staticRasPiForm != null)
                    {
                        _staticRasPiForm._logDat.sendInfoMessage(_threadResultString[0]);
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

        public void PuttyRequestRasPi(int command, int ID)
        {
            //string cmd = null;
            _commandString = command.ToString();
            _idString = ID.ToString();
            m_szFeedback = "Feedback from: " + _rasPiConfig[0] + "\r\n";
            ProcessStartInfo psi = new ProcessStartInfo()
            {
                FileName = _rasPiConfig[3], // A const or a readonly string that points to the plink executable
                Arguments = String.Format("-ssh {0}@{1} -pw {2}", _rasPiConfig[1] /*userName*/, _rasPiConfig[0] /*remoteHost*/, _rasPiConfig[2] /*password*/),
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Process p = Process.Start(psi);

            m_objLock = new Object();
            m_blnDoRead = true;

            AsyncReadFeedback(p.StandardOutput); // start the async read of stdout
            AsyncReadFeedback(p.StandardError); // start the async read of stderr

            StreamWriter strw = p.StandardInput;
            if (command > 0)
            {
                _rasPiForm._logDat.sendInfoMessage("start Raspberry Pi RasPi-Automation-application over Putty " + _commandString + " " + _idString);
                strw.WriteLine("echo " + _rasPiConfig[2] + " | sudo -S /home/pi/JokiAutomation/RasPiAutomation " + _commandString + " " + _idString);
            }
            else
            {
                _rasPiForm._logDat.sendInfoMessage("reset Raspberry Pi RasPi-Automation-application over Putty ");
                strw.WriteLine("echo " + _rasPiConfig[2] + " | sudo -S killall -SIGKILL RasPiAutomation");    // stopp all RasPiAutomation 
                strw.WriteLine("gpio export 5 out");
                strw.WriteLine("gpio -g write 5 0");
                strw.WriteLine("gpio export 6 out");
                strw.WriteLine("gpio -g write 6 0");
                strw.WriteLine("gpio export 7 out"); // IR out
                strw.WriteLine("gpio -g write 7 0"); // IR out
                strw.WriteLine("gpio export 13 out");
                strw.WriteLine("gpio -g write 13 0");
                strw.WriteLine("gpio export 19 out");
                strw.WriteLine("gpio -g write 19 0");
                strw.WriteLine("gpio export 26 out");
                strw.WriteLine("gpio -g write 26 0");
                strw.WriteLine("gpio export 16 out");
                strw.WriteLine("gpio -g write 16 0");
                strw.WriteLine("gpio export 20 out");
                strw.WriteLine("gpio -g write 20 0");
                strw.WriteLine("gpio export 21 out");
                strw.WriteLine("gpio -g write 21 0");
                strw.WriteLine("stty -F /dev/ttyUSB0 9600 raw -echo"); // configure serial port
                strw.WriteLine("echo -n '(MX*:RES!)' > /dev/ttyUSB0"); // send reset via serial port to audiomix
            }
            strw.WriteLine("exit"); // send exit command at the end

            p.WaitForExit(); // block thread until remote operations are done
            _rasPiForm._logDat.sendInfoMessage(m_szFeedback);
        }

        // trhread helper method for PuttyRequestRasPi
        public void AsyncReadFeedback(StreamReader strr)
        {
            Thread trdr = new Thread(new ParameterizedThreadStart(__ctReadFeedback));
            trdr.Start(strr);
        }
        // trhread helper method for PuttyRequestRasPi
        private void __ctReadFeedback(Object objStreamReader)
        {
            StreamReader strr = (StreamReader)objStreamReader;
            string line;
            while (!strr.EndOfStream && m_blnDoRead)
            {
                line = strr.ReadLine();
                // lock the feedback buffer (since we don't want some messy stdout/err mix string in the end)
                lock (m_objLock) { m_szFeedback += line + "\r\n"; }
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
        private string _commandString = null;
        private string _idString = null;
        private Form1 _rasPiForm;       
        private static Form1 _staticRasPiForm;      
        private static String[] _threadResultString = new String[5];
        private static string _JokiAutomationPath =
            Environment.GetEnvironmentVariable("JokiAutomation", EnvironmentVariableTarget.Process) ??
            Environment.GetEnvironmentVariable("JokiAutomation", EnvironmentVariableTarget.User) ??
            Environment.GetEnvironmentVariable("JokiAutomation", EnvironmentVariableTarget.Machine);
        private String m_szFeedback; // hold feedback data
        private Object m_objLock;    // lock object
        private Boolean m_blnDoRead; // boolean value keeping up the read (may be used to interrupt the reading process)
        static private string[] _rasPiConfig = { "", "pi", "raspberry", "C:/Program Files/PuTTY/plink" }; // IP loaded from Network.cfg, default login data
    }
}
