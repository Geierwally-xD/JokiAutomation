using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CanonRemoteControl;
using CanonPtzCommon;

namespace JokiAutomation
{
    public partial class Form1 : Form
    {
        // Password & Timer Constants
        private const int PASSWORD_TIMEOUT_SECONDS = 30;
        private const int TIMER_INTERVAL_MS = 1000;
        private const int DEFAULT_CAMERA_POSITION = 5;

        // Audio Channel Constants
        private const int AUDIO_CHANNELS_1_AND_2 = 0x03;
        private const int AUDIO_CHANNEL_COUNT = 4;

        // ATEM HDMI Input Enum
        public enum ATEMInput
        {
            Laptop = 1,             // HDMI 1: Laptop  
            GoPro = 2,              // HDMI 2: GoPro Actionkamera
            CanonPtzMain = 3,       // HDMI 3: Canon PTZ (Hauptkamera)
            CanonPtzPreacher = 4    // HDMI 4: Canon PTZ (Predigtkamera)
        }

        // Tab Indices
        private const int TAB_INDEX_INFRARED = 1;
        private const int TAB_INDEX_AUDIO = 2;
        private const int TAB_INDEX_POSITION_CONTROL = 3;
        private const int TAB_INDEX_AUTOZOOM_CONFIG = 4;

        // Position Control Constants
        private const int NULL_POSITION_INDEX = 21;
        private const int TEMP_ZOOM_INDEX = 20;

        // Requested Function IDs
        private const uint FUNCTION_CALIBRATE_MAGNETOMETER = 1;
        private const uint FUNCTION_TEACH_AUDIO = 2;
        private const uint FUNCTION_TEACH_INFRARED = 3;
        private const uint FUNCTION_TEACH_POSITION = 4;
        private const uint FUNCTION_CALIBRATE_GYROSCOPE = 5;
        private const uint FUNCTION_TEACH_NULL_POSITION = 6;

        // Configuration
        private const string NETWORK_CONFIG_FILE = "Network.cfg";
        private Dictionary<string, NetworkDevice> _networkDevices;
        private Dictionary<string, DelockSocketAdapter> _delockAdapters;
        private Dictionary<string, string> _userPasswords;

        // PTZ Camera Configuration
        private const string PTZ_MODE_CONFIG_KEY = "PTZ_CAM";
        private const string CANON_CAMERA_CONFIG_KEY = "CANON_CRN100";
        private const string PTZ_PRESET_RECALL_PATH_CONFIG_KEY = "PTZ_PRESET_RECALL_PATH";
        private const string PTZ_PRESET_RECALL_PATHS_CONFIG_KEY = "PTZ_PRESET_RECALL_PATHS";
        private bool _isPtzCameraMode;
        private ICanonPtzController _canonPtzController;
        private string _ptzPresetRecallPath;
        private List<string> _ptzPresetRecallFallbackPaths = new List<string>();

        private readonly object _operationLock = new object();
        //private volatile bool _isOperationInProgress = false;
        //private Keys _lastPtzKeyPressed = Keys.None; // <-- NEU: Für PTZ Tastatur-Steuerung

        // Network Device Information
        private class NetworkDevice
        {
            public string IPAddress { get; set; }
            public int Port { get; set; }
            public string Username { get; set; }
            public string Password { get; set; }

            public NetworkDevice(string ipAddress, int port = 80, string username = null, string password = null)
            {
                IPAddress = ipAddress;
                Port = port;
                Username = username;
                Password = password;
            }

            public bool IsDelockDevice => !string.IsNullOrEmpty(Username) && !string.IsNullOrEmpty(Password);
            public bool IsUserCredential => IPAddress == null; // ✅ NEU: User entries have no IP
        }

        public Form1()
        {
            InitializeComponent();
            _logDat.initLogData(this);
            _infraredControl.InitIR(this);
            _audioMix.initAudio(this);

            InitializeNetworkConfig();
            _ = InitializeCanonPtzControlAsync();

            _positionControl.initPC(this, _isPtzCameraMode, _canonPtzController);

            _autoZoom.initAZ(this);
            InitializeATEMControl();
            //rvtest InitializeRokuTV();

            _Inputtimer.Interval = TIMER_INTERVAL_MS;  // check rich text box each 1000ms
            _Inputtimer.Tick += new System.EventHandler(Inputtimer_Elapsed);

            // Configure UI based on PTZ mode (must be after InitializeComponent)
            ConfigureUIForPtzMode();

            listBox1.SelectedIndex = 0;
            listBox2.SelectedIndex = 0;
            listBox3.SelectedIndex = 0;
            listBox4.SelectedIndex = 0;
            listBoxCamPosControl.SelectedIndex = DEFAULT_CAMERA_POSITION;
            trackBar1.Value = _audioMix.audioProfile[listBox4.SelectedIndex, 0];
            trackBar2.Value = _audioMix.audioProfile[listBox4.SelectedIndex, 1];
            trackBar3.Value = _audioMix.audioProfile[listBox4.SelectedIndex, 2];
            trackBar4.Value = _audioMix.audioProfile[listBox4.SelectedIndex, 3];
            Version version = Assembly.GetExecutingAssembly().GetName().Version;
            this.Text = string.Format("JoKi Automation    Version {0}.{1}.{2}.{3}",
                           version.Major, version.Minor, version.Build, version.Revision);

        }

        private void InitializeNetworkConfig()
        {
            _networkDevices = new Dictionary<string, NetworkDevice>();
            _userPasswords = new Dictionary<string, string>();
            
            // ✅ Suche Network.cfg an mehreren möglichen Orten
            string configPath = FindNetworkConfigFile();
            
            if (configPath == null)
            {
                _logDat?.sendInfoMessage($"JokiAutomation\nNetzwerk-Konfigurationsdatei nicht gefunden!");
                _logDat?.sendInfoMessage($"JokiAutomation\nGesucht in:");
                _logDat?.sendInfoMessage($"JokiAutomation\n  - {Path.Combine(Application.StartupPath, NETWORK_CONFIG_FILE)}");
                _logDat?.sendInfoMessage($"JokiAutomation\n  - {Path.Combine(AppDomain.CurrentDomain.BaseDirectory, NETWORK_CONFIG_FILE)}");
                _logDat?.sendInfoMessage($"JokiAutomation\n  - {Path.Combine(Directory.GetCurrentDirectory(), NETWORK_CONFIG_FILE)}");
                _logDat?.sendInfoMessage($"JokiAutomation\nERROR: Keine Geräte-Konfiguration vorhanden!");
                return;
            }

            _logDat?.sendInfoMessage($"JokiAutomation\nNetwork.cfg gefunden: {configPath}");

            try
            {
                string[] lines = File.ReadAllLines(configPath);

                foreach (string line in lines)
                {
                    // Skip empty lines and comments
                    if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#"))
                        continue;

                    // Expected formats:
                    // Standard Device: DeviceName;IPAddress or DeviceName;IPAddress;Port
                    // Delock Device:   DeviceName;IPAddress;Port;Username;Password
                    // Canon PTZ:       DeviceName;IPAddress;Port;Username;Password;Protocol;PanSpeed;TiltSpeed;ZoomSpeed
                    // User Credential: USER_[Role];Password
                    string[] parts = line.Split(';');

                    if (parts.Length < 2)
                    {
                        _logDat?.sendInfoMessage($"JokiAutomation\nUngültige Zeile in {NETWORK_CONFIG_FILE}: {line}");
                        continue;
                    }

                    string deviceName = parts[0].Trim();

                    // ✅ Skip Canon PTZ camera entries (processed separately by NetworkCfgReader.LoadCamera)
                    if (deviceName.Equals(CANON_CAMERA_CONFIG_KEY, StringComparison.OrdinalIgnoreCase))
                    {
                        _logDat?.sendInfoMessage($"JokiAutomation\nCanon PTZ Konfiguration erkannt (wird separat geladen): {deviceName}");
                        continue;
                    }

                    // ✅ Validate length AFTER Canon check
                    if (parts.Length > 5)
                    {
                        _logDat?.sendInfoMessage($"JokiAutomation\nUngültige Zeile in {NETWORK_CONFIG_FILE}: {line}");
                        continue;
                    }

                    // PTZ Camera Mode Configuration
                    if (deviceName.Equals(PTZ_MODE_CONFIG_KEY, StringComparison.OrdinalIgnoreCase))
                    {
                        bool enabled;
                        if (bool.TryParse(parts[1].Trim(), out enabled))
                        {
                            _isPtzCameraMode = enabled;
                            _logDat?.sendInfoMessage($"JokiAutomation\nPTZ_CAM = {_isPtzCameraMode}");
                        }
                        continue;
                    }

                    // PTZ Preset Recall Path Configuration
                    if (deviceName.Equals(PTZ_PRESET_RECALL_PATH_CONFIG_KEY, StringComparison.OrdinalIgnoreCase))
                    {
                        _ptzPresetRecallPath = parts[1].Trim();
                        _logDat?.sendInfoMessage($"JokiAutomation\nPTZ_PRESET_RECALL_PATH = {_ptzPresetRecallPath}");
                        continue;
                    }

                    // PTZ Preset Recall Fallback Paths Configuration
                    if (deviceName.Equals(PTZ_PRESET_RECALL_PATHS_CONFIG_KEY, StringComparison.OrdinalIgnoreCase))
                    {
                        _ptzPresetRecallFallbackPaths.Clear();
                        string[] templates = parts[1].Split('|');
                        foreach (string template in templates)
                        {
                            string trimmed = template.Trim();
                            if (!string.IsNullOrWhiteSpace(trimmed))
                            {
                                _ptzPresetRecallFallbackPaths.Add(trimmed);
                            }
                        }
                        _logDat?.sendInfoMessage($"JokiAutomation\nPTZ_PRESET_RECALL_PATHS geladen: {_ptzPresetRecallFallbackPaths.Count}");
                        continue;
                    }

                    // ✅ Check if this is a user credential entry
                    if (deviceName.StartsWith("USER_"))
                    {
                        if (parts.Length == 2)
                        {
                            string role = deviceName.Substring(5); // Remove "USER_" prefix
                            string userPassword = parts[1].Trim(); // <-- Renamed to avoid CS0136
                            _userPasswords[role] = userPassword;
                            _logDat?.sendInfoMessage($"JokiAutomation\nUser-Credential geladen: {role}");
                        }
                        else
                        {
                            _logDat?.sendInfoMessage($"JokiAutomation\nUngültiges User-Credential Format: {line}");
                        }
                        continue;
                    }

                    // ✅ Regular device entry
                    string ipAddress = parts[1].Trim();
                    int port = 80; // Default port
                    string username = null;
                    string password = null;

                    // Parse optional port
                    if (parts.Length >= 3 && !string.IsNullOrWhiteSpace(parts[2]))
                    {
                        if (!int.TryParse(parts[2].Trim(), out port))
                        {
                            _logDat?.sendInfoMessage($"JokiAutomation\nUngültiger Port für {deviceName}: {parts[2]}");
                            continue;
                        }
                    }

                    // Parse optional Delock credentials
                    if (parts.Length >= 5)
                    {
                        username = parts[3].Trim();
                        password = parts[4].Trim();
                    }

                    _networkDevices[deviceName] = new NetworkDevice(ipAddress, port, username, password);

                    string deviceInfo = username != null
                        ? $"{deviceName} = {ipAddress}:{port} (Delock: user={username})"
                        : $"{deviceName} = {ipAddress}:{port}";

                    _logDat?.sendInfoMessage($"JokiAutomation\nNetzwerk-Gerät konfiguriert: {deviceInfo}");
                }

                _logDat?.sendInfoMessage($"JokiAutomation\n{_networkDevices.Count} Netzwerk-Gerät(e) und {_userPasswords.Count} User-Credential(s) erfolgreich geladen");
            }
            catch (Exception ex)
            {
                _logDat?.sendInfoMessage($"JokiAutomation\nFehler beim Laden der Netzwerk-Konfiguration: {ex.Message}");
            }
        }

