using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
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

        // Requested Function ID's
        private const uint FUNCTION_CALIBRATE_MAGNETOMETER = 1;
        private const uint FUNCTION_TEACH_AUDIO = 2;
        private const uint FUNCTION_TEACH_INFRARED = 3;
        private const uint FUNCTION_TEACH_POSITION = 4;
        private const uint FUNCTION_CALIBRATE_GYROSCOPE = 5;
        private const uint FUNCTION_TEACH_NULL_POSITION = 6;

        // Configuration
        private const string NETWORK_CONFIG_FILE = "Network.cfg";
        private const bool ShutdownRasPiWhenIrShutdownFails = false;
        private Dictionary<string, NetworkDevice> _networkDevices;
        private Dictionary<string, DelockSocketAdapter> _delockAdapters;
        private Dictionary<string, string> _userPasswords;

        // PTZ Camera Configuration
        private const string PTZ_MODE_CONFIG_KEY = "PTZ_CAM";
        private const string CANON_CAMERA_CONFIG_KEY = "CANON_CRN100";
        private const string PTZ_PRESET_RECALL_PATH_CONFIG_KEY = "PTZ_PRESET_RECALL_PATH";
        private const string PTZ_PRESET_RECALL_PATHS_CONFIG_KEY = "PTZ_PRESET_RECALL_PATHS";
        // Dropbox API v2 Configuration
        private const string DROPBOX_APP_KEY_CONFIG_KEY = "DROPBOX_APP_KEY";
        private const string DROPBOX_APP_SECRET_CONFIG_KEY = "DROPBOX_APP_SECRET";
        private const string DROPBOX_REFRESH_TOKEN_CONFIG_KEY = "DROPBOX_REFRESH_TOKEN";

        private bool _isPtzCameraMode;
        private ICanonPtzController _canonPtzController;
        private string _ptzPresetRecallPath;
        private string _dropboxAppKey;
        private string _dropboxAppSecret;
        private string _dropboxRefreshToken;
        private List<string> _ptzPresetRecallFallbackPaths = new List<string>();

        private volatile string _currentCommand = string.Empty;
        internal readonly AsyncLocal<string> _activeCommandRequestId = new AsyncLocal<string>();
        private static int _activePositionControls;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        private const int SW_RESTORE = 9;

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
            LogData.LogLifecycle("UNKNOWN", "FORM_CONSTRUCTOR_ENTER");

            // ── Erfasse UI-Thread und SynchronizationContext am Konstruktor ────
            _uiThreadId = Thread.CurrentThread.ManagedThreadId;
            _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            Debug.WriteLine($"📋 Form1 UI-Thread: ThreadId={_uiThreadId}, SyncContext={_uiContext?.GetType().Name}");

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

            LogData.LogLifecycle(ResolveLifecycleMode(), "FORM_CONSTRUCTOR_EXIT");
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

                    // Dropbox API v2 configuration
                    if (deviceName.Equals(DROPBOX_APP_KEY_CONFIG_KEY, StringComparison.OrdinalIgnoreCase))
                    {
                        _dropboxAppKey = parts[1].Trim();
                        _logDat?.sendInfoMessage("JokiAutomation\nDROPBOX_APP_KEY konfiguriert.");
                        continue;
                    }

                    if (deviceName.Equals(DROPBOX_APP_SECRET_CONFIG_KEY, StringComparison.OrdinalIgnoreCase))
                    {
                        _dropboxAppSecret = parts[1].Trim();
                        _logDat?.sendInfoMessage("JokiAutomation\nDROPBOX_APP_SECRET konfiguriert.");
                        continue;
                    }

                    if (deviceName.Equals(DROPBOX_REFRESH_TOKEN_CONFIG_KEY, StringComparison.OrdinalIgnoreCase))
                    {
                        _dropboxRefreshToken = parts[1].Trim();
                        _logDat?.sendInfoMessage("JokiAutomation\nDROPBOX_REFRESH_TOKEN konfiguriert.");
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

            NetworkDevice rokuDevice = _networkDevices["Roku_TV"];
            string rokuIP = rokuDevice.IPAddress;
            int rokuPort = rokuDevice.Port > 0 ? rokuDevice.Port : 8060;

            _rokuTV = new ROKU_TV_Remote(rokuIP, rokuPort);
            _logDat?.sendInfoMessage($"JokiAutomation\nRoku TV konfiguriert für IP: {rokuIP}:{rokuPort}");
        }

        private async Task EnsureStartupInitAsync()
        {
            if (_startupInitCompleted)
            {
                return;
            }

            if (!await _startupInitSemaphore.WaitAsync(0))
            {
                while (!_startupInitCompleted)
                {
                    await Task.Delay(100);
                }

                return;
            }

            try
            {
                if (_startupInitCompleted)
                {
                    return;
                }

                _logDat?.sendInfoMessage(
                    "JokiAutomation\nStartupInit wird ausgeführt...");

                bool remoteControlReady =
                    await TryStartRemoteControlApplicationAsync();

                if (!remoteControlReady)
                {
                    _logDat?.sendInfoMessage(
                        "JokiAutomation\nStartupInit abgebrochen: Canon Remote nicht bereit.");

                    return;
                }

                await Task.Run(() => PowerOnRakutenTv());

                bool rasPiTimeSynced = await _infraredControl.SyncRaspberryPiDateTimeWithWindowsAsync();
                _logDat?.sendInfoMessage(rasPiTimeSynced
                    ? "JokiAutomation\nInitialisierung: Raspberry Pi Zeit erfolgreich synchronisiert."
                    : "JokiAutomation\nInitialisierung: Raspberry Pi Zeit konnte nicht synchronisiert werden.");

                await DisableCanonAutotrackAsync();

                _startupInitCompleted = true;
                _logDat?.sendInfoMessage("JokiAutomation\nStartupInit abgeschlossen.");
            }
            catch (Exception ex)
            {
                _logDat?.sendInfoMessage($"JokiAutomation\nStartupInit Fehler: {ex.Message}");
            }
            finally
            {
                _startupInitSemaphore.Release();
            }
        }

        private async Task<bool> TryStartRemoteControlApplicationAsync()
        {
            const string processName = "CanonRemoteControl";

            _logDat?.sendInfoMessage(
                "JokiAutomation\nREMOTE_CONTROL_START_BEGIN");

            try
            {
                var running =
                    Process.GetProcessesByName(processName)
                           .FirstOrDefault();

                if (running != null)
                {
                    _logDat?.sendInfoMessage(
                        $"JokiAutomation\nRemote Control läuft bereits. PID={running.Id}");

                    return true;
                }

                string[] candidatePaths =
                {
            Path.Combine(Application.StartupPath, "CanonRemoteControl.exe"),
            Path.GetFullPath(Path.Combine(
                Application.StartupPath,
                @"..\..\..\..\CanonRemoteControl\bin\x64\Release\CanonRemoteControl.exe")),
            Path.GetFullPath(Path.Combine(
                Application.StartupPath,
                @"..\..\..\..\CanonRemoteControl\bin\Release\CanonRemoteControl.exe"))
        };

                string remoteControlPath =
                    candidatePaths.FirstOrDefault(File.Exists);

                if (string.IsNullOrWhiteSpace(remoteControlPath))
                {
                    _logDat?.sendInfoMessage(
                        "JokiAutomation\nREMOTE_CONTROL_START_FAILED CanonRemoteControl.exe nicht gefunden.");

                    return false;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = remoteControlPath,
                    WorkingDirectory = Path.GetDirectoryName(remoteControlPath),
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Minimized
                });

                _logDat?.sendInfoMessage(
                    $"JokiAutomation\nRemote Control minimiert gestartet: {remoteControlPath}");

                var sw = Stopwatch.StartNew();

                while (sw.ElapsedMilliseconds < 5000)
                {
                    var process =
                        Process.GetProcessesByName(processName)
                               .FirstOrDefault();

                    if (process != null)
                    {
                        process.Refresh();

                        if (process.HasExited)
                        {
                            _logDat?.sendInfoMessage(
                                "JokiAutomation\nREMOTE_CONTROL_START_FAILED ProcessExited");

                            return false;
                        }

                        if (process.MainWindowHandle != IntPtr.Zero)
                        {
                            _logDat?.sendInfoMessage(
                                $"JokiAutomation\nREMOTE_CONTROL_WINDOW_FOUND PID={process.Id}");

                            _logDat?.sendInfoMessage(
                                "JokiAutomation\nREMOTE_CONTROL_START_SUCCESS");

                            return true;
                        }
                    }

                    await Task.Delay(100);
                }

                _logDat?.sendInfoMessage(
                    "JokiAutomation\nREMOTE_CONTROL_START_TIMEOUT");

                return false;
            }
            catch (Exception ex)
            {
                _logDat?.sendInfoMessage(
                    $"JokiAutomation\nREMOTE_CONTROL_START_FAILED {ex.Message}");

                return false;
            }
        }

        private async Task DisableCanonAutotrackAsync()
        {
            const string processName = "CanonRemoteControl";

            try
            {
                Process process = Process.GetProcessesByName(processName).FirstOrDefault();
                if (process == null)
                {
                    _logDat?.sendInfoMessage("JokiAutomation\nInitialisierung: Canon Remote läuft nicht, Shift+Alt+N übersprungen.");
                    return;
                }

                process.Refresh();
                if (process.MainWindowHandle == IntPtr.Zero)
                {
                    await Task.Delay(500);
                    process.Refresh();
                }

                if (process.MainWindowHandle == IntPtr.Zero)
                {
                    _logDat?.sendInfoMessage("JokiAutomation\nInitialisierung: Canon Remote Fenster nicht gefunden, Shift+Alt+N übersprungen.");
                    return;
                }

                ShowWindowAsync(process.MainWindowHandle, SW_RESTORE);
                SetForegroundWindow(process.MainWindowHandle);
                await Task.Delay(300);

                SendKeys.SendWait("+%n");
                _logDat?.sendInfoMessage("JokiAutomation\nInitialisierung: Canon Autotrack mit Shift+Alt+N ausgeschaltet.");
            }
            catch (Exception ex)
            {
                _logDat?.sendInfoMessage($"JokiAutomation\nInitialisierung: Fehler beim Senden von Shift+Alt+N: {ex.Message}");
            }
        }

        private void PowerOnRakutenTv()
        {
            DelockSocketAdapter hdmiDelockTransmitSupply = GetDelockAdapter("HDMI_Extender_Transmitter");
            DelockSocketAdapter hdmiDelockTVReceiverSupply = GetDelockAdapter("HDMI_Extender_TV_Receiver");

            if (hdmiDelockTransmitSupply != null)
            {
                hdmiDelockTransmitSupply.TurnOnSocket(1);
                _logDat?.sendInfoMessage("JokiAutomation\nRakuten TV: HDMI Extender Transmitter Steckdose eingeschaltet.");
            }

            Thread.Sleep(1000);

            if (hdmiDelockTVReceiverSupply != null)
            {
                hdmiDelockTVReceiverSupply.TurnOnSocket(1);
                _logDat?.sendInfoMessage("JokiAutomation\nRakuten TV: HDMI Extender TV Receiver Steckdose eingeschaltet.");
            }

            Thread.Sleep(1000);

            if (_rokuTV == null)
            {
                InitializeRokuTV();
            }

            if (_rokuTV != null)
            {
               bool powerOnOk = _rokuTV.PowerOn();
                if (powerOnOk)
                {
                    Thread.Sleep(3000);
                    bool hdmi2Ok = _rokuTV.SwitchToHDMI2();
                    _logDat?.sendInfoMessage(hdmi2Ok
                        ? "JokiAutomation\nRakuten TV eingeschaltet und auf HDMI2 geschaltet."
                        : "JokiAutomation\nRakuten TV eingeschaltet, HDMI2 konnte nicht aktiviert werden.");
                }
                else
                {
                    _logDat?.sendInfoMessage($"JokiAutomation\nRakuten TV PowerOn fehlgeschlagen (false). Prüfe in Network.cfg den Eintrag Roku_TV;IP;Port (Standard-Port 8060).");
                }
            }
            else
            {
                _logDat?.sendInfoMessage("JokiAutomation\nRakuten TV konnte nicht eingeschaltet werden: Roku nicht initialisiert.");
            }
        }

        private void PowerOffRakutenTv()
        {
            DelockSocketAdapter hdmiDelockTransmitSupply = GetDelockAdapter("HDMI_Extender_Transmitter");
            DelockSocketAdapter hdmiDelockTVReceiverSupply = GetDelockAdapter("HDMI_Extender_TV_Receiver");

            if (_rokuTV == null)
            {
                InitializeRokuTV();
            }

            if (_rokuTV != null)
            {
                _rokuTV.PowerOff();
                _logDat?.sendInfoMessage("JokiAutomation\nRakuten TV ausgeschaltet.");
            }
            else
            {
                _logDat?.sendInfoMessage("JokiAutomation\nRakuten TV konnte nicht ausgeschaltet werden: Roku nicht initialisiert.");
            }

            Thread.Sleep(1000);

            if (hdmiDelockTVReceiverSupply != null)
            {
                hdmiDelockTVReceiverSupply.TurnOffSocket(1);
                _logDat?.sendInfoMessage("JokiAutomation\nRakuten TV: HDMI Extender TV Receiver Steckdose ausgeschaltet.");
            }

            Thread.Sleep(1000);

            if (hdmiDelockTransmitSupply != null)
            {
                hdmiDelockTransmitSupply.TurnOffSocket(1);
                _logDat?.sendInfoMessage("JokiAutomation\nRakuten TV: HDMI Extender Transmitter Steckdose ausgeschaltet.");
            }
        }

        private async Task ExportLogsToDropBoxAsync()
        {
            if (string.IsNullOrWhiteSpace(_dropboxAppKey) || 
                string.IsNullOrWhiteSpace(_dropboxAppSecret) || 
                string.IsNullOrWhiteSpace(_dropboxRefreshToken))
            {
                _logDat?.sendInfoMessage("JokiAutomation\nAusschaltsequenz: Dropbox API-Zugangsdaten nicht konfiguriert - Log-Export übersprungen.");
                return;
            }

            string filePrefix = DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_";
            string localTempDirectory = Path.Combine(Path.GetTempPath(), "JokiAutomationLogs");

            try
            {
                string appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                var localLogFiles = Directory.GetFiles(appDirectory, "*", SearchOption.TopDirectoryOnly)
                    .Where(file =>
                    {
                        string extension = Path.GetExtension(file);
                        string fileName = Path.GetFileName(file);
                        bool isLogExtension = extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) ||
                                              extension.Equals(".log", StringComparison.OrdinalIgnoreCase);
                        bool containsLogName = fileName.IndexOf("log", StringComparison.OrdinalIgnoreCase) >= 0;
                        return isLogExtension && containsLogName;
                    })
                    .ToList();

                Directory.CreateDirectory(localTempDirectory);
                int rasPiCount = 0;
                try
                {
                    rasPiCount = await _infraredControl.DownloadRaspberryPiLogsAsync(localTempDirectory);
                }
                catch (Exception ex)
                {
                    _logDat?.sendInfoMessage($"JokiAutomation\nAusschaltsequenz: RasPi-Logs konnten nicht heruntergeladen werden: {ex.Message}");
                }

                using (var httpClient = new HttpClient())
                {
                    var uploader = new DropboxApiUploader(httpClient, _dropboxAppKey, _dropboxAppSecret, _dropboxRefreshToken);
                    int localUploadCount = 0;
                    foreach (string file in localLogFiles)
                    {
                        string targetFileName = filePrefix + Path.GetFileName(file);
                        if (await uploader.UploadFileAsync(file, targetFileName))
                        {
                            localUploadCount++;
                        }
                    }

                    int rasPiUploadCount = 0;
                    foreach (string file in Directory.GetFiles(localTempDirectory, "*", SearchOption.TopDirectoryOnly))
                    {
                        string targetFileName = filePrefix + "RasPi_" + Path.GetFileName(file);
                        if (await uploader.UploadFileAsync(file, targetFileName))
                        {
                            rasPiUploadCount++;
                        }
                    }

                    _logDat?.sendInfoMessage($"JokiAutomation\nAusschaltsequenz: Log-Export zu Dropbox abgeschlossen (Lokal={localUploadCount}/{localLogFiles.Count}, RasPi={rasPiUploadCount}/{rasPiCount}).");

                    if (localUploadCount == localLogFiles.Count)
                    {
                        ClearLocalLogFiles(localLogFiles);
                    }
                    else
                    {
                        _logDat?.sendInfoMessage("JokiAutomation\nAusschaltsequenz: Lokale Logs werden nicht geleert, da nicht alle Dateien hochgeladen wurden.");
                    }

                    if (rasPiCount > 0 && rasPiUploadCount == rasPiCount)
                    {
                        try
                        {
                            int clearedCount = await _infraredControl.ClearRaspberryPiLogsAsync();
                            _logDat?.sendInfoMessage($"JokiAutomation\nAusschaltsequenz: RasPi-Logs geleert ({clearedCount}).");
                        }
                        catch (Exception ex)
                        {
                            _logDat?.sendInfoMessage($"JokiAutomation\nAusschaltsequenz: RasPi-Logs konnten nicht geleert werden: {ex.Message}");
                        }
                    }
                    else if (rasPiCount > 0)
                    {
                        _logDat?.sendInfoMessage("JokiAutomation\nAusschaltsequenz: RasPi-Logs werden nicht geleert, da nicht alle Dateien hochgeladen wurden.");
                    }
                }
            }
            catch (Exception ex)
            {
                _logDat?.sendInfoMessage($"JokiAutomation\nAusschaltsequenz: Fehler beim Log-Export nach DropBox: {ex.Message}");
            }
            finally
            {
                if (Directory.Exists(localTempDirectory))
                {
                    Directory.Delete(localTempDirectory, true);
                }
            }
        }

        /// <summary>
        /// Clears the local log files on the Windows PC after they have been
        /// successfully uploaded to Dropbox, so the next run only collects fresh data.
        /// </summary>
        private void ClearLocalLogFiles(List<string> localLogFiles)
        {
            foreach (string file in localLogFiles)
            {
                try
                {
                    File.WriteAllText(file, string.Empty);
                }
                catch (Exception ex)
                {
                    _logDat?.sendInfoMessage($"JokiAutomation\nAusschaltsequenz: Lokale Log-Datei '{Path.GetFileName(file)}' konnte nicht geleert werden: {ex.Message}");
                }
            }
        }

        private void StopRemoteControlApplication()
        {
            const string processName = "CanonRemoteControl";

            try
            {
                Process[] processes = Process.GetProcessesByName(processName);
                if (processes.Length == 0)
                {
                    _logDat?.sendInfoMessage("JokiAutomation\nRemote Control läuft nicht.");
                    return;
                }

                foreach (Process process in processes)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.CloseMainWindow();
                            if (!process.WaitForExit(3000))
                            {
                                process.Kill();
                                process.WaitForExit(3000);
                            }
                        }
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }

                _logDat?.sendInfoMessage("JokiAutomation\nRemote Control App beendet.");
            }
            catch (Exception ex)
            {
                _logDat?.sendInfoMessage($"JokiAutomation\nFehler beim Beenden von Remote Control: {ex.Message}");
            }
        }


        /// <summary>
        /// Interprets command line arguments and executes corresponding automation commands.
        /// </summary>
        /// <param name="commands">Command line arguments array</param>
        public async Task<int> CommandInterpreterAsync(string[] commands)
        {
            int exitCode = CommandExitCodes.CommandFailed;

            // Validate input BEFORE trying to acquire lock
            if (commands == null || commands.Length < 2)
            {
                _logDat?.sendInfoMessage("JokiAutomation\nKeine gültigen Kommandozeilenargumente übergeben.");
                LogData.LogLifecycle(ResolveLifecycleMode(), "COMMAND_INTERPRETER_ENTER", "Command=<invalid>");
                LogData.LogLifecycle(ResolveLifecycleMode(), "COMMAND_INTERPRETER_EXIT", $"Command=<invalid> ExitCode={CommandExitCodes.InvalidArguments}");
                return CommandExitCodes.InvalidArguments;
            }

            string cmd = commands[1];
            _currentCommand = cmd;
            string requestId = _activeCommandRequestId.Value;
            if (string.IsNullOrWhiteSpace(requestId))
            {
                requestId = Guid.NewGuid().ToString("N");
                _activeCommandRequestId.Value = requestId;
            }

            LogData.LogLifecycle(ResolveLifecycleMode(), "COMMAND_INTERPRETER_ENTER", $"RequestId={requestId} Command={cmd}");
            _logDat?.sendInfoMessage(
                $"JokiAutomation\n[COMMAND_INPUT] Command='{cmd}' Mode='{ResolveLifecycleMode()}' RequestId='{requestId}' PID={Process.GetCurrentProcess().Id} TID={Thread.CurrentThread.ManagedThreadId}");
            _logDat?.sendInfoMessage($"JokiAutomation\n>>> Kommando '{cmd}' wird ausgeführt...");

            LogData.LogLifecycle(
                ResolveLifecycleMode(),
                "OPERATION_LOCK_TRY",
                $"Operation=CommandInterpreter TID={Thread.CurrentThread.ManagedThreadId} Acquired=True CurrentCommand={_currentCommand}");

            try
            {
                // Commands requiring 4 parameters
                if ((cmd == "Pause" || cmd == "BEAMER_VideoClip") && commands.Length < 4)
                {
                    _logDat?.sendInfoMessage($"JokiAutomation\nKommando '{cmd}' benötigt 2 zusätzliche Parameter.");
                    exitCode = CommandExitCodes.InvalidArguments;
                    return exitCode;
                }

                if (cmd == "PositionControl" && commands.Length < 4)
                {
                    _logDat?.sendInfoMessage("JokiAutomation\nKommando 'PositionControl' benötigt 2 Parameter: Position und Profil.");
                    exitCode = CommandExitCodes.InvalidArguments;
                    return exitCode;
                }

                // Execute command
                switch (cmd)
                {
                    case "Initialisierung":
                        await EnsureStartupInitAsync();
                        break;
                    case "Pause":
                        
                        await DisablePictureInPictureAsync();
                        await SwitchATEMInputAsync(ATEMInput.Laptop);
                        await DisablePictureInPictureAsync();
                        await ExecutePauseCommandAsync(commands[2], commands[3]);
                        break;

                    case "Timer":
                        if (commands.Length < 3)
                        {
                            _logDat?.sendInfoMessage("JokiAutomation\nKommando 'Timer' benötigt einen Zeit-Parameter.");
                            exitCode = CommandExitCodes.InvalidArguments;
                            return exitCode;
                        }

                        await DisablePictureInPictureAsync();
                        await SwitchATEMInputAsync(ATEMInput.Laptop);
                        await DisablePictureInPictureAsync();
                        await ExecuteTimerCommandAsync(commands[2]);
                        break;

                    case "Band":
                       // _audioMix?._rasPi?.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_PPP_VIEW);
                        await DisablePictureInPictureAsync();
                        _logDat?.sendInfoMessage("JokiAutomation\n[VISUAL_PIPE] Diashow/PiP sofort ausblenden (Band)");

                        if (!await QueueAudioProfileByNameAsync("Band"))
                        {
                            _logDat?.sendInfoMessage(
                                "JokiAutomation\nKommando 'Band' abgebrochen: Tonprofil konnte nicht gesetzt werden.");

                            exitCode = CommandExitCodes.CommandFailed;
                            return exitCode;
                        }

                        _logDat?.sendInfoMessage("JokiAutomation\n[VISUAL_PIPE] Starte Bild/Kamera-Kommandos parallel zum Audio-Fade (Band)");
                        _logDat?.sendInfoMessage("JokiAutomation\n[PIPE_PARALLEL] VisualStart RequestId='" + _activeCommandRequestId.Value + "' Command='Band'");
                        await SwitchATEMInputAsync(ATEMInput.Laptop);
                        await DisablePictureInPictureAsync();
                        _logDat?.sendInfoMessage("JokiAutomation\n[PIPE_PARALLEL] VisualCompleted RequestId='" + _activeCommandRequestId.Value + "' Command='Band'");
                        break;

                    case "Text":
                        //_audioMix?._rasPi?.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_TEXT_VIEW);
                        await DisablePictureInPictureAsync();
                        _logDat?.sendInfoMessage("JokiAutomation\n[VISUAL_PIPE] Diashow/PiP sofort ausblenden (Text)");

                        if (!await QueueAudioProfileByNameAsync("Text"))
                        {
                            _logDat?.sendInfoMessage(
                                "JokiAutomation\nKommando 'Text' abgebrochen: Tonprofil konnte nicht gesetzt werden.");

                            exitCode = CommandExitCodes.CommandFailed;
                            return exitCode;
                        }

                        _logDat?.sendInfoMessage("JokiAutomation\n[VISUAL_PIPE] Starte Bild/Kamera-Kommandos parallel zum Audio-Fade (Text)");
                        _logDat?.sendInfoMessage("JokiAutomation\n[PIPE_PARALLEL] VisualStart RequestId='" + _activeCommandRequestId.Value + "' Command='Text'");
                        await SwitchATEMInputAsync(ATEMInput.Laptop);
                        await DisablePictureInPictureAsync();
                        _logDat?.sendInfoMessage("JokiAutomation\n[PIPE_PARALLEL] VisualCompleted RequestId='" + _activeCommandRequestId.Value + "' Command='Text'");
                        break;

                    case "GoPro":
                        //_audioMix?._rasPi?.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_GOPRO_VIEW);
                        await DisablePictureInPictureAsync();
                        _logDat?.sendInfoMessage("JokiAutomation\n[VISUAL_PIPE] Diashow/PiP sofort ausblenden (GoPro)");

                        if (!await QueueAudioProfileByNameAsync("Gottesdienst"))
                        {
                            _logDat?.sendInfoMessage(
                                "JokiAutomation\nKommando 'GoPro' abgebrochen: Tonprofil konnte nicht gesetzt werden.");

                            exitCode = CommandExitCodes.CommandFailed;
                            return exitCode;
                        }

                        _logDat?.sendInfoMessage("JokiAutomation\n[PIPE_PARALLEL] VisualStart RequestId='" + _activeCommandRequestId.Value + "' Command='GoPro'");
                        await SwitchATEMInputAsync(ATEMInput.GoPro);
                        await DisablePictureInPictureAsync();
                        _logDat?.sendInfoMessage("JokiAutomation\n[PIPE_PARALLEL] VisualCompleted RequestId='" + _activeCommandRequestId.Value + "' Command='GoPro'");
                        break;

                    case "Altar":
                        _logDat?.sendInfoMessage("JokiAutomation\nFühre 'Altar' Kommando aus...");
                        await DisablePictureInPictureAsync();
                        _logDat?.sendInfoMessage("JokiAutomation\n[VISUAL_PIPE] Diashow/PiP sofort ausblenden (Altar)");

                        if (!await QueueAudioProfileByNameAsync("Gottesdienst"))
                        {
                            _logDat?.sendInfoMessage(
                                "JokiAutomation\nKommando 'Altar' abgebrochen: Tonprofil konnte nicht gesetzt werden.");

                            exitCode = CommandExitCodes.CommandFailed;
                            return exitCode;
                        }

                        _logDat?.sendInfoMessage("JokiAutomation\n[PIPE_PARALLEL] VisualStart RequestId='" + _activeCommandRequestId.Value + "' Command='Altar'");
                        await SwitchATEMInputAsync(ATEMInput.CanonPtzMain);
                        await DisablePictureInPictureAsync();
                        _logDat?.sendInfoMessage("JokiAutomation\n[PIPE_PARALLEL] VisualCompleted RequestId='" + _activeCommandRequestId.Value + "' Command='Altar' Stage='ATEMSwitch'");
                        // Kamera-Positionierung abhängig vom Modus:
                        // PTZ_CAM=true: Canon PTZ bewegt Kamera | PTZ_CAM=false: RasPi Motor bewegt Kamera
                        // IR-Sequenz wird IMMER über RasPi ausgeführt (Beamer, andere Geräte)
                        if (_isPtzCameraMode && _canonPtzController != null)
                        {
                            _logDat?.sendInfoMessage("JokiAutomation\nPTZ-Modus: Rufe ExecuteCanonSceneAsync auf...");
                            await ExecuteCanonSceneAsync("Altar", 1, ATEMInput.CanonPtzMain);
                            _logDat?.sendInfoMessage("JokiAutomation\nExecuteCanonSceneAsync abgeschlossen");
                        }
                        break;

                    case "Predigt":
                        _logDat?.sendInfoMessage("JokiAutomation\nFühre 'Predigt' Kommando aus...");
                        await DisablePictureInPictureAsync();
                        _logDat?.sendInfoMessage("JokiAutomation\n[VISUAL_PIPE] Diashow/PiP sofort ausblenden (Predigt)");

                        if (!await QueueAudioProfileByNameAsync("Predigt"))
                        {
                            _logDat?.sendInfoMessage(
                                "JokiAutomation\nKommando 'Predigt' abgebrochen: Tonprofil konnte nicht gesetzt werden.");

                            exitCode = CommandExitCodes.CommandFailed;
                            return exitCode;
                        }

                        _logDat?.sendInfoMessage("JokiAutomation\n[VISUAL_PIPE] Starte Bild/Kamera-Kommandos parallel zum Audio-Fade (Predigt)");
                        _logDat?.sendInfoMessage("JokiAutomation\n[PIPE_PARALLEL] VisualStart RequestId='" + _activeCommandRequestId.Value + "' Command='Predigt'");
                        await SwitchATEMInputAsync(ATEMInput.CanonPtzPreacher);
                        // Ensure final state stays PiP-off after source switch.
                        await DisablePictureInPictureAsync();
                        _logDat?.sendInfoMessage("JokiAutomation\n[PIPE_PARALLEL] VisualCompleted RequestId='" + _activeCommandRequestId.Value + "' Command='Predigt'");
                       /* if (_isPtzCameraMode && _canonPtzController != null)
                        {
                            _logDat?.sendInfoMessage("JokiAutomation\nPTZ-Modus: Rufe ExecuteCanonSceneAsync auf...");
                            await ExecuteCanonSceneAsync("Predigt", 3, ATEMInput.CanonPtzPreacher);
                            _logDat?.sendInfoMessage("JokiAutomation\nExecuteCanonSceneAsync abgeschlossen");
                        }*/
                        break;

                    case "Gebet":
                       DisablePictureInPicture();
                       _logDat?.sendInfoMessage("JokiAutomation\n[VISUAL_PIPE] Diashow/PiP sofort ausblenden (Gebet)");

                       if (!await QueueAudioProfileByNameAsync("Gottesdienst"))
                       {
                           _logDat?.sendInfoMessage(
                               "JokiAutomation\nKommando 'Gebet' abgebrochen: Tonprofil konnte nicht gesetzt werden.");

                           exitCode = CommandExitCodes.CommandFailed;
                           return exitCode;
                       }

                       ATEMInput gebetPiPInput = ResolveMultiViewPiPInput(commands, "Gebet");
                       _logDat?.sendInfoMessage("JokiAutomation\n[VISUAL_PIPE] Starte Bild/Kamera-Kommandos parallel zum Audio-Fade (Gebet)");
                       _logDat?.sendInfoMessage("JokiAutomation\n[PIPE_PARALLEL] VisualStart RequestId='" + _activeCommandRequestId.Value + "' Command='Gebet'");
                       await SwitchATEMInputAsync(ATEMInput.Laptop);
                       await Task.Delay(1000); // Kurze Verzögerung, um sicherzustellen, dass der ATEM die Eingangsquelle gewechselt hat
                       EnablePictureInPicture(gebetPiPInput);
                       _logDat?.sendInfoMessage("JokiAutomation\n[PIPE_PARALLEL] VisualCompleted RequestId='" + _activeCommandRequestId.Value + "' Command='Gebet'");
                       break;

                    case "LesungMulti":
                        DisablePictureInPicture();
                        _logDat?.sendInfoMessage("JokiAutomation\n[VISUAL_PIPE] Diashow/PiP sofort ausblenden (LesungMulti)");

                        if (!await QueueAudioProfileByNameAsync("Predigt"))
                        {
                            _logDat?.sendInfoMessage(
                                "JokiAutomation\nKommando 'LesungMulti' abgebrochen: Tonprofil konnte nicht gesetzt werden.");

                            exitCode = CommandExitCodes.CommandFailed;
                            return exitCode;
                        }

                        ATEMInput lesungPiPInput = ResolveMultiViewPiPInput(commands, "LesungMulti");
                        _logDat?.sendInfoMessage("JokiAutomation\n[VISUAL_PIPE] Starte Bild/Kamera-Kommandos parallel zum Audio-Fade (LesungMulti)");
                        _logDat?.sendInfoMessage("JokiAutomation\n[PIPE_PARALLEL] VisualStart RequestId='" + _activeCommandRequestId.Value + "' Command='LesungMulti'");
                        await SwitchATEMInputAsync(ATEMInput.Laptop);
                        await Task.Delay(1000); // Kurze Verzögerung, um sicherzustellen, dass der ATEM die Eingangsquelle gewechselt hat
                        EnablePictureInPicture(lesungPiPInput);
                        _logDat?.sendInfoMessage("JokiAutomation\n[PIPE_PARALLEL] VisualCompleted RequestId='" + _activeCommandRequestId.Value + "' Command='LesungMulti'");
                        break;

                    case "BandMulti":
                        DisablePictureInPicture();
                        _logDat?.sendInfoMessage("JokiAutomation\n[VISUAL_PIPE] Diashow/PiP sofort ausblenden (BandMulti)");

                        if (!await QueueAudioProfileByNameAsync("Band"))
                        {
                            _logDat?.sendInfoMessage(
                                "JokiAutomation\nKommando 'BandMulti' abgebrochen: Tonprofil konnte nicht gesetzt werden.");

                            exitCode = CommandExitCodes.CommandFailed;
                            return exitCode;
                        }

                        ATEMInput bandPiPInput = ResolveMultiViewPiPInput(commands, "BandMulti");
                        _logDat?.sendInfoMessage("JokiAutomation\n[VISUAL_PIPE] Starte Bild/Kamera-Kommandos parallel zum Audio-Fade (BandMulti)");
                        _logDat?.sendInfoMessage("JokiAutomation\n[PIPE_PARALLEL] VisualStart RequestId='" + _activeCommandRequestId.Value + "' Command='BandMulti'");
                        await SwitchATEMInputAsync(ATEMInput.Laptop);
                        await Task.Delay(1000); // Kurze Verzögerung, um sicherzustellen, dass der ATEM die Eingangsquelle gewechselt hat
                        EnablePictureInPicture(bandPiPInput);
                        _logDat?.sendInfoMessage("JokiAutomation\n[PIPE_PARALLEL] VisualCompleted RequestId='" + _activeCommandRequestId.Value + "' Command='BandMulti'");
                        break;

                    case "BEAMER_LiveVideo":
                        await ExecuteIrSequenceAsync(
                            InfraredControl.IR_LIVE_VIDEO,
                            "BEAMER_LiveVideo",
                            TimeSpan.FromSeconds(20));
                        await DisablePictureInPictureAsync();
                        await SwitchATEMInputAsync(ATEMInput.Laptop);
                        await DisablePictureInPictureAsync();
                        break;

                    case "BEAMER_LiveStream":
                        await ExecuteIrSequenceAsync(
                            InfraredControl.IR_BEAMER_TOGGLE,
                            "BEAMER_LiveStream",
                            TimeSpan.FromSeconds(20));
                        break;

                    case "BEAMER_VideoClip":
                        await ExecuteIrSequenceAsync(
                            InfraredControl.IR_BEAMER_ANALOG,
                            "BEAMER_VideoClip",
                            TimeSpan.FromSeconds(20));
                        await SwitchATEMInputAsync(ATEMInput.Laptop);
                        await DisablePictureInPictureAsync();
                        await ExecutePauseCommandAsync(commands[2], commands[3]);
                        break;

                    case "BEAMER_Mute":
                        await ExecuteIrSequenceAsync(
                            InfraredControl.IR_BEAMER_MUTE,
                            "BEAMER_Mute",
                            TimeSpan.FromSeconds(20));
                        break;

                    case "BEAMER_ON":
                        await ExecuteIrSequenceAsync(
                            InfraredControl.IR_BEAMER_ON,
                            "BEAMER_ON",
                            TimeSpan.FromSeconds(20));
                        break;

                    case "Backup_Start":
                        await ExecuteIrSequenceAsync(
                            InfraredControl.IR_START_BACKUP,
                            "Backup_Start",
                            TimeSpan.FromSeconds(20));
                        break;

                    case "Backup_Stop":
                        await ExecuteIrSequenceAsync(
                            InfraredControl.IR_STOP_BACKUP,
                            "Backup_Stop",
                            TimeSpan.FromSeconds(20));
                        break;

                    case "Backup_Switch":
                        await ExecuteIrSequenceAsync(
                            InfraredControl.IR_SWITCH_BACKUP,
                            "Backup_Switch",
                            TimeSpan.FromSeconds(20));
                        break;

                    case "Ausschaltsequenz":
                        _logDat?.sendInfoMessage("JokiAutomation\n");
                        _logDat?.sendInfoMessage("JokiAutomation\n╔═══════════════════════════════════════════════════╗");
                        _logDat?.sendInfoMessage("JokiAutomation\n║     AUSSCHALTSEQUENZ GESTARTET                   ║");
                        _logDat?.sendInfoMessage("JokiAutomation\n╚═══════════════════════════════════════════════════╝");
                        _logDat?.sendInfoMessage("JokiAutomation\n");

                        // Step 1: Export all logs to DropBox (local + RasPi)
                        _logDat?.sendInfoMessage("JokiAutomation\n[1/5] Logfiles nach DropBox exportieren...");
                        await ExportLogsToDropBoxAsync();

                        // Step 2: PTZ Camera shutdown (if in PTZ mode)
                        if (_isPtzCameraMode)
                        {
                            _logDat?.sendInfoMessage("JokiAutomation\n[2/5] Canon PTZ Kamera herunterfahren...");
                            await ShutdownPtzCameraAsync();
                        }
                        else
                        {
                            _logDat?.sendInfoMessage("JokiAutomation\n[2/5] Kein PTZ-Modus - Kamera-Shutdown übersprungen");
                        }

                        _logDat?.sendInfoMessage("JokiAutomation\n");

                        // Step 3: Rakuten TV power off
                        _logDat?.sendInfoMessage("JokiAutomation\n[3/5] Rakuten TV ausschalten...");
                        await Task.Run(() => PowerOffRakutenTv());

                        // Step 4: Stop Canon Remote Control app
                        _logDat?.sendInfoMessage("JokiAutomation\n[4/5] Remote Control App beenden...");
                        await Task.Run(() => StopRemoteControlApplication());

                        _logDat?.sendInfoMessage("JokiAutomation\n");

                        // Step 5: RasPi IR shutdown sequence (Beamer, etc.)
                        _logDat?.sendInfoMessage("JokiAutomation\n[5/6] RasPi IR-Shutdown-Sequenz starten...");
                        try
                        {
                            await ExecuteIrSequenceAsync(
                                InfraredControl.IR_SHUTDOWN,
                                "Ausschaltsequenz.IR_SHUTDOWN",
                                TimeSpan.FromSeconds(30));
                            _logDat?.sendInfoMessage("JokiAutomation\n[5/6] RasPi IR-Shutdown-Sequenz vollständig abgeschlossen.");
                        }
                        catch (Exception ex)
                        {
                            _logDat?.sendInfoMessage($"JokiAutomation\n[5/6] RasPi IR-Shutdown-Sequenz fehlgeschlagen: {ex.GetType().Name}: {ex.Message}");
                            if (!ShutdownRasPiWhenIrShutdownFails)
                            {
                                throw;
                            }
                        }

                        // Step 6: Shutdown Raspberry Pi operating system at the very end
                        _logDat?.sendInfoMessage("JokiAutomation\n[6/6] Raspberry Pi Betriebssystem herunterfahren...");
                        bool rasPiShutdownTriggered = _audioMix?._rasPi != null &&
                            await _audioMix._rasPi.ShutdownRaspberryPiAsync(TimeSpan.FromSeconds(20));
                        _logDat?.sendInfoMessage(rasPiShutdownTriggered
                            ? "JokiAutomation\nRaspberry Pi Shutdown-Befehl erfolgreich gesendet."
                            : "JokiAutomation\nRaspberry Pi Shutdown-Befehl konnte nicht bestätigt werden.");

                        _logDat?.sendInfoMessage("JokiAutomation\n");
                        _logDat?.sendInfoMessage("JokiAutomation\n╔═══════════════════════════════════════════════════╗");
                        _logDat?.sendInfoMessage("JokiAutomation\n║     AUSSCHALTSEQUENZ ABGESCHLOSSEN               ║");
                        _logDat?.sendInfoMessage("JokiAutomation\n╚═══════════════════════════════════════════════════╝");
                        break;

                    case "RasPi_Reset":
                        if (_audioMix?._rasPi == null)
                        {
                            throw new InvalidOperationException("Raspberry Pi ist für RasPi_Reset nicht verfügbar.");
                        }

                        await _audioMix._rasPi.RasPiResetAsync(TimeSpan.FromSeconds(30));
                        break;

                    case "PositionControl":
                        if (commands.Length >= 4 && !string.IsNullOrEmpty(commands[3]))
                        {
                            PositionControlProfileMapping profileMapping;
                            try
                            {
                                profileMapping = PositionControlProfileMapper.MapOrThrow(commands[3]);
                            }
                            catch (ArgumentException ex)
                            {
                                _logDat?.sendInfoMessage($"JokiAutomation\n{ex.Message}");
                                exitCode = CommandExitCodes.InvalidArguments;
                                return exitCode;
                            }

                            await DisablePictureInPictureAsync();

                            _logDat?.sendInfoMessage(
                                $"JokiAutomation\nPositionControl: Position={commands[2]}, Profil={commands[3]}, Modus={profileMapping.CameraCode}");

                            // The first profile character selects the cover image during PTZ movement.
                            // After a successful PTZ move, the sequence always returns to Input 3.
                            ATEMInput movementInput;
                            ATEMInput finalInput = ATEMInput.CanonPtzMain;
                            switch (profileMapping.CameraCode)
                            {
                                case 'G': movementInput = ATEMInput.GoPro;            break;
                                case 'K': movementInput = ATEMInput.CanonPtzPreacher; break;
                                case 'L': movementInput = ATEMInput.Laptop;           break;
                                case 'A': movementInput = ATEMInput.CanonPtzMain;     break;
                                default:
                                    throw new InvalidOperationException($"Nicht unterstützter PositionControl-Kameracode: {profileMapping.CameraCode}");
                            }

                            // Resolve ListBox position index
                            int seqPositionId = -1;
                            for (int i = 0; i < listBoxCamPosControl.Items.Count; i++)
                            {
                                if (commands[2] == listBoxCamPosControl.Items[i].ToString())
                                {
                                    seqPositionId = i;
                                    break;
                                }
                            }

                            if (seqPositionId < 0)
                            {
                                _logDat?.sendInfoMessage(
                                    $"JokiAutomation\nPositionControl: Position '{commands[2]}' nicht in ListBox gefunden");
                                break;
                            }

                            if (IsActivePtzPositionControlMode())
                            {
                                await ExecutePositionControlPtzAsync(commands[2], commands[3]);
                            }
                            else
                            {
                                // ── Legacy RasPi path ──────────────────────────
                                // Switch ATEM before movement (legacy behaviour preserved)
                                await SwitchATEMInputAsync(movementInput);
                                DisablePictureInPicture();

                                int maxWaitMs    = 80000;
                                int waitInterval = 100;
                                int elapsedMs    = 0;

                                // Nur Position fahren. Die Bildumschaltung wird hier zentral gesteuert,
                                // dadurch kein nachträgliches Zurückschalten auf K/G durch RasPi-Sequenz.
                                _positionControl?.moveToPos(seqPositionId);
                                while (_positionControl != null && _positionControl.IsMoving() && elapsedMs < maxWaitMs)
                                {
                                    Thread.Sleep(waitInterval);
                                    elapsedMs += waitInterval;
                                    Application.DoEvents();
                                }

                                if (elapsedMs >= maxWaitMs)
                                    _logDat?.sendInfoMessage("JokiAutomation\nWARNUNG: Position Control Timeout nach 80s");
                                else
                                    _logDat?.sendInfoMessage($"JokiAutomation\nPosition Control abgeschlossen nach {elapsedMs}ms");

                                // Nach der Positionierung auf die Ziel-PTZ-Kamera zurückschalten,
                                // nicht erneut auf die Ausweichkamera.
                                await SwitchATEMInputAsync(finalInput);
                                DisablePictureInPicture();
                            }
                        }
                        else
                        {
                            _logDat?.sendInfoMessage("JokiAutomation\nKeine Profil-Information vorhanden");
                            DisablePictureInPicture();
                        }
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

                            if (_rokuTV == null)
                            {
                                InitializeRokuTV();
                            }

                            if (_rokuTV != null)
                            {
                                bool powerOnOk = _rokuTV.PowerOn();
                                if (powerOnOk)
                                {
                                    Thread.Sleep(3000);
                                    bool hdmi2Ok = _rokuTV.SwitchToHDMI2();
                                    _logDat?.sendInfoMessage(hdmi2Ok
                                        ? "JokiAutomation\nRoku TV eingeschaltet und auf HDMI2 geschaltet."
                                        : "JokiAutomation\nRoku TV eingeschaltet, HDMI2 konnte nicht aktiviert werden.");
                                }
                                else
                                {
                                    _logDat?.sendInfoMessage("JokiAutomation\nRoku TV PowerOn fehlgeschlagen (false). Prüfe Roku_TV in Network.cfg (IP/Port). ");
                                }
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

                // Audio for this command (if any) was dispatched in parallel to the
                // visual/PTZ/ATEM work above. Await its completion now, at the actual CLI
                // lifecycle end, so that success is only reported once the audio request
                // has actually finished; failures/exceptions propagate into the command
                // result via the catch block below.
                await AwaitPendingAudioCompletionAsync();

                _logDat?.sendInfoMessage($"JokiAutomation\n✓ Kommando '{cmd}' erfolgreich abgeschlossen");
                exitCode = CommandExitCodes.Success;
                return exitCode;
            }
            catch (Exception ex)
            {
                _logDat?.sendInfoMessage($"JokiAutomation\nFehler beim Ausführen des Kommandos '{cmd}':\n{ex.Message}");
                _logDat?.sendInfoMessage($"JokiAutomation\nStackTrace:\n{ex.StackTrace}");
                exitCode = CommandExitCodes.CommandFailed;
                return exitCode;
            }
            finally
            {
                LogData.LogLifecycle(
                    ResolveLifecycleMode(),
                    "OPERATION_LOCK_RELEASE",
                    $"Operation=CommandInterpreter TID={Thread.CurrentThread.ManagedThreadId} CurrentCommand={_currentCommand}");
                _logDat?.sendInfoMessage($"JokiAutomation\n[COMMAND_RESULT] RequestId='{_activeCommandRequestId.Value}' Command='{cmd}' ExitCode={exitCode}");
                LogData.LogLifecycle(ResolveLifecycleMode(), "COMMAND_INTERPRETER_EXIT", $"RequestId={_activeCommandRequestId.Value} Command={cmd} ExitCode={exitCode}");
                _logDat?.sendInfoMessage($"JokiAutomation\n<<< Kommando '{cmd}' beendet\n");
                _currentCommand = string.Empty;
            }
        }

        /// <summary>
        /// Executes a sequence-critical IR command and awaits completion.
        /// </summary>
        private async Task ExecuteIrSequenceAsync(
            int irSequenceId,
            string operationName,
            TimeSpan? timeout = null,
            CancellationToken cancellationToken = default)
        {
            if (_audioMix?._rasPi == null)
            {
                throw new InvalidOperationException(
                    $"Raspberry Pi ist für IR-Aktion '{operationName}' nicht verfügbar.");
            }

            TimeSpan effectiveTimeout = timeout ?? TimeSpan.FromSeconds(20);
            string requestId = _activeCommandRequestId.Value ?? string.Empty;

            _logDat?.sendInfoMessage(
                $"JokiAutomation\n[IR_BEGIN] Operation={operationName} Command={InfraredControl.IR_SEQUENCE} ID={irSequenceId} RequestId={requestId} PID={Process.GetCurrentProcess().Id} TID={Thread.CurrentThread.ManagedThreadId}");

            Stopwatch stopwatch = Stopwatch.StartNew();

            try
            {
                await _audioMix._rasPi.RasPiExecuteAsync(
                    InfraredControl.IR_SEQUENCE,
                    irSequenceId,
                    effectiveTimeout,
                    cancellationToken);

                _logDat?.sendInfoMessage(
                    $"JokiAutomation\n[IR_END] Operation={operationName} Command={InfraredControl.IR_SEQUENCE} ID={irSequenceId} Result=Success DurationMs={stopwatch.ElapsedMilliseconds} RequestId={requestId}");
            }
            catch (OperationCanceledException)
            {
                _logDat?.sendInfoMessage(
                    $"JokiAutomation\n[IR_FAIL] Operation={operationName} Command={InfraredControl.IR_SEQUENCE} ID={irSequenceId} ErrorType=OperationCanceledException DurationMs={stopwatch.ElapsedMilliseconds} RequestId={requestId}");
                throw;
            }
            catch (Exception ex)
            {
                _logDat?.sendInfoMessage(
                    $"JokiAutomation\n[IR_FAIL] Operation={operationName} Command={InfraredControl.IR_SEQUENCE} ID={irSequenceId} ErrorType={ex.GetType().Name} Error={ex.Message} DurationMs={stopwatch.ElapsedMilliseconds} RequestId={requestId}");
                throw;
            }
            finally
            {
                stopwatch.Stop();
            }
        }

        private async Task ExecutePauseCommandAsync(string text1, string text2)
        {
            if (string.IsNullOrWhiteSpace(text1) || string.IsNullOrWhiteSpace(text2))
            {
                _logDat?.sendInfoMessage("JokiAutomation\nPause-Texte dürfen nicht leer sein.");
                return;
            }

            Task raspiTask = null;
            if (_audioMix?._rasPi == null)
            {
                _logDat?.sendInfoMessage("JokiAutomation\nRasPi nicht verfügbar.");
            }
            else
            {
                LogData.LogLifecycle(ResolveLifecycleMode(), "PAUSE_AUDIO_BEGIN", "Command=Pause IR=IR_PAUSE");
                raspiTask = _audioMix._rasPi.RasPiExecuteAsync(InfraredControl.IR_SEQUENCE, InfraredControl.IR_PAUSE, TimeSpan.FromSeconds(15));
            }

            if (raspiTask != null)
            {
                try
                {
                    await raspiTask;
                    LogData.LogLifecycle(ResolveLifecycleMode(), "PAUSE_AUDIO_END", "Command=Pause IR=IR_PAUSE Result=Success");
                }
                catch (Exception ex)
                {
                    _logDat?.sendInfoMessage($"JokiAutomation\nWarnung: RasPi Befehl konnte nicht abgeschlossen werden ({ex.Message})");
                    LogData.LogLifecycle(ResolveLifecycleMode(), "PAUSE_AUDIO_END", $"Command=Pause IR=IR_PAUSE Result=Failed Exception={ex.GetType().Name} Message={ex.Message}");
                }
            }

            LogData.LogLifecycle(ResolveLifecycleMode(), "EVENTTIMER_START_AFTER_AUDIO", "Command=Pause");
            string requestId = Guid.NewGuid().ToString("N");
            EventTimerStartResult pauseStartResult = _eventTimer?.sendPause(text1, text2, requestId, ResolveLifecycleMode());
            if (pauseStartResult != null && pauseStartResult.AlreadyRunning)
            {
                _logDat?.sendInfoMessage("JokiAutomation\nEventTimer läuft bereits - zweiter Start wurde abgelehnt.");
            }
        }

        private async Task WaitForPendingRasPiCommandAsync(int timeoutMs)
        {
            var runningThread = _audioMix?._rasPi?._RasPiThread;
            if (runningThread == null || !runningThread.IsAlive)
            {
                return;
            }

            await Task.Run(() => runningThread.Join(timeoutMs));
        }

        /// <summary>
        /// Executes the Timer command with event time parameter.
        /// </summary>
        private async Task ExecuteTimerCommandAsync(string eventTime)
        {
            if (string.IsNullOrWhiteSpace(eventTime))
            {
                _logDat?.sendInfoMessage("JokiAutomation\nEvent-Zeit darf nicht leer sein.");
                return;
            }

            if (_audioMix?._rasPi == null)
            {
                _logDat?.sendInfoMessage("JokiAutomation\nRasPi nicht verfügbar.");
                return;
            }

            LogData.LogLifecycle(ResolveLifecycleMode(), "PAUSE_AUDIO_BEGIN", "Command=Timer IR=IR_TIMER");
            Task raspiTask = _audioMix._rasPi.RasPiExecuteAsync(InfraredControl.IR_SEQUENCE, InfraredControl.IR_TIMER, TimeSpan.FromSeconds(15));
            try
            {
                await raspiTask;
                LogData.LogLifecycle(ResolveLifecycleMode(), "PAUSE_AUDIO_END", "Command=Timer IR=IR_TIMER Result=Success");
            }
            catch (Exception ex)
            {
                _logDat?.sendInfoMessage($"JokiAutomation\nWarnung: RasPi Befehl konnte nicht abgeschlossen werden ({ex.Message})");
                LogData.LogLifecycle(ResolveLifecycleMode(), "PAUSE_AUDIO_END", $"Command=Timer IR=IR_TIMER Result=Failed Exception={ex.GetType().Name} Message={ex.Message}");
            }

            LogData.LogLifecycle(ResolveLifecycleMode(), "EVENTTIMER_START_AFTER_AUDIO", "Command=Timer");
            string requestId = Guid.NewGuid().ToString("N");
            EventTimerStartResult timerStartResult = _eventTimer?.sendEventTime(eventTime, requestId, ResolveLifecycleMode());
            if (timerStartResult != null && timerStartResult.AlreadyRunning)
            {
                _logDat?.sendInfoMessage("JokiAutomation\nEventTimer läuft bereits - zweiter Start wurde abgelehnt.");
            }
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
        private async void button1_Click(object sender, EventArgs e)
        {
            bool eventTimerStarted = false;

            try
            {
                await SwitchATEMInputAsync(ATEMInput.Laptop);
                if (this.listBox1.Text == "Pause")
                {
                    if (_audioMix?._rasPi != null)
                    {
                        try
                        {
                            await _audioMix._rasPi.RasPiExecuteAsync(InfraredControl.IR_SEQUENCE, InfraredControl.IR_PAUSE, TimeSpan.FromSeconds(15));
                        }
                        catch (Exception ex)
                        {
                            _logDat?.sendInfoMessage($"JokiAutomation\nWarnung: RasPi Pause-Audio konnte nicht gesetzt werden ({ex.Message})");
                        }
                    }
                    string requestId = Guid.NewGuid().ToString("N");
                    EventTimerStartResult startResult = _eventTimer?.sendPause($"\"{textBox1.Text}\"", $"\"{textBox2.Text}\"", requestId, "GUI");
                    eventTimerStarted = startResult != null && (startResult.Started || startResult.AlreadyRunning);
                }
                else
                {
                    string eventTime = $"{dateTimePicker1.Value.TimeOfDay.Hours:00}:{dateTimePicker1.Value.TimeOfDay.Minutes:00}";
                    if (_audioMix?._rasPi != null)
                    {
                        try
                        {
                            await _audioMix._rasPi.RasPiExecuteAsync(InfraredControl.IR_SEQUENCE, InfraredControl.IR_TIMER, TimeSpan.FromSeconds(15));
                        }
                        catch (Exception ex)
                        {
                            _logDat?.sendInfoMessage($"JokiAutomation\nWarnung: RasPi Timer-Audio konnte nicht gesetzt werden ({ex.Message})");
                        }
                    }

                    string requestId = Guid.NewGuid().ToString("N");
                    EventTimerStartResult startResult = _eventTimer?.sendEventTime(eventTime, requestId, "GUI");
                    eventTimerStarted = startResult != null && (startResult.Started || startResult.AlreadyRunning);
                }

                if (eventTimerStarted)
                {
                    LogData.LogLifecycle(ResolveLifecycleMode(), "EVENTTIMER_START_CONFIRMED");
                    bool shutdownStarted = Program.BeginShutdown();
                    LogData.LogLifecycle(ResolveLifecycleMode(), "SHUTDOWN_REQUEST_AFTER_EVENTTIMER_START", $"Started={shutdownStarted}");
                    Application.Exit();
                    LogData.LogLifecycle(ResolveLifecycleMode(), "APPLICATION_EXIT_REQUESTED_AFTER_EVENTTIMER_START");
                }
            }
            catch (Exception ex)
            {
                _logDat?.sendInfoMessage($"JokiAutomation\nFehler beim Starten von Pause/Timer: {ex.Message}");
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
        private async void button7_Click(object sender, EventArgs e)
        {
            int selectedIndex = listBox3.SelectedIndex;

            if (selectedIndex < 0 || selectedIndex >= _audioMix.channelActive_.Length)
                return;

            if (_audioMix.channelActive_[selectedIndex]) // ✅ Kein == true
            {
                int commandID = AudioMix.AM_FADEDOWN + (1 << selectedIndex);
                if (_audioMix != null)
                {
                    await _audioMix.ExecuteAudioAsync(commandID);
                }
            }
        }
        // buttonhandler fade up >>>  AudioControl
        private async void button8_Click(object sender, EventArgs e)
        {
            int selectedIndex = listBox3.SelectedIndex;

            if (selectedIndex < 0 || selectedIndex >= _audioMix.channelActive_.Length)
                return;

            if (_audioMix.channelActive_[selectedIndex]) // ✅ Kein == true
            {
                int commandID = AudioMix.AM_FADEUP + (1 << selectedIndex);
                if (_audioMix != null)
                {
                    await _audioMix.ExecuteAudioAsync(commandID);
                }
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
        private async void button9_Click(object sender, EventArgs e)
        {
            try
            {
                if (!await ApplyAudioProfileByIndexAsync(listBox4?.SelectedIndex ?? -1))
                {
                    _logDat?.sendInfoMessage("JokiAutomation\nAudio Start abgebrochen: ungültige Profilauswahl.");
                }
            }
            catch (Exception ex)
            {
                _logDat?.sendInfoMessage($"JokiAutomation\nAudio Start fehlgeschlagen: {ex.Message}");
            }
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
        private async void button13_Click(object sender, EventArgs e)
        {
            await ExecuteRasPiResetAsync();
        }

        private async void button14_Click(object sender, EventArgs e)
        {
            await ExecuteRasPiResetAsync();
        }

        private async void button15_Click(object sender, EventArgs e)
        {
            await ExecuteRasPiResetAsync();
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
        private async void resetCamPos_Click(object sender, EventArgs e)
        {
            _requestedFunction = 0;
            await ExecuteRasPiResetAsync();
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
        private async void buttonAZReset_Click(object sender, EventArgs e)
        {
            await ExecuteRasPiResetAsync();
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
        private readonly SemaphoreSlim _startupInitSemaphore = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _visualCommandSemaphore = new SemaphoreSlim(1, 1);
        private readonly object _audioPipelineSync = new object();
        private Task _audioPipelineTail = Task.CompletedTask;
        private volatile Task _pendingAudioCompletionTask;
        private volatile bool _startupInitCompleted;
        private string _User = "DefaultUser"; // set user
        private string _requestedUser = "DefaultUser"; // requestet user
        private static uint _requestedFunction = 0;
        private System.Windows.Forms.Timer _Inputtimer = new System.Windows.Forms.Timer(); // input sequence of password 
        private uint _InputTimerloop = 0;
        public LogData _logDat = new LogData();
        private bool CI_test_active_ = false;

        // ── UI-Thread und SynchronizationContext ──────────────────────────────
        private readonly SynchronizationContext _uiContext;
        private readonly int _uiThreadId;

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
        private async void ResetRaspberryPi_Click(object sender, EventArgs e)
        {
            await ExecuteRasPiResetAsync();
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

        private async Task ExecuteRasPiResetAsync()
        {
            if (_audioMix?._rasPi == null)
            {
                throw new InvalidOperationException("Raspberry Pi ist für den Reset nicht verfügbar.");
            }

            try
            {
                await _audioMix._rasPi.RasPiResetAsync(TimeSpan.FromSeconds(30));
            }
            catch (Exception ex)
            {
                _logDat?.sendInfoMessage($"JokiAutomation\nRasPi Reset fehlgeschlagen: {ex.Message}");
                throw;
            }
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
private async void button5_Click(object sender, EventArgs e)
{
    int commandID = AudioMix.AM_ACTIVE;
    for (int i = 0; i < 4; i++) // add active channels to ID
    {
        if (_audioMix.channelActive_[i] == true)
        {
            commandID += 1 << i;
        }
    }

    if (_audioMix != null)
    {
        await _audioMix.ExecuteAudioAsync(commandID);
    }
}

// buttonhandler reset Audiocontrol resets volume, fader and mutes all audio channels
private async void button6_Click(object sender, EventArgs e)
{
    if (_audioMix != null)
    {
        await _audioMix.ExecuteAudioAsync(AudioMix.AM_AUDIO_RESET);
    }
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
/// Resolves optional PiP input parameter for multiview commands.
/// G=GoPro, K=Kanzel/Predigtkamera, L=Laptop, A=Altar/Hauptkamera.
/// Without parameter the backward-compatible default is GoPro.
/// </summary>
private ATEMInput ResolveMultiViewPiPInput(string[] commands, string commandName)
{
    if (commands == null || commands.Length < 3 || string.IsNullOrWhiteSpace(commands[2]))
    {
        return ATEMInput.GoPro;
    }

    char mode = char.ToUpperInvariant(commands[2][0]);
    switch (mode)
    {
        case 'G': return ATEMInput.GoPro;
        case 'K': return ATEMInput.CanonPtzPreacher;
        case 'L': return ATEMInput.Laptop;
        case 'A': return ATEMInput.CanonPtzMain;
        default:
            _logDat?.sendInfoMessage($"JokiAutomation\n{commandName}: Unbekannter PiP-Parameter '{commands[2]}', fallback auf GoPro");
            return ATEMInput.GoPro;
    }
}

/// <summary>
/// Executes the covered PTZ camera sequence:
/// 1. Confirm the movement/cover input on Program.
/// 2. Move the PTZ camera and await its centralized settling time.
/// 3. Return to the positioned PTZ main camera and confirm it on Program.
///
/// If PTZ movement fails or is cancelled, the cover input remains on Program.
/// Both ATEM transitions run on the UI thread / COM owner thread via InvokeAsync.
/// </summary>
private async Task ExecuteCameraSwitchSequenceAsync(
    int positionId,
    ATEMInput movementInput,
    ATEMInput finalInput,
    System.Threading.CancellationToken cancellationToken = default)
{
    string sequenceId = Guid.NewGuid().ToString("N").Substring(0, 8);
    int active = Interlocked.Increment(ref _activePositionControls);
    string requestId = _activeCommandRequestId.Value ?? string.Empty;
    string sceneId = _currentCommand ?? string.Empty;
    int pid = Process.GetCurrentProcess().Id;
    int tid = Thread.CurrentThread.ManagedThreadId;
    _logDat?.sendInfoMessage($"[POSITIONCONTROL_ENTER] SequenceId={sequenceId} Active={active} Position={positionId} Camera={movementInput} TargetInput={finalInput} PID={pid} TID={tid} RequestId={requestId} SceneId={sceneId}");
    if (active > 1)
    {
        _logDat?.sendInfoMessage($"[POSITIONCONTROL_CONCURRENCY_WARNING] SequenceId={sequenceId} Active={active} Position={positionId} PID={pid} TID={tid} RequestId={requestId} SceneId={sceneId}");
    }

    try
    {
        if (_positionControl == null)
        {
            throw new InvalidOperationException("PositionControl ist nicht initialisiert.");
        }

        if (_atemControl == null || !_atemControl.IsConnected)
        {
            throw new InvalidOperationException(
                "ATEM ist nicht verbunden. PTZ-Sequenz wird nicht gestartet.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        _logDat?.sendInfoMessage($"[PTZ] PRESET_BEGIN Preset=\"{positionId}\"");
        bool isAutoTrackingEnabled = false;
        try
        {
            var xcController = _canonPtzController as XcCanonPtzController;
            if (xcController != null)
            {
                var autoTrackingService = xcController.GetAutoTrackingService();
                if (autoTrackingService != null)
                {
                    isAutoTrackingEnabled = await autoTrackingService.IsEnabledAsync();
                }
            }
        }
        catch (Exception ex)
        {
            _logDat?.sendInfoMessage($"JokiAutomation\n[PTZ] AUTOTRACKING_STATE_ERROR Error=\"{ex.Message}\"");
        }

        _logDat?.sendInfoMessage($"[PTZ] AUTOTRACKING_STATE Enabled={isAutoTrackingEnabled}");

        _logDat?.sendInfoMessage(
            $"JokiAutomation\n[CamSwitch] Sequenz gestartet - Position={positionId}, " +
            $"Movement={movementInput}, Final={finalInput}");

        _logDat?.sendInfoMessage($"[POSITIONCONTROL_COVER_BEGIN] SequenceId={sequenceId} Position={positionId} Camera={movementInput} TargetInput={finalInput} PID={pid} TID={tid} RequestId={requestId} SceneId={sceneId}");
        await SetATEMProgramInputAsync(movementInput, "PositionControl.Cover", sceneId, requestId);
        _logDat?.sendInfoMessage($"[POSITIONCONTROL_COVER_CONFIRMED] SequenceId={sequenceId} Position={positionId} Camera={movementInput} TargetInput={finalInput} PID={pid} TID={tid} RequestId={requestId} SceneId={sceneId}");

        _logDat?.sendInfoMessage(
            $"JokiAutomation\n[CamSwitch] Ausweichkamera bestaetigt - Program={movementInput}");

        _logDat?.sendInfoMessage($"[POSITIONCONTROL_MOVE_BEGIN] SequenceId={sequenceId} Position={positionId} Camera={movementInput} TargetInput={finalInput} PID={pid} TID={tid} RequestId={requestId} SceneId={sceneId}");
        PositionMoveResult moveResult =
            await _positionControl.MoveToPosAsync(
                positionId,
                cancellationToken);

        if (!moveResult.Success)
        {
            _logDat?.sendInfoMessage(
                $"JokiAutomation\n[CamSwitch] PTZ-Positionierung fehlgeschlagen - " +
                $"Ausweichkamera bleibt aktiv: {moveResult.Message}");
            _logDat?.sendInfoMessage($"[PTZ] PRESET_FAILED Preset=\"{positionId}\" Error=\"{moveResult.Message}\"");

            throw new InvalidOperationException(
                $"Kameraposition konnte nicht angefahren werden: " +
                moveResult.Message);
        }

        _logDat?.sendInfoMessage($"[POSITIONCONTROL_MOVE_COMPLETED] SequenceId={sequenceId} Position={positionId} Camera={movementInput} TargetInput={finalInput} PID={pid} TID={tid} RequestId={requestId} SceneId={sceneId}");
        _logDat?.sendInfoMessage(
            $"JokiAutomation\n[CamSwitch] PTZ-Settling abgeschlossen - " +
            $"Preset={moveResult.CanonPreset}, Rueckschaltung={finalInput}");

        _logDat?.sendInfoMessage($"[POSITIONCONTROL_FINAL_BEGIN] SequenceId={sequenceId} Position={positionId} Camera={finalInput} TargetInput={finalInput} PID={pid} TID={tid} RequestId={requestId} SceneId={sceneId}");
        await SetATEMProgramInputAsync(finalInput, "PositionControl.Final", sceneId, requestId);
        _logDat?.sendInfoMessage($"[POSITIONCONTROL_FINAL_CONFIRMED] SequenceId={sequenceId} Position={positionId} Camera={finalInput} TargetInput={finalInput} PID={pid} TID={tid} RequestId={requestId} SceneId={sceneId}");

        _logDat?.sendInfoMessage(
            $"JokiAutomation\n[CamSwitch] Sequenz abgeschlossen - Program={finalInput}");
        _logDat?.sendInfoMessage($"[PTZ] PRESET_SUCCESS Preset=\"{positionId}\"");
        _logDat?.sendInfoMessage($"[POSITIONCONTROL_COMPLETED] SequenceId={sequenceId} Position={positionId} Camera={finalInput} TargetInput={finalInput} PID={pid} TID={tid} RequestId={requestId} SceneId={sceneId}");
    }
    finally
    {
        int remaining = Interlocked.Decrement(ref _activePositionControls);
        _logDat?.sendInfoMessage($"[POSITIONCONTROL_EXIT] SequenceId={sequenceId} Active={remaining} Position={positionId} Camera={finalInput} TargetInput={finalInput} PID={pid} TID={tid} RequestId={requestId} SceneId={sceneId}");
    }
}
/// <summary>
/// Helper extension to invoke async operations on UI thread using SynchronizationContext
/// (no Control.Invoke needed - works without window handle)
/// </summary>
private async Task InvokeAsync(Func<Task> action)
{
    int currentThreadId = Thread.CurrentThread.ManagedThreadId;

    // Wenn wir bereits auf dem UI-Thread sind → direkt ausführen
    if (currentThreadId == _uiThreadId)
    {
        await action();
        return;
    }

    // Sonst: Dispatch zu UI-Thread via SynchronizationContext
    var completionSource = new TaskCompletionSource<bool>();

    _uiContext.Post(async _ =>
    {
        try
        {
            int dispatchedThreadId = Thread.CurrentThread.ManagedThreadId;
            Debug.WriteLine($"📌 InvokeAsync dispatched to ThreadId={dispatchedThreadId} (expected={_uiThreadId})");
            
            await action();
            completionSource.SetResult(true);
        }
        catch (OperationCanceledException ex)
        {
            completionSource.SetException(ex);
        }
        catch (Exception ex)
        {
            completionSource.SetException(ex);
        }
    }, null);

    await completionSource.Task;
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
            _positionControl?.UpdatePtzController(_canonPtzController);
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
        _atemControl = new ATEMControl(atemIP, message => _logDat?.sendInfoMessage(message));
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
            _atemControl = new ATEMControl(atemIP, message => _logDat?.sendInfoMessage(message));

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
/// Switch ATEM to specified HDMI input without blocking the caller.
/// </summary>
private Task SwitchATEMInput(ATEMInput input)
{
    if (_atemControl == null || !_atemControl.IsConnected)
    {
        _logDat?.sendInfoMessage("JokiAutomation\nATEM nicht verbunden");
        return Task.CompletedTask;
    }

    return SwitchATEMInputAsync(input);
}

/// <summary>
/// Switch ATEM to specified HDMI input asynchronously.
/// </summary>
private async Task SwitchATEMInputAsync(
    ATEMInput input,
    string source = "VideoPipeline",
    string sceneId = null,
    string requestId = null)
{
    string sourceName = GetATEMInputName(input);
    string switchId = Guid.NewGuid().ToString("N").Substring(0, 8);
    string normalizedSceneId = string.IsNullOrWhiteSpace(sceneId) ? (_currentCommand ?? string.Empty) : sceneId;
    string normalizedRequestId = string.IsNullOrWhiteSpace(requestId) ? (_activeCommandRequestId.Value ?? string.Empty) : requestId;

    _logDat?.sendInfoMessage(
        $"[CAMSWITCH_BEGIN] Id={switchId} Source={source} Input={sourceName} SceneId={normalizedSceneId} RequestId={normalizedRequestId} PID={Process.GetCurrentProcess().Id}");

    if (_atemControl == null || !_atemControl.IsConnected)
    {
        _logDat?.sendInfoMessage("JokiAutomation\nATEM nicht verbunden");
        _logDat?.sendInfoMessage($"[ATEM] INPUT_FAILED Input=\"{sourceName}\" Error=\"ATEM nicht verbunden\"");
        _logDat?.sendInfoMessage($"[CAMSWITCH_END] Id={switchId} Source={source} Input={sourceName}");
        return;
    }

    try
    {
        LogCameraSwitch(source, sourceName);
        _logDat?.sendInfoMessage($"[ATEM] INPUT_BEGIN Input=\"{sourceName}\"");
        ATEMControl.VideoSource atemSource = (ATEMControl.VideoSource)((int)input);
        await _atemControl.TransitionToProgramInputAsync(
            atemSource,
            CancellationToken.None,
            null,
            source,
            normalizedSceneId,
            normalizedRequestId,
            switchId);

        _logDat?.sendInfoMessage($"JokiAutomation\nATEM umgeschaltet auf {sourceName} (HDMI {(int)input})");
        _logDat?.sendInfoMessage($"[ATEM] INPUT_SUCCESS Input=\"{sourceName}\"");
    }
    catch (Exception ex)
    {
        _logDat?.sendInfoMessage($"JokiAutomation\nATEM Fehler: {ex.Message}");
        _logDat?.sendInfoMessage($"[ATEM] INPUT_FAILED Input=\"{sourceName}\" Error=\"{ex.Message}\"");
    }
    finally
    {
        _logDat?.sendInfoMessage($"[CAMSWITCH_END] Id={switchId} Source={source} Input={sourceName}");
    }
}

/// <summary>
/// Phase 2C: explicit, fully-awaitable video execution hook for the "Predigt" scene used by the
/// ScenePlan/CommandDispatcher VideoCommand executor. Encapsulates exactly today's "Predigt" visual
/// sequence (DisablePictureInPictureAsync -> SwitchATEMInputAsync(CanonPtzPreacher) ->
/// DisablePictureInPictureAsync), unchanged in order and behavior. Does not change SwitchATEMInputAsync's
/// existing error handling: ATEM exceptions remain logged internally and are not rethrown here.
/// </summary>
internal async Task ExecutePredigtVideoAsync()
{
    await DisablePictureInPictureAsync();
    await SwitchATEMInputAsync(ATEMInput.CanonPtzPreacher);
    // Ensure final state stays PiP-off after source switch.
    await DisablePictureInPictureAsync();
}

/// <summary>
/// Phase 2F: single source of truth for the "active PTZ" condition that selects between the
/// production PTZ orchestrator path and the legacy RasPi positioning path. Reused identically by
/// both the legacy CommandInterpreterAsync branch and the ScenePlan/PositionCommand executor so the
/// two paths can never diverge.
/// </summary>
internal bool IsActivePtzPositionControlMode()
{
    return _isPtzCameraMode && _canonPtzController != null;
}

/// <summary>
/// Phase 2F: ScenePlan/CommandDispatcher PositionCommand executor entry point for active PTZ
/// PositionControl requests. Reuses the existing, unchanged PositionControlPtzAudioOrchestrator
/// (Audio -> Cover -> PTZ MoveToPosAsync -> Final) and the existing production arguments
/// (position name, profile code) without reinterpreting them.
/// </summary>
internal Task ExecutePositionControlPtzAsync(string positionName, string profileCode)
{
    var ptzOrchestrator = new PositionControlPtzAudioOrchestrator(
        commandId => _audioMix.ExecuteAudioAsync(commandId),
        (positionId, coverInput, targetInput) => ExecuteCameraSwitchSequenceAsync(positionId, coverInput, targetInput),
        message => _logDat?.sendInfoMessage(message));

    return ptzOrchestrator.ExecuteAsync(
        positionName,
        profileCode,
        listBoxCamPosControl.Items.Cast<object>().Select(item => item?.ToString()).ToList());
}

/// <summary>
/// Phase 2D: resolves the optional PiP source parameter the same way the legacy multiview cases
/// (Gebet/BandMulti/LesungMulti) do via <see cref="ResolveMultiViewPiPInput"/>, but from a
/// ScenePlan/PipelineCommand argument list (Arguments[0]=command name, Arguments[1]=optional PiP
/// parameter) instead of the raw CommandInterpreterAsync commands array (commands[0]=exe,
/// commands[1]=command, commands[2]=optional PiP parameter). Reuses the existing resolver so the
/// PiP-selection logic itself is not duplicated or changed.
/// </summary>
private ATEMInput ResolveMultiViewPiPInputFromCommandArguments(IReadOnlyList<string> arguments, string commandName)
{
    if (arguments == null || arguments.Count < 2 || string.IsNullOrWhiteSpace(arguments[1]))
    {
        return ATEMInput.GoPro;
    }

    string[] legacyStyleCommands = { "JokiAutomation.exe", arguments[0], arguments[1] };
    return ResolveMultiViewPiPInput(legacyStyleCommands, commandName);
}

/// <summary>
/// Phase 2D: explicit, fully-awaitable video execution hook shared by the migrated "Gebet",
/// "BandMulti" and "LesungMulti" ScenePlan VideoCommands. All three legacy cases share the exact
/// same visual sequence (DisablePictureInPicture -> SwitchATEMInputAsync(Laptop) ->
/// Task.Delay(1000) -> EnablePictureInPicture(pipInput)); this method reproduces that sequence,
/// order and delay unchanged, only parameterized by the resolved PiP input and the command name
/// used for log correlation.
/// </summary>
private async Task ExecuteMultiViewVideoAsync(ATEMInput pipInput, string commandName)
{
    DisablePictureInPicture();
    _logDat?.sendInfoMessage($"JokiAutomation\n[VISUAL_PIPE] Diashow/PiP sofort ausblenden ({commandName})");
    _logDat?.sendInfoMessage($"JokiAutomation\n[VISUAL_PIPE] Starte Bild/Kamera-Kommandos parallel zum Audio-Fade ({commandName})");
    _logDat?.sendInfoMessage("JokiAutomation\n[PIPE_PARALLEL] VisualStart RequestId='" + _activeCommandRequestId.Value + "' Command='" + commandName + "'");
    await SwitchATEMInputAsync(ATEMInput.Laptop);
    await Task.Delay(1000); // Kurze Verzögerung, um sicherzustellen, dass der ATEM die Eingangsquelle gewechselt hat
    EnablePictureInPicture(pipInput);
    _logDat?.sendInfoMessage("JokiAutomation\n[PIPE_PARALLEL] VisualCompleted RequestId='" + _activeCommandRequestId.Value + "' Command='" + commandName + "'");
}

/// <summary>
/// Phase 2D: ScenePlan VideoCommand executor entry point for the migrated multiview scenes
/// ("Gebet", "BandMulti", "LesungMulti"). Resolves the PiP input exactly as the legacy case does
/// and then runs the exact same, unchanged visual sequence via
/// <see cref="ExecuteMultiViewVideoAsync"/>.
/// </summary>
internal Task ExecuteSceneVideoAsync(string commandName, IReadOnlyList<string> arguments)
{
    ATEMInput pipInput = ResolveMultiViewPiPInputFromCommandArguments(arguments, commandName);
    return ExecuteMultiViewVideoAsync(pipInput, commandName);
}

/// <summary>
/// Phase 2E: explicit, fully-awaitable video execution hook for the migrated "Text" ScenePlan
/// VideoCommand. Unlike the multiview scenes (Gebet/BandMulti/LesungMulti), the legacy "Text" case
/// never enables Picture-in-Picture again; it only disables PiP, switches to the Laptop input, and
/// disables PiP a second time (DisablePictureInPictureAsync -> SwitchATEMInputAsync(Laptop) ->
/// DisablePictureInPictureAsync). Order and calls preserved unchanged from the legacy case. Does not
/// change SwitchATEMInputAsync's existing error handling: ATEM exceptions remain logged internally
/// and are not rethrown here.
/// </summary>
internal async Task ExecuteTextVideoAsync()
{
    await DisablePictureInPictureAsync();
    _logDat?.sendInfoMessage("JokiAutomation\\n[VISUAL_PIPE] Diashow/PiP sofort ausblenden (Text)");
    _logDat?.sendInfoMessage("JokiAutomation\\n[VISUAL_PIPE] Starte Bild/Kamera-Kommandos parallel zum Audio-Fade (Text)");
    _logDat?.sendInfoMessage("JokiAutomation\\n[PIPE_PARALLEL] VisualStart RequestId='" + _activeCommandRequestId.Value + "' Command='Text'");
    await SwitchATEMInputAsync(ATEMInput.Laptop);
    await DisablePictureInPictureAsync();
    _logDat?.sendInfoMessage("JokiAutomation\\n[PIPE_PARALLEL] VisualCompleted RequestId='" + _activeCommandRequestId.Value + "' Command='Text'");
}

private async Task SetATEMProgramInputAsync(
    ATEMInput input,
    string source,
    string sceneId = null,
    string requestId = null)
{
    string sourceName = GetATEMInputName(input);
    string switchId = Guid.NewGuid().ToString("N").Substring(0, 8);
    string normalizedSceneId = string.IsNullOrWhiteSpace(sceneId) ? (_currentCommand ?? string.Empty) : sceneId;
    string normalizedRequestId = string.IsNullOrWhiteSpace(requestId) ? (_activeCommandRequestId.Value ?? string.Empty) : requestId;

    _logDat?.sendInfoMessage(
        $"[CAMSWITCH_BEGIN] Id={switchId} Source={source} Input={sourceName} SceneId={normalizedSceneId} RequestId={normalizedRequestId} PID={Process.GetCurrentProcess().Id}");
    _logDat?.sendInfoMessage("[CAMSWITCH_TRACE]\n" + Environment.StackTrace);

    if (_atemControl == null || !_atemControl.IsConnected)
    {
        _logDat?.sendInfoMessage("JokiAutomation\nATEM nicht verbunden");
        _logDat?.sendInfoMessage($"[ATEM] PROGRAM_SET_FAILED Input=\"{sourceName}\" Source={source} Error=\"ATEM nicht verbunden\"");
        _logDat?.sendInfoMessage($"[CAMSWITCH_END] Id={switchId} Source={source} Input={sourceName}");
        return;
    }

    try
    {
        LogCameraSwitch(source, sourceName);
        _logDat?.sendInfoMessage($"[ATEM] PROGRAM_SET_BEGIN Input=\"{sourceName}\" Source={source}");
        ATEMControl.VideoSource atemSource = (ATEMControl.VideoSource)((int)input);
        await _atemControl.SetProgramInputAsync(
            atemSource,
            CancellationToken.None,
            null,
            source,
            normalizedSceneId,
            normalizedRequestId,
            switchId);

        _logDat?.sendInfoMessage($"JokiAutomation\nATEM direkt auf {sourceName} gesetzt (HDMI {(int)input})");
        _logDat?.sendInfoMessage($"[ATEM] PROGRAM_SET_SUCCESS Input=\"{sourceName}\" Source={source}");
    }
    catch (Exception ex)
    {
        _logDat?.sendInfoMessage($"JokiAutomation\nATEM Fehler: {ex.Message}");
        _logDat?.sendInfoMessage($"[ATEM] PROGRAM_SET_FAILED Input=\"{sourceName}\" Source={source} Error=\"{ex.Message}\"");
    }
    finally
    {
        _logDat?.sendInfoMessage($"[CAMSWITCH_END] Id={switchId} Source={source} Input={sourceName}");
    }
}

private void LogCameraSwitch(string source, string camera)
{
    string normalizedSource = string.IsNullOrWhiteSpace(source) ? "Unknown" : source;
    string normalizedCamera = string.IsNullOrWhiteSpace(camera) ? "Unknown" : camera;
    _logDat?.sendInfoMessage($"CAMERA_SWITCH Source={normalizedSource} Camera={normalizedCamera}");
}

        /// <summary>
        /// Selects the given audio tone profile in the UI.
        /// </summary>
        /// <summary>
        /// Wählt das angegebene Audioprofil aus, sendet es an den Raspberry Pi
        /// und wartet kurz auf die Verarbeitung.
        /// </summary>
        /// <param name="profileName">Name des Audioprofils, z. B. Band.</param>
        /// <returns>
        /// True, wenn das Profil gefunden und an den Raspberry Pi gesendet wurde,
        /// andernfalls false.
        /// </returns>
        private async Task<bool> SetAudioProfileAsync(string profileName)
        {
            return await ApplyAudioProfileByNameAsync(
                profileName,
                TimeSpan.FromMilliseconds(200),
                CancellationToken.None);
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
    EnablePictureInPicture(ATEMInput.GoPro);
}

/// <summary>
/// Enable Picture-in-Picture mode with a specific PiP fill input.
/// </summary>
private void EnablePictureInPicture(ATEMInput pipInput)
{
    if (_atemControl == null || !_atemControl.IsConnected)
    {
        _logDat?.sendInfoMessage("JokiAutomation\nATEM nicht verbunden");
        return;
    }

    try
    {
        ATEMControl.VideoSource source = (ATEMControl.VideoSource)((int)pipInput);
        _atemControl.EnablePictureInPicture(source);
        _logDat?.sendInfoMessage($"JokiAutomation\nBild-in-Bild aktiviert ({GetATEMInputName(pipInput)})");
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
/// Disable Picture-in-Picture mode asynchronously and verify final off-air state.
/// </summary>
private async Task DisablePictureInPictureAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
{
    if (_atemControl == null || !_atemControl.IsConnected)
    {
        _logDat?.sendInfoMessage("JokiAutomation\nATEM nicht verbunden");
        return;
    }

    try
    {
        await _atemControl.DisablePictureInPictureAsync(cancellationToken, timeout);
        _logDat?.sendInfoMessage("JokiAutomation\nBild-in-Bild deaktiviert");
    }
    catch (Exception ex)
    {
        _logDat?.sendInfoMessage($"JokiAutomation\nBild-in-Bild Fehler: {ex.Message}");
        _logDat?.sendInfoMessage("JokiAutomation\nPiP-Deaktivierung nicht bestätigt - Ablauf wird fortgesetzt.");
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
        if (micNumber ==  1)
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

        await SwitchATEMInputAsync(atemInput);

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
        var homeResult = await _canonPtzController.RecallPresetAsync(1); // Preset 1 as "home"
        
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

private string ResolveLifecycleMode()
{
    if (!string.IsNullOrEmpty(Application.ProductName) &&
        Application.ProductName.IndexOf("EventTimer", StringComparison.OrdinalIgnoreCase) >= 0)
    {
        return "EventTimer";
    }

    if (!ShowInTaskbar && Opacity <= 0.0d)
    {
        return "CLI";
    }

    if (Visible)
    {
        return "GUI";
    }

    return "UNKNOWN";
}

protected override void OnShown(EventArgs e)
{
    LogData.LogLifecycle(ResolveLifecycleMode(), "FORM_SHOWN");
    base.OnShown(e);
}

protected override void OnFormClosing(FormClosingEventArgs e)
{
    LogData.LogLifecycle(
        ResolveLifecycleMode(),
        "FORM_CLOSING",
        $"CloseReason={e.CloseReason} Cancel={e.Cancel} Visible={Visible} IsDisposed={IsDisposed} Disposing={Disposing} OpenFormsCount={Application.OpenForms.Count} ShutdownInProgress={Program.IsShutdownInProgress}");

    base.OnFormClosing(e);

    LogData.LogLifecycle(
        ResolveLifecycleMode(),
        "FORM_CLOSING_RETURN",
        $"CloseReason={e.CloseReason} Cancel={e.Cancel}");
}

protected override void OnFormClosed(FormClosedEventArgs e)
{
    LogData.LogLifecycle(
        ResolveLifecycleMode(),
        "FORM_CLOSED",
        $"CloseReason={e.CloseReason} OpenFormsCount={Application.OpenForms.Count} ShutdownInProgress={Program.IsShutdownInProgress}");

    base.OnFormClosed(e);
}
    }
}

