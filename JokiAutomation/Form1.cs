using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration; // ✅ ADD THIS
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

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
            GoPro = 1,              // HDMI 1: GoPro Actionkamera
            Laptop = 2,             // HDMI 2: Laptop/Computer
            CamcorderMain = 3,      // HDMI 3: Camcorder Schwenkneiger (Hauptkamera)
            CamcorderPreacher = 4   // HDMI 4: Camcorder Empore (Predigtkamera)
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
        private Dictionary<string, string> _userPasswords; // ✅ NEU

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
            _positionControl.initPC(this);
            _autoZoom.initAZ(this);

            InitializeNetworkConfig();
            InitializeATEMControl();
            //rvtest InitializeRokuTV();

            _Inputtimer.Interval = TIMER_INTERVAL_MS;  // check rich text box each 1000ms
            _Inputtimer.Tick += new System.EventHandler(Inputtimer_Elapsed);
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
            _userPasswords = new Dictionary<string, string>(); // ✅ NEU
            string configPath = Path.Combine(Application.StartupPath, NETWORK_CONFIG_FILE);

            if (!File.Exists(configPath))
            {
                _logDat?.sendInfoMessage($"JokiAutomation\nNetzwerk-Konfigurationsdatei nicht gefunden: {configPath}");
                _logDat?.sendInfoMessage($"JokiAutomation\nERROR: Keine Geräte-Konfiguration vorhanden!");
                return;
            }

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
                    // User Credential: USER_[Role];Password
                    string[] parts = line.Split(';');

                    if (parts.Length < 2 || parts.Length > 5)
                    {
                        _logDat?.sendInfoMessage($"JokiAutomation\nUngültige Zeile in {NETWORK_CONFIG_FILE}: {line}");
                        continue;
                    }

                    string deviceName = parts[0].Trim();

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


        /// <summary>
        /// Interprets command line arguments and executes corresponding automation commands.
        /// </summary>
        /// <param name="commands">Command line arguments array</param>
        public void CommandInterpreter(string[] commands)
        {
            // Validate input
            if (commands == null || commands.Length < 2)
            {
                _logDat?.sendInfoMessage("JokiAutomation\nKeine gültigen Kommandozeilenargumente übergeben.");
                return;
            }

            string cmd = commands[1];

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
                        _audioMix?._rasPi?.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_POSCAM_VIEW);
                        DisablePictureInPicture();
                        SwitchATEMInput(ATEMInput.CamcorderMain);
                        break;

                    case "Predigt":
                        _audioMix?._rasPi?.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_PREACHER_VIEW);
                        DisablePictureInPicture();
                        SwitchATEMInput(ATEMInput.CamcorderPreacher);
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
                        _audioMix?._rasPi?.rasPiExecute(InfraredControl.IR_SEQUENCE, InfraredControl.IR_SHUTDOWN);
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
                                case 'A':  // Altar = Camcorder Main
                                    SwitchATEMInput(ATEMInput.CamcorderMain);
                                    break;

                                case 'G':  // GoPro = GoPro Actionkamera
                                    SwitchATEMInput(ATEMInput.GoPro);
                                    break;

                                case 'K':  // Kanzel = Camcorder Preacher
                                    SwitchATEMInput(ATEMInput.CamcorderPreacher);
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

                        _positionControl?.sequence(commands[2], commands[3]);
                        SwitchATEMInput(ATEMInput.CamcorderMain);
                        break;

                    case "AutoZoom":
                        _autoZoom?.openDialog(this);
                        break;

                    case "ZoomReferenz":
                        moveZoomReference();
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
            }
            catch (Exception ex)
            {
                _logDat?.sendInfoMessage($"JokiAutomation\nFehler beim Ausführen des Kommandos '{cmd}':\n{ex.Message}");
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
        private void TabControl1_SelectedIndexChanged(Object sender, EventArgs e)
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

        // teach selected position of camcorder; this method needs super user log in
        private void teachCamPos_Click(object sender, EventArgs e)
        {
            richTextBox3.Clear();
            _requestedFunction = FUNCTION_TEACH_POSITION;

            if (loginUser("SuperUser"))
            {
                _positionControl?.teachPos(listBoxCamPosControl.SelectedIndex);

                if (_autoZoom?._AZ_ZoomValue != null &&
                    listBoxCamPosControl.SelectedIndex < _autoZoom._AZ_ZoomValue.Length &&
                    TEMP_ZOOM_INDEX < _autoZoom._AZ_ZoomValue.Length)
                {
                    _autoZoom._AZ_ZoomValue[listBoxCamPosControl.SelectedIndex] = _autoZoom._AZ_ZoomValue[TEMP_ZOOM_INDEX];
                }
            }
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
            _positionControl.moveButtonPressed(PositionControl.PC_BUTTON_UP);
        }
        // eventhandler move down
        private void moveDownHandler(object sender, EventArgs e)
        {
            _positionControl.moveButtonPressed(PositionControl.PC_BUTTON_DOWN);
        }
        // eventhandler move left
        private void moveLeftHandler(object sender, EventArgs e)
        {
            _positionControl.moveButtonPressed(PositionControl.PC_BUTTON_LEFT);
        }
        // eventhandler move right
        private void moveRightHandler(object sender, EventArgs e)
        {
            _positionControl.moveButtonPressed(PositionControl.PC_BUTTON_RIGHT);
        }
        // eventhandler move button released handler
        private void moveDoneHandler(object sender, EventArgs e)
        {
            _positionControl.moveButtonPressed(PositionControl.PC_BUTTON_RELEASED);
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

        private void button6_Click(object sender, EventArgs e)
        {
            // TODO: Implement the logic for resetting the audiomix as needed.
            // For now, you can leave it empty or add a comment.
        }

        private void button5_Click(object sender, EventArgs e)
        {
            // TODO: Implement the logic for the "Init" button in groupBox1 (Audiomix/Eingangssignale)
            // For now, just show a message box as a placeholder.
            MessageBox.Show("Init button clicked.");
        }

        private void moveCamPos_Click(object sender, EventArgs e)
        {
            // TODO: Implement the logic for moving the camera position.
            // For now, you can leave it empty or add your intended functionality here.
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
        /// MIC 1: ON, MIC 2: OFF
        /// Source: MainCamcorder (HDMI 3)
        /// PiP: OFF
        /// Transition: MIX, 1.0s duration, AUTO ON
        /// Recording: OFF, Streaming: OFF
        /// </summary>
        private void InitializeATEMToDefault()
        {
            try
            {
                // Prüfen, ob ATEM in Network.cfg konfiguriert ist
                if (!_networkDevices.ContainsKey("ATEM_MiniPro"))
                {
                    _logDat?.sendInfoMessage("JokiAutomation\nATEM Mini Pro nicht in Network.cfg konfiguriert!");
                    return;
                }

                // ATEM-Verbindung initialisieren, falls noch nicht verbunden
                if (_atemControl == null || !_atemControl.IsConnected)
                {
                    string atemIP = _networkDevices["ATEM_MiniPro"].IPAddress;
                    _atemControl = new ATEMControl(atemIP);

                    if (!_atemControl.Connect())
                    {
                        _logDat?.sendInfoMessage($"JokoAutomation\nATEM Verbindung fehlgeschlagen ({atemIP}:9910)");
                        _logDat?.sendInfoMessage("JokiAutomation\nBitte prüfen:\n" +
                                                "- ATEM Mini Pro ist eingeschaltet\n" +
                                                "- Netzwerkkabel ist verbunden\n" +
                                                "- IP-Adresse ist korrekt\n" +
                                                "- Firewall blockiert Port 9910 nicht");
                        return;
                    }

                    _logDat?.sendInfoMessage($"JokiAutomation\nATEM Mini Pro verbunden ({atemIP})");
                }

                _logDat?.sendInfoMessage("JokiAutomation\nATEM Mini Pro wird initialisiert...");

                // Eingebaute Initialisierungsmethode aus ATEMControl aufrufen
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
        /// <param name="input">ATEM input source</param>
        private void SwitchATEMInput(ATEMInput input)
        {
            if (_atemControl == null || !_atemControl.IsConnected)
            {
                _logDat?.sendInfoMessage("JokiAutomation\nATEM nicht verbunden");
                return;
            }

            try
            {
                ATEMControl.VideoSource source = (ATEMControl.VideoSource)((int)input - 1);
                _atemControl.TransitionToProgramInput(source);

                string sourceName = GetATEMInputName(input);
                _logDat?.sendInfoMessage($"JokiAutomation\nATEM umgeschaltet auf {sourceName} (HDMI {(int)input})");
            }
            catch (Exception ex)
            {
                _logDat?.sendInfoMessage($"JokiAutomation\nATEM Fehler: {ex.Message}");
            }
        }

        /// <summary>
        /// Overload for backward compatibility with integer input
        /// </summary>
        /// <param name="inputNumber">HDMI Input number (1-4)</param>
        private void SwitchATEMInput(int inputNumber)
        {
            if (Enum.IsDefined(typeof(ATEMInput), inputNumber))
            {
                SwitchATEMInput((ATEMInput)inputNumber);
            }
            else
            {
                _logDat?.sendInfoMessage($"JokiAutomation\nUngültige ATEM-Eingangsnummer: {inputNumber} (gültig: 1-4)");
            }
        }

        /// <summary>
        /// Get user-friendly name for ATEM input
        /// </summary>
        /// <param name="input">ATEM input source</param>
        /// <returns>Descriptive name of the input source</returns>
        private string GetATEMInputName(ATEMInput input)
        {
            switch (input)
            {
                case ATEMInput.GoPro:
                    return "GoPro Actionkamera";
                case ATEMInput.Laptop:
                    return "Laptop";
                case ATEMInput.CamcorderMain:
                    return "Camcorder Schwenkneiger";
                case ATEMInput.CamcorderPreacher:
                    return "Camcorder Empore";
                default:
                    return $"HDMI {(int)input}";
            }
        }

        /// <summary>
        /// Enable Picture-in-Picture mode with GoPro in bottom-right corner and Laptop as main
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
                // PiP aktivieren: Input 1 (GoPro) unten rechts, klein
                _atemControl.EnablePictureInPicture(
                    ATEMControl.VideoSource.Input1,
                    ATEMControl.PiPPosition.BottomRight,
                    ATEMControl.PiPSize.Small
                );

                // Status prüfen
                if (_atemControl.IsPiPActive())
                {
                    _logDat?.sendInfoMessage("JokiAutomation\nBild-in-Bild aktiviert: GoPro (unten rechts, klein) + Laptop (Hauptbild)");
                }
                
                Thread.Sleep(500); // Kurze Pause, um sicherzustellen, dass die PiP-Einstellungen übernommen werden
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
                // ✅ NUR DisablePictureInPicture aufrufen (kein doppelter SetDownstreamKeyerOnAir Aufruf!)
                _atemControl.DisablePictureInPicture();
                _logDat?.sendInfoMessage("JokiAutomation\nBild-in-Bild deaktiviert");
            }
            catch (Exception ex)
            {
                _logDat?.sendInfoMessage($"JokiAutomation\nBild-in-Bild Fehler: {ex.Message}");
            }
        }

        /// <summary>
        /// Perform ATEM cut transition
        /// </summary>
        private void ATEMPerformCut()
        {
            if (_atemControl == null || !_atemControl.IsConnected)
            {
                _logDat?.sendInfoMessage("JokiAutomation\nATEM nicht verbunden");
                return;
            }

            try
            {
                // There is no PerformCut method in ATEMControl.
                // Instead, send the appropriate transition style and duration, then switch program input.
                // For a "cut", set transition style to Mix and duration to 0.
                _atemControl.SetTransitionStyle(ATEMControl.TransitionStyle.Mix);
                _atemControl.SetTransitionDuration(0);
                // Optionally, you may want to switch program/preview inputs here if needed.
                _logDat?.sendInfoMessage("JokiAutomation\nATEM Cut ausgeführt");
            }
            catch (Exception ex)
            {
                _logDat?.sendInfoMessage($"JokiAutomation\nATEM Fehler: {ex.Message}");
            }
        }

        /// <summary>
        /// Set ATEM microphone on/off
        /// </summary>
        /// <param name="micNumber">Microphone number (1 or 2)</param>
        /// <param name="enable">True to enable, false to disable</param>
        private void SetATEMMicrophone(int micNumber, bool enable)
        {
            if (_atemControl == null || !_atemControl.IsConnected)
            {
                _logDat?.sendInfoMessage("JokiAutomation\nATEM nicht verbunden");
                return;
            }

            try
            {
                ushort audioSource = (ushort)(micNumber - 1); // MIC 1 = 0, MIC 2 = 1
                _atemControl.SetAudioMixerInput(audioSource, enable);

                string state = enable ? "aktiviert" : "deaktiviert";
                _logDat?.sendInfoMessage($"JokiAutomation\nMIC {micNumber} {state}");
            }
            catch (Exception ex)
            {
                _logDat?.sendInfoMessage($"JokiAutomation\nATEM MIC Fehler: {ex.Message}");
            }
        }

        /// <summary>
        /// Returns a DelockSocketAdapter instance by name from Network.cfg.
        /// Connects to the adapter if not already connected.
        /// </summary>
        /// <param name="name">Adapter name (e.g., "HDMI_Extender_Transmitter")</param>
        /// <returns>DelockSocketAdapter instance or null if not found</returns>
        private DelockSocketAdapter GetDelockAdapter(string name)
        {
            // Check if already cached
            if (_delockAdapters != null && _delockAdapters.ContainsKey(name))
            {
                DelockSocketAdapter cachedAdapter = _delockAdapters[name];
                
                // Connect if not already connected
                if (!cachedAdapter.IsConnected)
                {
                    if (!cachedAdapter.Connect())
                    {
                        _logDat?.sendInfoMessage($"JokiAutomation\nVerbindung zu {name} fehlgeschlagen");
                        return null;
                    }
                    _logDat?.sendInfoMessage($"JokiAutomation\n{name} verbunden");
                }
                
                return cachedAdapter;
            }

            // Load from network config
            if (!_networkDevices.ContainsKey(name))
            {
                _logDat?.sendInfoMessage($"JokiAutomation\nDelock Adapter '{name}' nicht in Network.cfg konfiguriert");
                return null;
            }

            NetworkDevice device = _networkDevices[name];
            
            // Validate Delock device has credentials
            if (!device.IsDelockDevice)
            {
                _logDat?.sendInfoMessage($"JokiAutomation\nGerät '{name}' hat keine Delock-Zugangsdaten in Network.cfg");
                return null;
            }
            
            // Parse username as integer
            if (!int.TryParse(device.Username, out int usernameInt))
            {
                _logDat?.sendInfoMessage($"JokiAutomation\nUngültiger Username für {name}: {device.Username}");
                return null;
            }
            
            // Create new adapter with credentials from config (using original constructor signature)
            var adapter = new DelockSocketAdapter(
                name,                        
                device.IPAddress,           
                device.Port,                
                device.Username,            
                device.Password             
            );
            
            // Cache it
            if (_delockAdapters == null)
                _delockAdapters = new Dictionary<string, DelockSocketAdapter>();
            
            _delockAdapters[name] = adapter;
            
            // Connect
            if (!adapter.Connect())
            {
                _logDat?.sendInfoMessage($"JokiAutomation\nVerbindung zu {name} fehlgeschlagen");
                return null;
            }
            
            _logDat?.sendInfoMessage($"JokiAutomation\n{name} verbunden ({device.IPAddress}:{device.Port})");
            return adapter;
        }
    }
}