        private void InitializeRokuTV()
        {
            if (!_networkDevices.ContainsKey("Roku_TV"))
            {
                _logDat?.sendInfoMessage("JokiAutomation\nRoku TV nicht in Network.cfg konfiguriert!");
                return;
            }

            string rokuIP = _networkDevices["Roku_TV"].IPAddress;
            _rokuTV = new ROKU_TV_Remote(rokuIP);
            _logDat?.sendInfoMessage($"JokiAutomation\nRoku TV konfiguriert für IP: {rokuIP}");
        }

        private bool TryBeginOperation(string operationName)
        {
            if (!Monitor.TryEnter(_operationLock, TimeSpan.Zero))
            {
                Debug.WriteLine($"⚠ Operation '{operationName}' is locked because other process is running");
                return false;
            }

           // _isOperationInProgress = true;
            Debug.WriteLine($"→ Operation '{operationName}' started");
            return true;
        }

        private void EndOperation(string operationName)
        {
           // _isOperationInProgress = false;
            Debug.WriteLine($"← Operation '{operationName}' finished");
            Monitor.Exit(_operationLock);
        }

        /// <summary>
        /// Interprets command line arguments and executes corresponding automation commands.
        /// </summary>
        /// <param name="commands">Command line arguments array</param>
        public async Task CommandInterpreterAsync(string[] commands)
        {
            // Validate input BEFORE trying to acquire lock
            if (commands == null || commands.Length < 2)
            {
                _logDat?.sendInfoMessage("JokiAutomation\nKeine gültigen Kommandozeilenargumente übergeben.");
                return;
            }

            string cmd = commands[1];
            _logDat?.sendInfoMessage($"JokiAutomation\n>>> Kommando '{cmd}' wird ausgeführt...");

            if (!TryBeginOperation("CommandInterpreter"))
            {
                _logDat?.sendInfoMessage($"JokiAutomation\n⚠ Kommando '{cmd}' wird übersprungen - eine andere Operation läuft bereits");
                return;
            }

            try
            {
                // Commands requiring 4 parameters
                if ((cmd == "Pause" || cmd == "BEAMER_VideoClip") && commands.Length < 4)
                {
                    _logDat?.sendInfoMessage($"JokiAutomation\nKommando '{cmd}' benötigt 2 zusätzliche Parameter.");
                    return;
                }

                if (cmd == "PositionControl" && commands.Length < 4)
                {
                    _logDat?.sendInfoMessage("JokiAutomation\nKommando 'PositionControl' benötigt 2 Parameter: Position und Profil.");
                    return;
                }

                // Execute command
                switch (cmd)
                {
                    case "Pause":
                        DisablePictureInPicture();
                        SwitchATEMInput(ATEMInput.Laptop);
                        ExecutePauseCommand(commands[2], commands[3]);
                        break;

                    case "Timer":
                        if (commands.Length < 3)
                        {
                            _logDat?.sendInfoMessage("JokiAutomation\nKommando 'Timer' benötigt einen Zeit-Parameter.");
                            return;
                        }
                        DisablePictureInPicture();
                        SwitchATEMInput(ATEMInput.Laptop);
                        ExecuteTimerCommand(commands[2]);
                        break;

                    case "Band":
                        _audioMix?._rasPi?.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_PPP_VIEW);
                        DisablePictureInPicture();
                        SwitchATEMInput(ATEMInput.Laptop);
                        break;

                    case "Text":
                        _audioMix?._rasPi?.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_TEXT_VIEW);
                        DisablePictureInPicture();
                        SwitchATEMInput(ATEMInput.Laptop);
                        break;

                    case "GoPro":
                        _audioMix?._rasPi?.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_GOPRO_VIEW);
                        DisablePictureInPicture();
                        SwitchATEMInput(ATEMInput.GoPro);
                        break;

                    case "Altar":
                        _logDat?.sendInfoMessage("JokiAutomation\nFühre 'Altar' Kommando aus...");
                        // Kamera-Positionierung abhängig vom Modus:
                        // PTZ_CAM=true: Canon PTZ bewegt Kamera | PTZ_CAM=false: RasPi Motor bewegt Kamera
                        // IR-Sequenz wird IMMER über RasPi ausgeführt (Beamer, andere Geräte)
                        if (_isPtzCameraMode && _canonPtzController != null)
                        {
                            _logDat?.sendInfoMessage("JokiAutomation\nPTZ-Modus: Rufe ExecuteCanonSceneAsync auf...");
                            await ExecuteCanonSceneAsync("Altar", 2, ATEMInput.CanonPtzMain);
                            _logDat?.sendInfoMessage("JokiAutomation\nExecuteCanonSceneAsync abgeschlossen");
                        }
                        else
                        {
                            _logDat?.sendInfoMessage("JokiAutomation\nLegacy-Modus: RasPi IR-Sequenz");
                            // RasPi IR-Sequenz (Beamer-Umschaltung + ggf. Motor-Steuerung im Non-PTZ Modus)
                            _audioMix?._rasPi?.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_POSCAM_VIEW);
                            DisablePictureInPicture();
                            SwitchATEMInput(ATEMInput.CanonPtzMain);
                        }
                        break;

                    case "Predigt":
                        _logDat?.sendInfoMessage("JokiAutomation\nFühre 'Predigt' Kommando aus...");
                        if (_isPtzCameraMode && _canonPtzController != null)
                        {
                            _logDat?.sendInfoMessage("JokiAutomation\nPTZ-Modus: Rufe ExecuteCanonSceneAsync auf...");
                            await ExecuteCanonSceneAsync("Predigt", 3, ATEMInput.CanonPtzPreacher);
                            _logDat?.sendInfoMessage("JokiAutomation\nExecuteCanonSceneAsync abgeschlossen");
                        }
                        else
                        {
                            _logDat?.sendInfoMessage("JokiAutomation\nLegacy-Modus: RasPi IR-Sequenz");
                            _audioMix?._rasPi?.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_PREACHER_VIEW);
                            DisablePictureInPicture();
                            SwitchATEMInput(ATEMInput.CanonPtzPreacher);
                        }
                        break;

                    case "Gebet":
                        _audioMix?._rasPi?.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_PRAYER_VIEW);
                        SwitchATEMInput(ATEMInput.Laptop);
                        Thread.Sleep(1000); // Kurze Verzögerung, um sicherzustellen, dass der ATEM die Eingangsquelle gewechselt hat
                        EnablePictureInPicture();
                        break;

                    case "LesungMulti":
                        _audioMix?._rasPi?.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_READER_VIEW);
                        SwitchATEMInput(ATEMInput.Laptop);
                        Thread.Sleep(1000); // Kurze Verzögerung, um sicherzustellen, dass der ATEM die Eingangsquelle gewechselt hat
                        EnablePictureInPicture();
                        break;

                    case "BandMulti":
                        _audioMix?._rasPi?.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_SONG_VIEW);
                        SwitchATEMInput(ATEMInput.Laptop);
                        Thread.Sleep(1000); // Kurze Verzögerung, um sicherzustellen, dass der ATEM die Eingangsquelle gewechselt hat
                        EnablePictureInPicture();
                        break;

                    case "BEAMER_LiveVideo":
                        _audioMix?._rasPi?.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_LIVE_VIDEO);
                        DisablePictureInPicture();
                        SwitchATEMInput(ATEMInput.Laptop);
                        break;

                    case "BEAMER_LiveStream":
                        _audioMix?._rasPi?.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_BEAMER_TOGGLE);
                        break;

                    case "BEAMER_VideoClip":
                        _audioMix?._rasPi?.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_BEAMER_ANALOG);
                        SwitchATEMInput(ATEMInput.Laptop);
                        ExecutePauseCommand(commands[2], commands[3]);
                        break;

                    case "BEAMER_Mute":
                        _audioMix?._rasPi?.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_BEAMER_MUTE);
                        break;

                    case "BEAMER_ON":
                        _audioMix?._rasPi?.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_BEAMER_ON);
                        break;

                    case "Backup_Start":
                        _audioMix?._rasPi?.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_START_BACKUP);
                        break;

                    case "Backup_Stop":
                        _audioMix?._rasPi?.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_STOP_BACKUP);
                        break;

                    case "Backup_Switch":
                        _audioMix?._rasPi?.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_SWITCH_BACKUP);
                        break;

                    case "Ausschaltsequenz":
                        _logDat?.sendInfoMessage("JokiAutomation\n");
                        _logDat?.sendInfoMessage("JokiAutomation\n╔═══════════════════════════════════════════════════╗");
                        _logDat?.sendInfoMessage("JokiAutomation\n║     AUSSCHALTSEQUENZ GESTARTET                   ║");
                        _logDat?.sendInfoMessage("JokiAutomation\n╚═══════════════════════════════════════════════════╝");
                        _logDat?.sendInfoMessage("JokiAutomation\n");
                        
                        // Step 1: PTZ Camera shutdown (if in PTZ mode)
                        if (_isPtzCameraMode)
                        {
                            _logDat?.sendInfoMessage("JokiAutomation\n[1/2] Canon PTZ Kamera herunterfahren...");
                            await ShutdownPtzCameraAsync();
                        }
                        else
                        {
                            _logDat?.sendInfoMessage("JokiAutomation\n[1/2] Kein PTZ-Modus - Kamera-Shutdown übersprungen");
                        }
                        
                        _logDat?.sendInfoMessage("JokiAutomation\n");
                        
                        // Step 2: RasPi IR shutdown sequence (Beamer, etc.)
                        _logDat?.sendInfoMessage("JokiAutomation\n[2/2] RasPi IR-Shutdown-Sequenz (Beamer, etc.)...");
                        _audioMix?._rasPi?.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_SHUTDOWN);
                        
                        _logDat?.sendInfoMessage("JokiAutomation\n");
                        _logDat?.sendInfoMessage("JokiAutomation\n╔═══════════════════════════════════════════════════╗");
                        _logDat?.sendInfoMessage("JokiAutomation\n║     AUSSCHALTSEQUENZ ABGESCHLOSSEN               ║");
                        _logDat?.sendInfoMessage("JokiAutomation\n╚═══════════════════════════════════════════════════╝");
                        break;

                    case "RasPi_Reset":
                        _audioMix?._rasPi?.rasPiStop();
                        break;

                    case "PositionControl":
                        // Auswerten des ersten Buchstabens von commands[3] für ATEM-Steuerung
                        if (commands.Length >= 4 && !string.IsNullOrEmpty(commands[3]))
                        {
                            char firstChar = char.ToUpper(commands[3][0]);
                            DisablePictureInPicture();

                            _logDat?.sendInfoMessage($"JokiAutomation\nPositionControl: Position={commands[2]}, Profil={commands[3]}, Modus={firstChar}");

                            switch (firstChar)
                            {
                                case 'A':  // Altar = Canon PTZ Main
                                    SwitchATEMInput(ATEMInput.CanonPtzMain);
                                    break;

                                case 'G':  // GoPro = GoPro Actionkamera
                                    SwitchATEMInput(ATEMInput.GoPro);
                                    break;

                                case 'K':  // Kanzel = Canon PTZ Preacher
                                    SwitchATEMInput(ATEMInput.CanonPtzPreacher);
                                    break;

                                case 'L':  // Laptop = Laptop/Computer
                                    SwitchATEMInput(ATEMInput.Laptop);
                                    break;

                                default:
                                    _logDat?.sendInfoMessage($"JokiAutomation\nUnbekannter Profil-Typ: {firstChar}, PiP wird deaktiviert");
                                    DisablePictureInPicture();
                                    break;
                            }
                        }
                        else
                        {
                            _logDat?.sendInfoMessage("JokiAutomation\nKeine Profil-Information vorhanden");
                            DisablePictureInPicture();
                        }

                        // Warte bis Position Control fertig ist (max. 80 Sekunden)
                        int maxWaitMs = 80000;
                        int waitIntervalMs = 100;
                        int elapsedMs = 0;

                        _positionControl?.sequence(commands[2], commands[3]);
                        while (_positionControl != null && _positionControl.IsMoving() && elapsedMs < maxWaitMs)
                        {
                            Thread.Sleep(waitIntervalMs);
                            elapsedMs += waitIntervalMs;
                            Application.DoEvents(); // UI responsive halten
                        }

                        if (elapsedMs >= maxWaitMs)
                        {
                            _logDat?.sendInfoMessage("JokiAutomation\nWARNUNG: Position Control Timeout nach 15s");
                        }
                        else
                        {
                            _logDat?.sendInfoMessage($"JokiAutomation\nPosition Control abgeschlossen nach {elapsedMs}ms");
                        }

                        SwitchATEMInput(ATEMInput.CanonPtzMain);
                        break;

                    case "AutoZoom":
                        if (_isPtzCameraMode)
                        {
                            _logDat?.sendInfoMessage("JokiAutomation\nAutoZoom nicht verfügbar im PTZ-Modus (Canon Zoom über Kamera steuern)");
                        }
                        else
                        {
                            _autoZoom?.openDialog(this);
                        }
                        break;

                    case "ZoomReferenz":
                        if (_isPtzCameraMode)
                        {
                            _logDat?.sendInfoMessage("JokiAutomation\nZoomReferenz nicht verfügbar im PTZ-Modus");
                        }
                        else
                        {
                            moveZoomReference();
                        }
                        break;

                    case "ATEM_Init":
                        InitializeATEMToDefault();
                        break;

                    case "ATEM_MIC1_On":
                        SetATEMMicrophone(1, true);
                        break;

                    case "ATEM_MIC1_Off":
                        SetATEMMicrophone(1, false);
                        break;

                    case "ATEM_MIC2_On":
                        SetATEMMicrophone(2, true);
                        break;

                    case "ATEM_MIC2_Off":
                        SetATEMMicrophone(2, false);
                        break;

                    case "ROKU_TVon":
                        {
                            DelockSocketAdapter hdmiDelockTransmitSupply = GetDelockAdapter("HDMI_Extender_Transmitter");
                            DelockSocketAdapter hdmiDelockTVReceiverSupply = GetDelockAdapter("HDMI_Extender_TV_Receiver");
                            if (hdmiDelockTransmitSupply != null)
                            {
                                hdmiDelockTransmitSupply.TurnOnSocket(1);
                                _logDat?.sendInfoMessage("JokiAutomation\nRoku HDMI Extender TV Receiver Steckdose eingeschaltet.");
                            }

                            Thread.Sleep(1000);

                            if (hdmiDelockTVReceiverSupply != null)
                            {
                                hdmiDelockTVReceiverSupply.TurnOnSocket(1);
                                _logDat?.sendInfoMessage("JokiAutomation\nRoku HDMI Extender TV Receiver Steckdose eingeschaltet.");
                            }

                            Thread.Sleep(1000);

                            if (_rokuTV != null)
                            {
                                _rokuTV.PowerOn();
                                Thread.Sleep(1000);
                                _rokuTV.SwitchToHDMI2();
                                _logDat?.sendInfoMessage("JokiAutomation\nRoku TV eingeschaltet.");
                            }
                        }
                        break;

                    case "ROKU_TVoff":
                        {
                            DelockSocketAdapter hdmiDelockTransmitSupply = GetDelockAdapter("HDMI_Extender_Transmitter");
                            DelockSocketAdapter hdmiDelockTVReceiverSupply = GetDelockAdapter("HDMI_Extender_TV_Receiver");
                            if (_rokuTV != null)
                            {
                                _rokuTV.PowerOff();
                                _logDat?.sendInfoMessage("JokiAutomation\nRoku TV ausgeschaltet.");
                            }

                            Thread.Sleep(1000);

                            if (hdmiDelockTVReceiverSupply != null)
                            {
                                hdmiDelockTVReceiverSupply.TurnOffSocket(1);
                                _logDat?.sendInfoMessage("JokiAutomation\nRoku HDMI Extender TV Receiver Steckdose ausgeschaltet.");
                            }

                            Thread.Sleep(1000);

                            if (hdmiDelockTransmitSupply != null)
                            {
                                hdmiDelockTransmitSupply.TurnOffSocket(1);
                                _logDat?.sendInfoMessage("JokiAutomation\nRoku HDMI Extender TV Receiver Steckdose ausgeschaltet.");
                            }
                        }
                        break;

                    default:
                        _logDat?.sendInfoMessage($"JokiAutomation\nUnbekanntes Kommando: '{cmd}'");
                        break;
                }

                _logDat?.sendInfoMessage($"JokiAutomation\n✓ Kommando '{cmd}' erfolgreich abgeschlossen");
            }
            catch (Exception ex)
            {
                _logDat?.sendInfoMessage($"JokiAutomation\nFehler beim Ausführen des Kommandos '{cmd}':\n{ex.Message}");
                _logDat?.sendInfoMessage($"JokiAutomation\nStackTrace:\n{ex.StackTrace}");
            }
            finally
            {
                EndOperation("CommandInterpreter");
                _logDat?.sendInfoMessage($"JokiAutomation\n<<< Kommando '{cmd}' beendet\n");
            }
        }

        /// <summary>
        /// Executes the Pause command with two text parameters.
        /// </summary>
        private void ExecutePauseCommand(string text1, string text2)
        {
            if (string.IsNullOrWhiteSpace(text1) || string.IsNullOrWhiteSpace(text2))
            {
                _logDat?.sendInfoMessage("JokiAutomation\nPause-Texte dürfen nicht leer sein.");
                return;
            }

            _audioMix?._rasPi?.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_PAUSE);
            _eventTimer?.sendPause(text1, text2);
        }

        /// <summary>
        /// Executes the Timer command with event time parameter.
        /// </summary>
        private void ExecuteTimerCommand(string eventTime)
        {
            if (string.IsNullOrWhiteSpace(eventTime))
            {
                _logDat?.sendInfoMessage("JokiAutomation\nEvent-Zeit darf nicht leer sein.");
                return;
            }

            _audioMix?._rasPi?.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_TIMER);
            _eventTimer?.sendEventTime(eventTime);
        }

        public void displayZoomConfig()
        {
            //rv
            textBoxZoomCalibTime.Text = Convert.ToString(_autoZoom._AZ_Config.CalibrationTime, 10);
            textBoxServoControlInv.Text = Convert.ToString(_autoZoom._AZ_Config.ServoControlN, 10);
            textBoxServoMiddle.Text = Convert.ToString(_autoZoom._AZ_Config.ServoMiddle, 10);
            textBoxServoControl.Text = Convert.ToString(_autoZoom._AZ_Config.ServoControl, 10);
            textBoxServoReference.Text = Convert.ToString(_autoZoom._AZ_Config.ServoReference, 10);
        }

        // set autozoom value
        public void setZoomValue(byte value)
        {
            _autoZoom.setZoomValue(value);
        }

        // move the autozoom
        public void moveZoom()
        {
            _autoZoom.moveToPos();
        }

        // eventhandler Start button, start timer or pause slide show depending on selected listbox item
        private void button1_Click(object sender, EventArgs e)
        {
            int commandID = AudioMix.AM_ACTIVE + AUDIO_CHANNELS_1_AND_2;
            _audioMix?.executeAudio(commandID); // activate audio channels 1 and 2 

            if (this.listBox1.Text == "Pause")
            {
                _eventTimer?.sendPause($"\"{textBox1.Text}\"", $"\"{textBox2.Text}\"");
                _audioMix?._rasPi?.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_PAUSE);
            }
            else
            {
                string eventTime = $"{dateTimePicker1.Value.TimeOfDay.Hours:00}:{dateTimePicker1.Value.TimeOfDay.Minutes:00}";
                _eventTimer?.sendEventTime(eventTime);
                _audioMix?._rasPi?.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_TIMER);
            }
        }


        // eventhandler Start button InfraredControl
        private void button2_Click(object sender, EventArgs e)
        {
            _infraredControl.ExecuteIR(listBox2.SelectedIndex);
        }

        // eventhandler Teach button InfraredControl
        private void button3_Click(object sender, EventArgs e)
        {
            if (loginUser("Admin")) // ✅ Kein == true
            {
                _infraredControl?.TeachIR(listBox2.SelectedIndex);
            }
            else
            {
                _requestedFunction = FUNCTION_TEACH_INFRARED;
            }
        }

        // buttonhandler fade down <<< AudioControl
        private void button7_Click(object sender, EventArgs e)
        {
            int selectedIndex = listBox3.SelectedIndex;

            if (selectedIndex < 0 || selectedIndex >= _audioMix.channelActive_.Length)
                return;

            if (_audioMix.channelActive_[selectedIndex]) // ✅ Kein == true
            {
                int commandID = AudioMix.AM_FADEDOWN + (1 << selectedIndex);
                _audioMix?.executeAudio(commandID);
            }
        }
        // buttonhandler fade up >>>  AudioControl
        private void button8_Click(object sender, EventArgs e)
        {
            int selectedIndex = listBox3.SelectedIndex;

            if (selectedIndex < 0 || selectedIndex >= _audioMix.channelActive_.Length)
                return;

            if (_audioMix.channelActive_[selectedIndex]) // ✅ Kein == true
            {
                int commandID = AudioMix.AM_FADEUP + (1 << selectedIndex);
                _audioMix?.executeAudio(commandID);
            }
        }
        // buttonhandler activate Audiochannel Audiocontrol  
        private void button4_Click(object sender, EventArgs e)
        {
            int selectedIndex = listBox3.SelectedIndex;

            if (selectedIndex < 0 || selectedIndex >= _audioMix.channelActive_.Length)
                return;

            _audioMix.channelActive_[selectedIndex] = !_audioMix.channelActive_[selectedIndex];
            button4.BackColor = _audioMix.channelActive_[selectedIndex] ? Color.Green : Color.Red;
        }

        // active channel listbox index changed
        private void listBox3_SelectedIndexChanged(object sender, EventArgs e)
        {
            int selectedIndex = listBox3.SelectedIndex;

            if (selectedIndex < 0 || selectedIndex >= _audioMix.channelActive_.Length)
                return;

            button4.BackColor = _audioMix.channelActive_[selectedIndex] ? Color.Green : Color.Red;
        }

        // execute Audio - profile
        private void button9_Click(object sender, EventArgs e)
        {
            int commandID = AudioMix.AM_PROFILE + listBox4.SelectedIndex;
            _audioMix.executeAudio(commandID);
        }

        // tab control index changed initialize Audiomix for channel 1 and 2 if page opens
        private async void TabControl1_SelectedIndexChanged(Object sender, EventArgs e)
        {
            if (TabControl1.SelectedIndex == TAB_INDEX_AUTOZOOM_CONFIG)
            {
                try
                {
                    _autoZoom?.readZoomConfiguration();
                    _autoZoom?.readZoomValues();
                    displayZoomConfig();
                }
                catch (Exception ex)
                {
                    _logDat?.sendInfoMessage($"JokiAutomation\nFehler beim Laden der Zoom-Konfiguration:\n{ex.Message}");
                }
            }
            else if (TabControl1.SelectedIndex == TAB_INDEX_POSITION_CONTROL)
            {
                try
                {
                    _autoZoom?.readZoomValues();
                    byte zoomVal = GetZoomValueSafely(listBoxCamPosControl.SelectedIndex);
                    _autoZoom?.setZoomValue(zoomVal);
                    zoomValue.Text = zoomVal.ToString();
                    
                    // ✅ NEU: Update Auto-Tracking Button Status
                    await UpdateAutoTrackingButtonStateAsync();
                }
                catch (Exception ex)
                {
                    _logDat?.sendInfoMessage($"JokiAutomation\nFehler beim Laden der Zoom-Werte:\n{ex.Message}");
                }
            }
        }

        // reset raspberry pi 1 set RaspiAutomation defaults on raspberry from audiomix menu page
        private void button11_Click(object sender, EventArgs e)
        {
            _audioMix._rasPi.rasPiDefaultSwitch();
        }
        // reset raspberry pi 2 set RaspiAutomation defaults on raspberry from infrared menu page
        private void button10_Click(object sender, EventArgs e)
        {
            _audioMix._rasPi.rasPiDefaultSwitch();
        }
        // button handler start/stop sequencer test
        private void button12_Click(object sender, EventArgs e)
        {
            CI_test_active_ = !CI_test_active_; // start stop test IR sequencer
            if (CI_test_active_)
            {
                button12.BackColor = Color.Green;
            }
            else
            {
                button12.BackColor = Color.Transparent;
            }
            _infraredControl.IRTest(CI_test_active_);
        }
        // button handler reset
        private void button13_Click(object sender, EventArgs e)
        {
            _audioMix._rasPi.rasPiStop();
        }

        private void button14_Click(object sender, EventArgs e)
        {
            _audioMix._rasPi.rasPiStop();
        }

        private void button15_Click(object sender, EventArgs e)
        {
            _audioMix._rasPi.rasPiStop();
        }
        //slider Laptop audio
        private void trackBar1_Scroll(object sender, EventArgs e)
        {
            _audioMix.audioProfile[listBox4.SelectedIndex, 0] = (byte)trackBar1.Value;
        }
        //slider sumary signal amplifier audio
        private void trackBar2_Scroll(object sender, EventArgs e)
        {
            _audioMix.audioProfile[listBox4.SelectedIndex, 1] = (byte)trackBar2.Value;
        }

        //slider room microphone audio
        private void trackBar3_Scroll(object sender, EventArgs e)
        {
            _audioMix.audioProfile[listBox4.SelectedIndex, 2] = (byte)trackBar3.Value;
        }

        //slider channel 4 adio
        private void trackBar4_Scroll(object sender, EventArgs e)
        {
            _audioMix.audioProfile[listBox4.SelectedIndex, 3] = (byte)trackBar4.Value;
        }

        // change audio profile depending listbox index
        private void listBox4_SelectedIndexChanged(object sender, EventArgs e)
        {
            trackBar1.Value = _audioMix.audioProfile[listBox4.SelectedIndex, 0];
            trackBar2.Value = _audioMix.audioProfile[listBox4.SelectedIndex, 1];
            trackBar3.Value = _audioMix.audioProfile[listBox4.SelectedIndex, 2];
            trackBar4.Value = _audioMix.audioProfile[listBox4.SelectedIndex, 3];
        }

        // eventhandler audio teach
        private void button16_Click(object sender, EventArgs e)
        {
            if (loginUser("SuperUser")) // ✅ Kein == true
            {
                _audioMix?.teachAudio(listBox4.SelectedIndex);
            }
            else
            {
                _requestedFunction = FUNCTION_TEACH_AUDIO;
            }
        }

        // eventhandler position control calibrate
        private void calibrate_Click(object sender, EventArgs e)
        {
            _requestedFunction = FUNCTION_CALIBRATE_MAGNETOMETER;
            richTextBox3.Clear();

            if (loginUser("Admin")) // ✅ Kein == true
            {
                _positionControl?.calibratePC(1);
                _requestedFunction = 0;
            }
        }

        // eventhandler reset button position control 
        private void resetCamPos_Click(object sender, EventArgs e)
        {
            _requestedFunction = 0;
            _audioMix._rasPi.rasPiStop();
        }

        private bool loginUser(string userString)
        {
            bool retVal = false;
            _requestedUser = userString;
            if ((_User == _requestedUser) || (_User == "Admin"))
            {
                retVal = true;
            }
            else
            {
                _logDat.sendInfoMessage("Für diese Funktion ist ein " + _requestedUser + " login erforderlich, bitte Passwort in Textbox eingeben\nPasswort: ");
                _InputTimerloop = 0;
                _Inputtimer.Start();
            }

            return retVal;
        }

        // event handler Input timer elapsed for user handling
        private void Inputtimer_Elapsed(object sender, System.EventArgs e)
        {
            string userString = null;
            if (++_InputTimerloop < PASSWORD_TIMEOUT_SECONDS) // 30 seconds time for log in
            {
                _Inputtimer.Start();
                switch (TabControl1.SelectedIndex)
                {
                    case TAB_INDEX_INFRARED:
                        userString = richTextBox1.Text;
                        richTextBox1.Select(richTextBox1.Text.Length - 1, 0);
                        richTextBox1.ScrollToCaret();
                        break;
                    case TAB_INDEX_AUDIO:
                        userString = richTextBox2.Text;
                        richTextBox2.Select(richTextBox2.Text.Length - 1, 0);
                        richTextBox2.ScrollToCaret();
                        break;
                    case TAB_INDEX_POSITION_CONTROL:
                        userString = richTextBox3.Text;
                        richTextBox3.Select(richTextBox3.Text.Length - 1, 0);
                        richTextBox3.ScrollToCaret();
                        break;
                    case TAB_INDEX_AUTOZOOM_CONFIG:
                        userString = richTextBoxZoomConfig.Text;
                        richTextBoxZoomConfig.Select(richTextBoxZoomConfig.Text.Length - 1, 0);
                        richTextBoxZoomConfig.ScrollToCaret();
                        break;
                }

                // ✅ NEU: Passwörter aus Config verwenden statt hardcoded
                if (userString != null && _userPasswords != null)
                {
                    if (_userPasswords.ContainsKey("Admin") && userString.Contains(_userPasswords["Admin"]))
                    {
                        _User = "Admin";
                    }
                    else if (_userPasswords.ContainsKey("SuperUser") && userString.Contains(_userPasswords["SuperUser"]))
                    {
                        _User = "SuperUser";
                    }
                    else
                    {
                        _User = "DefaultUser";
                    }
                }
                else
                {
                    _User = "DefaultUser";
                }
            }
            else
            {
                _Inputtimer.Stop();
                _logDat?.sendInfoMessage($"Passwort falsch!!! Funktion nicht möglich. Für diese Funktion ist ein {_requestedUser} login erforderlich");
                _requestedFunction = 0;
            }

            if ((_requestedUser == _User) || (_User == "Admin"))
            {
                _Inputtimer.Stop();
                switch (TabControl1.SelectedIndex)
                {
                    case TAB_INDEX_INFRARED:
                        richTextBox1.Clear();
                        break;
                    case TAB_INDEX_AUDIO:
                        richTextBox2.Clear();
                        break;
                    case TAB_INDEX_POSITION_CONTROL:
                        richTextBox3.Clear();
                        break;
                }

                ExecuteRequestedFunction();
            }
        }

        /// <summary>
        /// Executes the function requested after successful authentication.
        /// </summary>
        private void ExecuteRequestedFunction()
        {
            switch (_requestedFunction)
            {
                case FUNCTION_CALIBRATE_MAGNETOMETER:
                    _positionControl?.calibratePC(1);
                    _requestedFunction = 0;
                    break;
                case FUNCTION_TEACH_AUDIO:
                    _audioMix?.teachAudio(listBox4.SelectedIndex);
                    _requestedFunction = 0;
                    break;
                case FUNCTION_TEACH_INFRARED:
                    _infraredControl?.TeachIR(listBox2.SelectedIndex);
                    _requestedFunction = 0;
                    break;
                case FUNCTION_TEACH_POSITION:
                    _positionControl?.teachPos(listBoxCamPosControl.SelectedIndex);
                    break;
                case FUNCTION_CALIBRATE_GYROSCOPE:
                    _positionControl?.calibratePC(2);
                    _requestedFunction = 0;
                    break;
                case FUNCTION_TEACH_NULL_POSITION:
                    _positionControl?.teachPos(NULL_POSITION_INDEX);
                    _requestedFunction = 0;
                    break;
            }
        }

        // teach selected position of camcorder
        private void teachCamPos_Click(object sender, EventArgs e)
        {
            richTextBox3.Clear();

            int selectedPosition = listBoxCamPosControl.SelectedIndex;
            
            if (selectedPosition < 0)
            {
                _logDat?.sendInfoMessage("JokiAutomation\nBitte wählen Sie eine Position aus!");
                return;
            }

            // Delegiere an PositionControl - funktioniert sowohl für PTZ als auch RasPi
            _positionControl?.teachPos(selectedPosition);
        }

        // eventhandler teach null position
        private void teachNullPos_Click(object sender, EventArgs e)
        {
            richTextBox3.Clear();
            _requestedFunction = FUNCTION_TEACH_NULL_POSITION;

            if (loginUser("Admin"))
            {
                _positionControl?.teachPos(NULL_POSITION_INDEX);
            }
        }
        // eventhandler move up
        private void moveUpHandler(object sender, EventArgs e)
        {
            if (_isPtzCameraMode && _canonPtzController != null)
            {
                Task.Run(async () => await _canonPtzController.StartTiltUpAsync());
            }
            else
            {
                _positionControl.moveButtonPressed(PositionControl.PC_BUTTON_UP);
            }
        }

        // eventhandler move down
        private void moveDownHandler(object sender, EventArgs e)
        {
            if (_isPtzCameraMode && _canonPtzController != null)
            {
                Task.Run(async () => await _canonPtzController.StartTiltDownAsync());
            }
            else
            {
                _positionControl.moveButtonPressed(PositionControl.PC_BUTTON_DOWN);
            }
        }

        // eventhandler move left
        private void moveLeftHandler(object sender, EventArgs e)
        {
            if (_isPtzCameraMode && _canonPtzController != null)
            {
                Task.Run(async () => await _canonPtzController.StartPanLeftAsync());
            }
            else
            {
                _positionControl.moveButtonPressed(PositionControl.PC_BUTTON_LEFT);
            }
        }

        // eventhandler move right
        private void moveRightHandler(object sender, EventArgs e)
        {
            if (_isPtzCameraMode && _canonPtzController != null)
            {
                Task.Run(async () => await _canonPtzController.StartPanRightAsync());
            }
            else
            {
                _positionControl.moveButtonPressed(PositionControl.PC_BUTTON_RIGHT);
            }
        }

        // eventhandler move button released handler
        private void moveDoneHandler(object sender, EventArgs e)
        {
            if (_isPtzCameraMode && _canonPtzController != null)
            {
                Task.Run(async () => await _canonPtzController.StopAllAsync());
            }
            else
            {
                _positionControl.moveButtonPressed(PositionControl.PC_BUTTON_RELEASED);
            }
        }
        // eventhandler test position control moves to top five positions in list
        private void testPos_Click(object sender, EventArgs e)
        {
            _positionControl.testProgram(0); // test program 1 move to first five positions with cam 1 view
        }

        // eventhandler advanced test program 1 move to top five positions with switching cam 1, cam 2, gopro, laptop and sound profiles
        private void testPosSwitch_Click(object sender, EventArgs e)
        {
            _positionControl.testProgram(1);
        }

        // zoom value changed
        private void zoomValue_TextChanged(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(zoomValue.Text))
                return;

            if (!byte.TryParse(zoomValue.Text, out byte num_var))
            {
                zoomValue.Text = "";
                _logDat?.sendInfoMessage("JokiAutomation\nUngültiges Zahlenformat");
                return;
            }

            if (num_var > 100) // ✅ >= 0 entfernt (byte ist immer >= 0)
            {
                zoomValue.Text = "";
                _logDat?.sendInfoMessage("JokiAutomation\nWert muss zwischen 0 und 100 liegen");
                return;
            }

            setZoomValue(num_var);
        }

        // zoom calibration time changed
        private void textBoxZoomCalibTime_TextChanged(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(textBoxZoomCalibTime.Text))
                return;

            if (!uint.TryParse(textBoxZoomCalibTime.Text, out uint num_var))
            {
                textBoxZoomCalibTime.Text = "";
                _logDat?.sendInfoMessage("JokiAutomation\nFormatfehler Zahlenwert in Autozoom");
                return;
            }

            if (num_var >= 30000000) // ✅ >= 0 entfernt (uint ist immer >= 0)
            {
                textBoxZoomCalibTime.Text = "";
                _logDat?.sendInfoMessage("JokiAutomation\nWert zu groß (max: 30000000)");
                return;
            }

            if (_autoZoom != null)
            {
                _autoZoom._AZ_Config.CalibrationTime = num_var;
            }
        }

        // zoom servo middle position
        private void textBoxServoMiddle_TextChanged(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(textBoxServoMiddle.Text))
                return;

            if (!ushort.TryParse(textBoxServoMiddle.Text, out ushort num_var))
            {
                textBoxServoMiddle.Text = "";
                _logDat?.sendInfoMessage("JokiAutomation\nFormatfehler Zahlenwert in Autozoom");
                return;
            }

            if (num_var >= 1700) // ✅ >= 0 entfernt (ushort ist immer >= 0)
            {
                textBoxServoMiddle.Text = "";
                _logDat?.sendInfoMessage("JokiAutomation\nWert zu groß (max: 1699)");
                return;
            }

            if (_autoZoom != null)
            {
                _autoZoom._AZ_Config.ServoMiddle = num_var;
            }
        }

        // zoom servo control position offset (= middle position - offset)
        private void textBoxServoControlInv_TextChanged(object sender, EventArgs e)
        {
            try
            {
                ushort num_var = ushort.Parse(textBoxServoControlInv.Text);
                if ((num_var >= 0) && (num_var < 200))
                {
                    _autoZoom._AZ_Config.ServoControlN = num_var;
                    _autoZoom._AZLastServoPosition = (byte)AutoZoomControl.AZ_SERVOPOS.AZ_CON_LEFT;
                }
                else
                {
                    textBoxServoControlInv.Text = "";
                }
            }
            catch (Exception)
            {
                textBoxServoControlInv.Text = "";
                _logDat.sendInfoMessage("JokiAutomation\nFormatfehler Zahlenwert in Autozoom \n");
                textBoxServoControlInv.Focus();
            }

        }

        // zoom servo reference position offset (= middle position +/- offset)
        private void textBoxServoReference_TextChanged(object sender, EventArgs e)
        {
            try
            {
                ushort num_var = ushort.Parse(textBoxServoReference.Text);
                if ((num_var >= 0) && (num_var < 400))
                {
                    _autoZoom._AZ_Config.ServoReference = num_var;
                    _autoZoom._AZLastServoPosition = (byte)AutoZoomControl.AZ_SERVOPOS.AZ_REF_RIGHT;
                }
                else
                {
                    textBoxServoReference.Text = "";
                }
            }
            catch (Exception)
            {
                textBoxServoReference.Text = "";
                _logDat.sendInfoMessage("JokiAutomation\nFormatfehler Zahlenwert in Autozoom \n");
                textBoxServoReference.Focus();
            }
        }

        // zoom servo control position offset (= middle position + offset)
        private void textBoxServoControl_TextChanged(object sender, EventArgs e)
        {
            try
            {
                ushort num_var = ushort.Parse(textBoxServoControl.Text);
                if ((num_var >= 0) && (num_var < 200))
                {
                    _autoZoom._AZ_Config.ServoControl = num_var;
                    _autoZoom._AZLastServoPosition = (byte)AutoZoomControl.AZ_SERVOPOS.AZ_CON_RIGHT;

                }
                else
                {
                    textBoxServoControl.Text = "";
                }
            }
            catch (Exception)
            {
                textBoxServoControl.Text = "";
                _logDat.sendInfoMessage("JokiAutomation\nFormatfehler Zahlenwert in Autozoom \n");
                textBoxServoControl.Focus();
            }
        }

        // move zoom to position
        private void buttonZoom_Click(object sender, EventArgs e)
        {
            moveZoom();
        }

        // move zoom to reference point
        private void moveZoomReference()
        {
            setZoomValue(100);
            moveZoom();
        }


        // eventhandler button write zoom configuration
        private void buttonConfig_Click(object sender, EventArgs e)
        {
            if (loginUser("Admin") == true)     // admin login necessary
            {
                _autoZoom.writeZoomConfiguration(); // write zoom configuration to raspberry pi
                _autoZoom.writeZoomValues(); //rvtodo comment out
            }
        }

        // eventhandlier start autozoom calibration
        private void buttonCalib_Click(object sender, EventArgs e)
        {
            _autoZoom.calibrate();
        }

        // autozoom test move to first five positions in a loop 
        private void buttonZoomTest_Click(object sender, EventArgs e)
        {
            if (loginUser("Admin") == true)     // superuser login necessary
            {
                _autoZoom.test();
            }
        }

        // autozoom servo test move
        private void buttonServoStart_Click(object sender, EventArgs e)
        {
            _autoZoom.servoMove();
        }

        // autozoom move servo to middle position
        private void buttonServoStop_Click(object sender, EventArgs e)
        {
            if (loginUser("Admin") == true)     // superuser login necessary
            {
                _autoZoom.servoMiddle();
            }
        }

        // eventhandler reset button autozoom config 
        private void buttonAZReset_Click(object sender, EventArgs e)
        {
            _audioMix._rasPi.rasPiStop();
        }

        // eventhandler move reference
        private void buttonZoomReference_Click(object sender, EventArgs e)
        {
            moveZoomReference();
        }

        // eventhandler test left right servo position servo Zoom
        private void buttonZoomTestMiddle_Click(object sender, EventArgs e)
        {
            if (loginUser("SuperUser") == true) // super user login necessary
            {
                // _autoZoom._AZLastServoPosition = (byte)AutoZoomControl.AZ_SERVOPOS.AZ_CON_RIGHT;
                _autoZoom.servoMove();
            }
        }

        // eventhandler Test middle position servo Zoom
        private void buttonZoomServoMiddle_Click(object sender, EventArgs e)
        {
            if (loginUser("SuperUser") == true) // super user login necessary
            {
                _autoZoom.servoMiddle();
            }
        }

        private EventTimer _eventTimer = new EventTimer();
        private AudioMix _audioMix = new AudioMix();
        private InfraredControl _infraredControl = new InfraredControl();
        private PositionControl _positionControl = new PositionControl();
        private AutoZoomControl _autoZoom = new AutoZoomControl();
        private ATEMControl _atemControl;  // ✅ NEU: ATEM Mini Pro Control
        private ROKU_TV_Remote _rokuTV;
        private string _User = "DefaultUser"; // set user
        private string _requestedUser = "DefaultUser"; // requestet user
        private static uint _requestedFunction = 0;
        private System.Windows.Forms.Timer _Inputtimer = new System.Windows.Forms.Timer(); // input sequence of password 
        private uint _InputTimerloop = 0;
        public LogData _logDat = new LogData();
        private bool CI_test_active_ = false;

        /// <summary>
        /// Safely retrieves zoom value at specified index with bounds checking.
        /// </summary>
        /// <param name="index">Index of zoom value to retrieve</param>
        /// <returns>Zoom value at index, or 0 if index is out of bounds</returns>
        private byte GetZoomValueSafely(int index)
        {
            if (_autoZoom?._AZ_ZoomValue == null)
            {
                _logDat?.sendInfoMessage("JokiAutomation\nZoom-Werte nicht initialisiert.");
                return 0;
            }

            if (index < 0 || index >= _autoZoom._AZ_ZoomValue.Length)
            {
                _logDat?.sendInfoMessage($"JokiAutomation\nUngültiger Zoom-Index: {index} (gültig: 0-{_autoZoom._AZ_ZoomValue.Length - 1})");
                return 0;
            }

            return _autoZoom._AZ_ZoomValue[index];
        }

        /// <summary>
        /// Safely sets zoom value at specified index with bounds checking.
        /// </summary>
        /// <param name="index">Index where to set zoom value</param>
        /// <param name="value">Zoom value to set (0-100)</param>
        /// <returns>True if successful, false otherwise</returns>
        private bool SetZoomValueSafely(int index, byte value)
        {
            if (_autoZoom?._AZ_ZoomValue == null)
            {
                _logDat?.sendInfoMessage("JokiAutomation\nZoom-Werte nicht initialisiert.");
                return false;
            }

            if (index < 0 || index >= _autoZoom._AZ_ZoomValue.Length)
            {
                _logDat?.sendInfoMessage($"JokiAutomation\nUngültiger Zoom-Index: {index}");
                return false;
            }

            if (value > 100)
            {
                _logDat?.sendInfoMessage($"JokiAutomation\nUngültiger Zoom-Wert: {value} (gültig: 0-100)");
                return false;
            }

            _autoZoom._AZ_ZoomValue[index] = value;
            return true;
        }

        // eventhandler cam pos value index changed, set corresponding zoomVal from RasPi
        private void listBoxCamPosControl_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (TabControl1.SelectedIndex == TAB_INDEX_POSITION_CONTROL)
            {
                byte zoomVal = GetZoomValueSafely(listBoxCamPosControl.SelectedIndex);
                _autoZoom?.setZoomValue(zoomVal);
                zoomValue.Text = zoomVal.ToString();
                _requestedFunction = 0;
            }
        }

        // Vereinheitliche Reset-Buttons
        private void ResetRaspberryPi_Click(object sender, EventArgs e)
        {
            _audioMix?._rasPi?.rasPiStop();
        }

        // Im Designer oder Constructor:
        // button13.Click += ResetRaspberryPi_Click;
        // button14.Click += ResetRaspberryPi_Click;
        // button15.Click += ResetRaspberryPi_Click;
        // buttonAZReset.Click += ResetRaspberryPi_Click;

        // Vereinheitliche RasPi Default Switch
        private void RaspberryPiDefaultSwitch_Click(object sender, EventArgs e)
        {
            _audioMix?._rasPi?.rasPiDefaultSwitch();
        }

        // button10.Click += RaspberryPiDefaultSwitch_Click;
        // button11.Click += RaspberryPiDefaultSwitch_Click;

        // Add this method to your Form1 class

/// <summary>
/// Configures the UI based on PTZ camera mode.
/// Disables and hides Zoom-related controls and AutoZoom Config tab in PTZ mode.
/// </summary>
private void ConfigureUIForPtzMode()
{
    if (_isPtzCameraMode)
    {
        // ============================================
        // PTZ MODE: Hide legacy zoom controls, show PTZ zoom buttons
        // ============================================
        
        // Hide Legacy Zoom Controls
        testPos.Visible = false;
        testPos.Enabled = false;
        
        testPosSwitch.Visible = false;
        testPosSwitch.Enabled = false;
        
        teachNullPos.Visible = false;
        teachNullPos.Enabled = false;
        
        buttonZoomReference.Visible = false;
        buttonZoomReference.Enabled = false;
        
        buttonZoom.Visible = false;
        buttonZoom.Enabled = false;
        
        zoomValue.Visible = false;
        zoomValue.Enabled = false;
        
        labelAutozoom.Visible = false;
        labelAutozoom.Enabled = false;

        buttonZoomServoMiddle.Visible = false;
        buttonZoomServoMiddle.Enabled = false;
        
        buttonZoomTestMiddle.Visible = false;
        buttonZoomTestMiddle.Enabled = false;
        
        // ✅ Show PTZ Zoom Buttons
        zoomIn.Visible = true;
        zoomIn.Enabled = true;
        
        zoomOut.Visible = true;
        zoomOut.Enabled = true;
        
        // Hide AutoZoom Config Tab
        TabControl1.TabPages.Remove(tabPage5);
        
        _logDat?.sendInfoMessage("JokiAutomation\nPTZ-Modus: Legacy Zoom deaktiviert, PTZ Zoom-Buttons aktiviert");
    }
    else
    {
        // ============================================
        // LEGACY MODE: Show legacy zoom controls, hide PTZ zoom buttons
        // ============================================
        
        // Show Legacy Zoom Controls
        testPos.Visible = true;
        testPos.Enabled = true;
        
        testPosSwitch.Visible = true;
        testPosSwitch.Enabled = true;
        
        teachNullPos.Visible = true;
        teachNullPos.Enabled = true;
        
        buttonZoomReference.Visible = true;
        buttonZoomReference.Enabled = true;
        
        buttonZoom.Visible = true;
        buttonZoom.Enabled = true;
        
        zoomValue.Visible = true;
        zoomValue.Enabled = true;
        
        labelAutozoom.Visible = true;
        labelAutozoom.Enabled = true;

        buttonZoomServoMiddle.Visible = true;
        buttonZoomServoMiddle.Enabled = true;
        
        buttonZoomTestMiddle.Visible = true;
        buttonZoomTestMiddle.Enabled = true;
        
        // ✅ Hide PTZ Zoom Buttons
        zoomIn.Visible = false;
        zoomIn.Enabled = false;
        
        zoomOut.Visible = false;
        zoomOut.Enabled = false;
        
        // Show AutoZoom Config Tab
        if (!TabControl1.TabPages.Contains(tabPage5))
        {
            TabControl1.TabPages.Add(tabPage5);
        }
        
        _logDat?.sendInfoMessage("JokiAutomation\nLegacy-Modus: Zoom-Controls aktiviert, PTZ Zoom-Buttons deaktiviert");
    }
}

// buttonhandler init Audiocontrol  enables activated audiochannels and sets volume to maximum  
private void button5_Click(object sender, EventArgs e)
{
    int commandID = AudioMix.AM_ACTIVE;
    for (int i = 0; i < 4; i++) // add active channels to ID
    {
        if (_audioMix.channelActive_[i] == true)
        {
            commandID += 1 << i;
        }
    }
    _audioMix.executeAudio(commandID);
}

// buttonhandler reset Audiocontrol resets volume, fader and mutes all audio channels
private void button6_Click(object sender, EventArgs e)
{
    _audioMix.executeAudio(AudioMix.AM_AUDIO_RESET);
}

private void moveCamPos_Click(object sender, EventArgs e)
{
    try
    {
        _requestedFunction = 0;
        int selectedPosition = listBoxCamPosControl.SelectedIndex;

        if (selectedPosition < 0)
        {
            _logDat?.sendInfoMessage("JokiAutomation\nBitte wählen Sie eine Position aus!");
            return;
        }

        string positionName = listBoxCamPosControl.Items[selectedPosition].ToString();

        if (_isPtzCameraMode)
        {
            if (_canonPtzController == null || !_canonPtzController.IsConnected)
            {
                _logDat?.sendInfoMessage("JokiAutomation\n⚠ PTZ-Kamera nicht verbunden!");
                _logDat?.sendInfoMessage("JokiAutomation\nBitte prüfen Sie die Kamera-Verbindung");
                return;
            }
            _logDat?.sendInfoMessage($"JokiAutomation\nBewege PTZ-Kamera zu Position {selectedPosition}: {positionName}");
        }
        else
        {
            _logDat?.sendInfoMessage($"JokiAutomation\nBewege RasPi-Motor zu Position {selectedPosition}: {positionName}");
        }

        _positionControl.moveToPos(selectedPosition);
    }
    catch (Exception ex)
    {
        _logDat?.sendInfoMessage($"JokiAutomation\nFehler beim Bewegen der Position:\n{ex.Message}\n{ex.StackTrace}");
    }
}

/// <summary>
/// Initializes Canon CR-N100 PTZ camera if PTZ_CAM mode is enabled
/// </summary>
private async Task InitializeCanonPtzControlAsync()
{
    if (!_isPtzCameraMode)
    {
        _logDat?.sendInfoMessage("JokiAutomation\nPTZ_CAM = false, Kamera-Positionierung via RasPi Motor");
        return;
    }

    try
    {
        string configPath = Path.Combine(Application.StartupPath, NETWORK_CONFIG_FILE);

        if (!File.Exists(configPath))
        {
            _logDat?.sendInfoMessage($"JokiAutomation\nFehler: {NETWORK_CONFIG_FILE} nicht gefunden!");
            return;
        }

        _logDat?.sendInfoMessage($"JokiAutomation\nLade Canon PTZ Konfiguration aus {NETWORK_CONFIG_FILE}...");

        CameraConfig config = NetworkCfgReader.LoadCamera(configPath, CANON_CAMERA_CONFIG_KEY);

        _logDat?.sendInfoMessage($"JokiAutomation\nKonfiguration geladen:");
        _logDat?.sendInfoMessage($"JokiAutomation\n  IP: {config.IpAddress}:{config.Port}");
        _logDat?.sendInfoMessage($"JokiAutomation\n  User: {config.Username ?? "admin"}");
        _logDat?.sendInfoMessage($"JokiAutomation\n  Protocol: {config.Protocol ?? "XC"}");
        _logDat?.sendInfoMessage($"JokiAutomation\n  HTTPS: {config.UseHttps}");

        _canonPtzController = CreatePtzController(config);

        _logDat?.sendInfoMessage($"JokiAutomation\nVerbinde mit Canon PTZ: {config.IpAddress}:{config.Port}...");

        var connectResult = await _canonPtzController.ConnectAsync();

        if (connectResult.Success)
        {
            _logDat?.sendInfoMessage($"JokiAutomation\n✓ Canon CR-N100 verbunden ({config.Protocol ?? "XC"} Protocol)");
        }
        else
        {
            _logDat?.sendInfoMessage($"JokiAutomation\n✗ Canon CR-N100 Verbindungsfehler: {connectResult.Message}");
            _canonPtzController = null;
        }
    }
    catch (Exception ex)
    {
        _logDat?.sendInfoMessage($"JokiAutomation\n✗ Canon PTZ Initialisierungsfehler: {ex.Message}");
        _canonPtzController = null;
    }
}

/// <summary>
/// Create PTZ controller
/// </summary>
private ICanonPtzController CreatePtzController(CameraConfig config)
{
    if (string.Equals(config.Protocol, "XC", StringComparison.OrdinalIgnoreCase))
    {
        _logDat?.sendInfoMessage("JokiAutomation\nVerwende XC Protocol Controller");
        return new XcCanonPtzController(config);
    }

    _logDat?.sendInfoMessage("JokiAutomation\nVerwende Legacy AW Protocol Controller");
    return new LegacyAwPtzController(
        config.IpAddress,
        config.Port,
        config.Username ?? "admin",
        config.Password ?? "",
        config.UseHttps);
}

/// <summary>
/// Initializes the ATEM Mini Pro control connection
/// </summary>
private void InitializeATEMControl()
{
    try
    {
        if (!_networkDevices.ContainsKey("ATEM_MiniPro"))
        {
            _logDat?.sendInfoMessage("JokiAutomation\nATEM Mini Pro nicht in Network.cfg konfiguriert!");
            return;
        }

        string atemIP = _networkDevices["ATEM_MiniPro"].IPAddress;
        _atemControl = new ATEMControl(atemIP);
        bool connected = _atemControl.Connect();

        if (connected)
        {
            _logDat?.sendInfoMessage($"JokiAutomation\nATEM Mini Pro erfolgreich verbunden ({atemIP})");
        }
        else
        {
            _logDat?.sendInfoMessage($"JokiAutomation\nATEM Mini Pro Verbindung fehlgeschlagen ({atemIP})");
        }
    }
    catch (Exception ex)
    {
        _logDat?.sendInfoMessage($"JokiAutomation\nATEM Verbindungsfehler: {ex.Message}");
        _atemControl = null;
    }
}

/// <summary>
/// Initialize ATEM Mini Pro to default state
/// </summary>
private void InitializeATEMToDefault()
{
    try
    {
        if (!_networkDevices.ContainsKey("ATEM_MiniPro"))
        {
            _logDat?.sendInfoMessage("JokiAutomation\nATEM Mini Pro nicht in Network.cfg konfiguriert!");
            return;
        }

        if (_atemControl == null || !_atemControl.IsConnected)
        {
            string atemIP = _networkDevices["ATEM_MiniPro"].IPAddress;
            _atemControl = new ATEMControl(atemIP);

            if (!_atemControl.Connect())
            {
                _logDat?.sendInfoMessage($"JokiAutomation\nATEM Verbindung fehlgeschlagen ({atemIP}:9910)");
                return;
            }

            _logDat?.sendInfoMessage($"JokiAutomation\nATEM Mini Pro verbunden ({atemIP})");
        }

        _logDat?.sendInfoMessage("JokiAutomation\nATEM Mini Pro wird initialisiert...");

        if (_atemControl != null && _atemControl.IsConnected)
        {
            _atemControl.InitializeToDefaultState();
        }
    }
    catch (Exception ex)
    {
        _logDat?.sendInfoMessage($"JokiAutomation\nATEM Initialisierung fehlgeschlagen:\n{ex.Message}");
    }
}

/// <summary>
/// Switch ATEM to specified HDMI input
/// </summary>
private void SwitchATEMInput(ATEMInput input)
{
    if (_atemControl == null || !_atemControl.IsConnected)
    {
        _logDat?.sendInfoMessage("JokiAutomation\nATEM nicht verbunden");
        return;
    }

    try
    {
        ATEMControl.VideoSource source = (ATEMControl.VideoSource)((int)input);
        _atemControl.TransitionToProgramInput(source);

        string sourceName = GetATEMInputName(input);
        _logDat?.sendInfoMessage($"JokiAutomation\nATEM umgeschaltet auf {sourceName} (HDMI {(int)input})");
    }
    catch (Exception ex)
    {
        _logDat?.sendInfoMessage($"JokiAutomation\nATEM Fehler: {ex.Message}");
    }
}

private string GetATEMInputName(ATEMInput input)
{
    switch (input)
    {
        case ATEMInput.GoPro:
            return "GoPro Actionkamera";
        case ATEMInput.Laptop:
            return "Laptop";
        case ATEMInput.CanonPtzMain:
            return "Canon PTZ Hauptkamera";
        case ATEMInput.CanonPtzPreacher:
            return "Canon PTZ Predigtkamera";
        default:
            return $"HDMI {(int)input}";
    }
}

/// <summary>
/// Enable Picture-in-Picture mode
/// </summary>
private void EnablePictureInPicture()
{
    if (_atemControl == null || !_atemControl.IsConnected)
    {
        _logDat?.sendInfoMessage("JokiAutomation\nATEM nicht verbunden");
        return;
    }

    try
    {
        _atemControl.EnablePictureInPicture(
            ATEMControl.VideoSource.Input1,
            ATEMControl.PiPPosition.BottomRight,
            ATEMControl.PiPSize.Small
        );

        if (_atemControl.IsPiPActive())
        {
            _logDat?.sendInfoMessage("JokiAutomation\nBild-in-Bild aktiviert");
        }
        
        Thread.Sleep(500);
    }
    catch (Exception ex)
    {
        _logDat?.sendInfoMessage($"JokiAutomation\nBild-in-Bild Fehler: {ex.Message}");
    }
}

/// <summary>
/// Disable Picture-in-Picture mode
/// </summary>
private void DisablePictureInPicture()
{
    if (_atemControl == null || !_atemControl.IsConnected)
    {
        _logDat?.sendInfoMessage("JokiAutomation\nATEM nicht verbunden");
        return;
    }

    try
    {
        _atemControl.DisablePictureInPicture();
        _logDat?.sendInfoMessage("JokiAutomation\nBild-in-Bild deaktiviert");
    }
    catch (Exception ex)
    {
        _logDat?.sendInfoMessage($"JokiAutomation\nBild-in-Bild Fehler: {ex.Message}");
    }
}

/// <summary>
/// Set ATEM microphone on/off
/// </summary>
private void SetATEMMicrophone(int micNumber, bool enable)
{
    if (_atemControl == null || !_atemControl.IsConnected)
    {
        _logDat?.sendInfoMessage("JokiAutomation\nATEM nicht verbunden");
        return;
    }

    try
    {
        ATEMControl.VideoSource audioSource;
        if (micNumber == 1)
        {
            audioSource = ATEMControl.VideoSource.Input1;
        }
        else if (micNumber == 2)
        {
            audioSource = ATEMControl.VideoSource.Input2;
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(micNumber), "Nur Mikrofon 1 oder 2 werden unterstützt");
        }

        _atemControl.SetAudioMixerInput((ushort)audioSource, enable);
        string state = enable ? "eingeschaltet" : "ausgeschaltet";
        _logDat?.sendInfoMessage($"JokiAutomation\nMikrofon {micNumber} {state}");
    }
    catch (Exception ex)
    {
        _logDat?.sendInfoMessage($"JokiAutomation\nMikrofon-Fehler: {ex.Message}");
    }
}

/// <summary>
/// Get Delock adapter from cache or create new connection
/// </summary>
private DelockSocketAdapter GetDelockAdapter(string name)
{
    if (_delockAdapters != null && _delockAdapters.ContainsKey(name))
    {
        return _delockAdapters[name];
    }

    if (!_networkDevices.ContainsKey(name))
    {
        _logDat?.sendInfoMessage($"JokiAutomation\nDelock Adapter '{name}' nicht in Network.cfg konfiguriert");
        return null;
    }

    NetworkDevice device = _networkDevices[name];
    
    if (!device.IsDelockDevice)
    {
        _logDat?.sendInfoMessage($"JokiAutomation\nGerät '{name}' hat keine Delock-Zugangsdaten");
        return null;
    }
    
    var adapter = new DelockSocketAdapter(
        name,                        
        device.IPAddress,           
        device.Port,                
        device.Username,            
        device.Password             
    );
    
    if (_delockAdapters == null)
        _delockAdapters = new Dictionary<string, DelockSocketAdapter>();
    
    _delockAdapters[name] = adapter;
    
    if (!adapter.Connect())
    {
        _logDat?.sendInfoMessage($"JokiAutomation\nVerbindung zu {name} fehlgeschlagen");
        return null;
    }
    
    _logDat?.sendInfoMessage($"JokiAutomation\n{name} verbunden ({device.IPAddress}:{device.Port})");
    return adapter;
}

/// <summary>
/// Execute Canon PTZ camera scene
/// </summary>
private async Task ExecuteCanonSceneAsync(string sceneName, int presetNumber, ATEMInput atemInput, bool enablePiP = false)
{
    if (_canonPtzController == null || !_canonPtzController.IsConnected)
    {
        _logDat?.sendInfoMessage($"JokiAutomation\nCanon PTZ nicht verbunden");
        return;
    }

    try
    {
        _logDat?.sendInfoMessage($"JokiAutomation\nSzene '{sceneName}' wird ausgeführt...");

        var recallResult = await _canonPtzController.RecallPresetAsync(presetNumber);
        if (recallResult.Success)
        {
            _logDat?.sendInfoMessage($"JokiAutomation\nCanon PTZ: Preset {presetNumber} ({sceneName}) angefahren");
        }
        else
        {
            _logDat?.sendInfoMessage($"JokiAutomation\nCanon PTZ Fehler: {recallResult.Message}");
            return;
        }

        await Task.Delay(2000);

        SwitchATEMInput(atemInput);

        if (enablePiP)
        {
            EnablePictureInPicture();
        }
        else
        {
            DisablePictureInPicture();
        }

        _logDat?.sendInfoMessage($"JokiAutomation\nSzene '{sceneName}' erfolgreich");
    }
    catch (Exception ex)
    {
        _logDat?.sendInfoMessage($"JokiAutomation\nFehler: {ex.Message}");
    }
}

        // ============================================
        // PTZ ZOOM CONTROL EVENT HANDLERS
        // ============================================

        /// <summary>
        /// Zoom In Button pressed (PTZ Mode only)
        /// </summary>
        private void zoomInHandler(object sender, MouseEventArgs e)
        {
            if (_isPtzCameraMode && _canonPtzController != null)
            {
                Task.Run(async () => await _canonPtzController.StartZoomInAsync());
            }
        }

        /// <summary>
        /// Zoom Out Button pressed (PTZ Mode only)
        /// </summary>
        private void zoomOutHandler(object sender, MouseEventArgs e)
        {
            if (_isPtzCameraMode && _canonPtzController != null)
            {
                Task.Run(async () => await _canonPtzController.StartZoomOutAsync());
            }
        }

        /// <summary>
        /// Zoom Button released (PTZ Mode only)
        /// </summary>
        private void zoomStopHandler(object sender, MouseEventArgs e)
        {
            if (_isPtzCameraMode && _canonPtzController != null)
            {
                Task.Run(async () => await _canonPtzController.StopZoomAsync());
            }
        }

       // Auto-Tracking Toggle Button - Position Control Tab
        private async void buttonAutoTracking_Click(object sender, EventArgs e)
        {
            if (!_isPtzCameraMode || _canonPtzController == null)
            {
                _logDat?.sendInfoMessage("Position Control\nAuto-Tracking nur im PTZ-Kamera-Modus verfügbar");
                return;
            }

            if (!_canonPtzController.IsConnected)
            {
                _logDat?.sendInfoMessage("Position Control\nKamera nicht verbunden");
                MessageBox.Show("Kamera ist nicht verbunden!", "Auto-Tracking", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                buttonAutoTracking.Enabled = false;
                
                CommandResult result;
                var xcController = _canonPtzController as XcCanonPtzController;
                
                if (xcController != null)
                {
                    var autoTrackingService = xcController.GetAutoTrackingService();
                    bool isCurrentlyEnabled = await autoTrackingService.IsEnabledAsync();
                    
                    if (isCurrentlyEnabled)
                    {
                        _logDat?.sendInfoMessage("Position Control\nDeaktiviere Auto-Tracking...");
                        result = await _canonPtzController.DisableTrackingAsync();
                    }
                    else
                    {
                        _logDat?.sendInfoMessage("Position Control\nStoppe Kamera-Bewegung...");
                        await _canonPtzController.StopAllAsync();
                        await Task.Delay(500);
                        
                        _logDat?.sendInfoMessage("Position Control\nAktiviere Auto-Tracking...");
                        result = await _canonPtzController.EnableTrackingSingleAsync();
                    }
                }
                else
                {
                    await _canonPtzController.StopAllAsync();
                    await Task.Delay(500);
                    result = await _canonPtzController.EnableTrackingSingleAsync();
                }
                
                if (result.Success)
                {
                    _logDat?.sendInfoMessage($"Position Control\n✓ {result.Message}");
                    await UpdateAutoTrackingButtonStateAsync();
                }
                else
                {
                    _logDat?.sendInfoMessage($"Position Control\n✗ Auto-Tracking Fehler:\n{result.Message}");
                    MessageBox.Show($"Auto-Tracking Fehler:\n\n{result.Message}", "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                _logDat?.sendInfoMessage($"Position Control\nAuto-Tracking Exception:\n{ex.Message}");
                MessageBox.Show($"Fehler:\n{ex.Message}", "Auto-Tracking Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                buttonAutoTracking.Enabled = true;
            }
        }

        // Update Auto-Tracking Button Status (Farbe und Text)
        private async Task UpdateAutoTrackingButtonStateAsync()
        {
            if (!_isPtzCameraMode || _canonPtzController == null || !_canonPtzController.IsConnected)
            {
                buttonAutoTracking.BackColor = Color.Gray;
                buttonAutoTracking.ForeColor = Color.White;
                buttonAutoTracking.Text = "Auto-Track\n(nicht verfügbar)";
                buttonAutoTracking.Enabled = false;
                return;
            }

            try
            {
                var xcController = _canonPtzController as XcCanonPtzController;
                
                if (xcController != null)
                {
                    var autoTrackingService = xcController.GetAutoTrackingService();
                    bool isEnabled = await autoTrackingService.IsEnabledAsync();
                    
                    if (isEnabled)
                    {
                        buttonAutoTracking.BackColor = Color.LimeGreen;
                        buttonAutoTracking.ForeColor = Color.Black;
                        buttonAutoTracking.Text = "Auto-Track\nEIN";
                    }
                    else
                    {
                        buttonAutoTracking.BackColor = Color.Red;
                        buttonAutoTracking.ForeColor = Color.White;
                        buttonAutoTracking.Text = "Auto-Track\nAUS";
                    }
                    
                    buttonAutoTracking.Enabled = true;
                }
                else
                {
                    buttonAutoTracking.BackColor = Color.Gray;
                    buttonAutoTracking.ForeColor = Color.White;
                    buttonAutoTracking.Text = "Auto-Track\n(nicht verfügbar)";
                    buttonAutoTracking.Enabled = false;
                }
            }
            catch
            {
                buttonAutoTracking.BackColor = Color.Gray;
                buttonAutoTracking.ForeColor = Color.White;
                buttonAutoTracking.Text = "Auto-Track\n(Fehler)";
                buttonAutoTracking.Enabled = false;
            }
        }

        public bool IsPtzInitialized()
        {
            // Wenn PTZ-Modus deaktiviert ist, gilt es als "initialisiert"
            if (!_isPtzCameraMode)
            {
                return true;
            }

            // Im PTZ-Modus: Prüfe ob Controller existiert und verbunden ist
            return _canonPtzController != null && _canonPtzController.IsConnected;
        }

        // Add this method to your Form1 class (anywhere in the class, e.g., near other config helpers)
        private string FindNetworkConfigFile()
        {
            // Search in several common locations for the config file
            string[] possiblePaths = new[]
            {
                Path.Combine(Application.StartupPath, NETWORK_CONFIG_FILE),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, NETWORK_CONFIG_FILE),
                Path.Combine(Directory.GetCurrentDirectory(), NETWORK_CONFIG_FILE)
            };

            foreach (string path in possiblePaths)
            {
                if (File.Exists(path))
                    return path;
            }

            return null;
        }

/// <summary>
/// Shutdown PTZ Camera: Move to home position, enter standby, and disconnect
/// </summary>
private async Task ShutdownPtzCameraAsync()
{
    if (!_isPtzCameraMode || _canonPtzController == null)
    {
        _logDat?.sendInfoMessage("JokiAutomation\nPTZ-Kamera nicht konfiguriert - Shutdown übersprungen");
        return;
    }

    if (!_canonPtzController.IsConnected)
    {
        _logDat?.sendInfoMessage("JokiAutomation\nPTZ-Kamera bereits getrennt");
        return;
    }

    try
    {
        _logDat?.sendInfoMessage("JokiAutomation\n=== PTZ-Kamera Shutdown gestartet ===");
        
        // Step 1: Disable Auto-Tracking if active
        _logDat?.sendInfoMessage("JokiAutomation\nSchritt 1: Deaktiviere Auto-Tracking...");
        await _canonPtzController.DisableTrackingAsync();
        await Task.Delay(500);
        
        // Step 2: Stop all movements
        _logDat?.sendInfoMessage("JokiAutomation\nSchritt 2: Stoppe alle Bewegungen...");
        await _canonPtzController.StopAllAsync();
        await Task.Delay(500);
        
        // Step 3: Move to home/null position
        _logDat?.sendInfoMessage("JokiAutomation\nSchritt 3: Fahre zu Nullposition...");
        var homeResult = await _canonPtzController.RecallPresetAsync(0); // Preset 0 as "home"
        
        if (homeResult.Success)
        {
            _logDat?.sendInfoMessage($"JokiAutomation\n✓ {homeResult.Message}");
        }
        else
        {
            _logDat?.sendInfoMessage($"JokiAutomation\n⚠ Warnung: {homeResult.Message}");
        }
        
        // Wait for camera to reach home position
        await Task.Delay(3000);
        
        // Step 4: Enter standby mode
        _logDat?.sendInfoMessage("JokiAutomation\nSchritt 4: Aktiviere Standby-Modus...");
        var standbyResult = await _canonPtzController.SetStandbyAsync(true);
        
        if (standbyResult.Success)
        {
            _logDat?.sendInfoMessage($"JokiAutomation\n✓ {standbyResult.Message}");
        }
        else
        {
            _logDat?.sendInfoMessage($"JokiAutomation\n⚠ Warnung: {standbyResult.Message}");
        }
        
        await Task.Delay(1000);
        
        // Step 5: Disconnect Canon Remote App
        _logDat?.sendInfoMessage("JokiAutomation\nSchritt 5: Trenne Canon Remote App...");
        var disconnectResult = await _canonPtzController.DisconnectAsync();
        
        if (disconnectResult.Success)
        {
            _logDat?.sendInfoMessage($"JokiAutomation\n✓ Canon Remote App getrennt");
        }
        else
        {
            _logDat?.sendInfoMessage($"JokiAutomation\n⚠ Warnung: {disconnectResult.Message}");
        }
        
        // Step 6: Dispose controller to release resources
        _logDat?.sendInfoMessage("JokiAutomation\nSchritt 6: Gebe Ressourcen frei...");
        if (_canonPtzController is IDisposable disposable)
        {
            disposable.Dispose();
            _logDat?.sendInfoMessage("JokiAutomation\n✓ Ressourcen freigegeben");
        }
        
        // Clear reference
        _canonPtzController = null;
        
        _logDat?.sendInfoMessage("JokiAutomation\n=== PTZ-Kamera Shutdown abgeschlossen ===");
    }
    catch (Exception ex)
    {
        _logDat?.sendInfoMessage($"JokiAutomation\n✗ PTZ-Kamera Shutdown Fehler: {ex.Message}");
        _logDat?.sendInfoMessage($"JokiAutomation\nStackTrace: {ex.StackTrace}");
        
        // Try to dispose anyway
        try
        {
            if (_canonPtzController is IDisposable disposable)
            {
                disposable.Dispose();
            }
            _canonPtzController = null;
        }
        catch
        {
            // Ignore disposal errors
        }
    }
}

    }
}

