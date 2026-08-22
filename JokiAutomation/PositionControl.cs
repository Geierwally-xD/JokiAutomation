using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using CanonRemoteControl;
using CanonPtzCommon;

namespace JokiAutomation
{
    /// <summary>
    /// Position control manager for camcorder positioning and IR control
    /// - Raspberry Pi: ALWAYS active (IR sequences, Audio, Beamer control)
    /// - Camera positioning: Either RasPi motor control OR Canon PTZ (depending on PTZ_CAM setting)
    /// </summary>
    class PositionControl : IDisposable
    {
        /// <summary>
        /// Initialize position control components and interface
        /// Reads position configuration from file and sets up Raspberry Pi and optionally Canon PTZ camera
        /// Note: Raspberry Pi is ALWAYS initialized for IR control (Beamer etc.)
        /// </summary>
        /// <param name="winForm">Parent form for logging and UI updates</param>
        /// <param name="isPtzMode">True to use Canon PTZ for camera positioning, false to use RasPi motor control</param>
        /// <param name="canonPtzController">Canon PTZ Controller instance (required if isPtzMode is true)</param>
        public void initPC(Form1 winForm, bool isPtzMode = false, ICanonPtzController canonPtzController = null)
        {
            _isPtzMode = isPtzMode;
            _canonPtzController = canonPtzController;

            // RasPi wird IMMER initialisiert (für IR-Steuerung)
            _rasPi.initRasPi(winForm);

            _PCForm = winForm;
            readConfigFile();

            string mode = _isPtzMode ? "Canon PTZ (Kamera-Positionierung)" : "RasPi Motor (Kamera-Positionierung)";
            _PCForm._logDat.sendInfoMessage($"JokiAutomation\nPosition Control: {mode} + RasPi IR-Steuerung");
        }

        /// <summary>
        /// Update PTZ controller (used when reconnecting to camera)
        /// </summary>
        /// <param name="canonPtzController">New Canon PTZ Controller instance</param>
        public void UpdatePtzController(ICanonPtzController canonPtzController)
        {
            _canonPtzController = canonPtzController;
            _PCForm._logDat.sendInfoMessage("PositionControl\nPTZ Controller aktualisiert");
        }

        /// <summary>
        /// Read all position descriptions from configuration file into listbox
        /// Loads PositionControl.cfg and populates the position listbox
        /// </summary>
        public void readConfigFile()
        {
            try
            { 
                if (File.Exists(_JokiAutomationPath + "PositionControl.cfg"))
                {
                    _PCForm.listBoxCamPosControl.Items.Clear();
                    string line;
                    var file = new System.IO.StreamReader(_JokiAutomationPath + "PositionControl.cfg");
                    while ((line = file.ReadLine()) != null)
                    {
                        _PCForm.listBoxCamPosControl.Items.Add(line);
                    }
                    file.Close();
                }
            }
            catch(Exception e)
            {
                _PCForm._logDat.sendInfoMessage(e.Message);
            }
        }

        /// <summary>
        /// Write all position descriptions from listbox to configuration file
        /// Saves current position names to PositionControl.cfg
        /// </summary>
        public void writeConfigFile()
        {
            try
            {
                using (StreamWriter sw = new StreamWriter(_JokiAutomationPath + "PositionControl.cfg"))
                {
                    foreach (var item in _PCForm.listBoxCamPosControl.Items)
                    {
                        sw.WriteLine(item);
                    }
                    sw.Close();
                }
            }
            catch(Exception e)
            {
                _PCForm._logDat.sendInfoMessage(e.Message);
            }
        }

        /// <summary>
        /// Maps Raspberry Pi position IDs (0-based) to Canon PTZ preset numbers (1-based)
        /// RasPi uses: 0=Altar, 1=Taufstein, 2=Kanzel, 3=Orgel, 4=Mittelgang, 5=Orgel2, 6=Mittelgang2, 7=Altarbild, 8=Liedtafel
        /// Canon PTZ uses: 1=Altar, 2=Taufstein, 3=Kanzel, 4=Orgel, 5=Mittelgang, 6=Orgel2, 7=Mittelgang2, 8=Altarbild, 9=Liedtafel
        /// </summary>
        private int MapRaspiPositionToCanonPreset(int raspiPositionId)
        {
            // Canon PTZ verwendet 1-basierte Preset-Nummern
            // RasPi verwendet 0-basierte Positions-IDs
            // Einfaches Mapping: Canon Preset = RasPi Position + 1
            return raspiPositionId + 1;
        }

        /// <summary>
        /// Move camcorder to specified position
        /// </summary>
        /// <param name="ID">Position ID to move to (0-based index)</param>
        public void movePC(int ID)
        {
            if (_isPtzMode && _canonPtzController != null)
            {
                int canonPreset = MapRaspiPositionToCanonPreset(ID);
                SharedPresetState.SetLastPreset(canonPreset);
                Task.Run(async () => await _canonPtzController.RecallPresetAsync(canonPreset));
            }
            else
            {
                _rasPi.rasPiExecute(PC_MOVE, ID);
            }
        }

        /// <summary>
        /// Teach current camcorder position
        /// </summary>
        /// <param name="ID">Position ID to store (0-19 for regular positions, 20 for null position)</param>
        public void teachPC(int ID)
        {
            if (_isPtzMode && _canonPtzController != null)
            {
                int presetNumber = MapRaspiPositionToCanonPreset(ID);
                _PCForm._logDat.sendInfoMessage($"PositionControl\nSpeichere Canon PTZ Preset {presetNumber}...");
                
                Task.Run(async () =>
                {
                    var result = await _canonPtzController.StorePresetAsync(presetNumber);
                    if (result.Success)
                    {
                        _PCForm._logDat.sendInfoMessage($"PositionControl\n✓ Preset {presetNumber} gespeichert");
                    }
                    else
                    {
                        _PCForm._logDat.sendInfoMessage($"PositionControl\n✗ Fehler: {result.Message}");
                    }
                });
            }
            else
            {
                _rasPi.rasPiExecute(PC_TEACH, ID);
            }
        }

        /// <summary>
        /// Teach camcorder position with user feedback
        /// Prompts user to adjust position description after teaching
        /// </summary>
        /// <param name="ID">Position ID to teach (0-20, where 20 is null position)</param>
        public async void teachPos(int ID)
        {
            if (_isPtzMode && _canonPtzController != null)
            {
                int presetNumber = MapRaspiPositionToCanonPreset(ID);
                
                if (!_canonPtzController.IsConnected)
                {
                    System.Windows.Forms.MessageBox.Show("Kamera nicht verbunden!", "Fehler", 
                        System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                    return;
                }
                
                try
                {
                    _PCForm._logDat.sendInfoMessage($"PositionControl\nSpeichere Canon PTZ Preset {presetNumber}...");
                    
                    var storeResult = await _canonPtzController.StorePresetAsync(presetNumber);
                    
                    if (storeResult.Success)
                    {
                        _PCForm._logDat.sendInfoMessage($"PositionControl\n✓ Preset {presetNumber} gespeichert");
                        
                        if (ID < 20)
                        {
                            System.Windows.Forms.DialogResult dialogResult = System.Windows.Forms.MessageBox.Show(
                                "Position ändern?", 
                                "Positionsverwaltung", 
                                System.Windows.Forms.MessageBoxButtons.YesNo);
                            
                            if (dialogResult == System.Windows.Forms.DialogResult.Yes)
                            {
                                _PCForm.listBoxCamPosControl.SelectedIndex = ID;
                                _PCForm.listBoxCamPosControl.Focus();
                            }
                        }
                    }
                    else
                    {
                        _PCForm._logDat.sendInfoMessage($"PositionControl\n✗ Fehler: {storeResult.Message}");
                        System.Windows.Forms.MessageBox.Show($"Fehler:\n{storeResult.Message}", "Fehler", 
                            System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                    }
                }
                catch (Exception ex)
                {
                    _PCForm._logDat.sendInfoMessage($"PositionControl\nFehler beim Speichern: {ex.Message}");
                    System.Windows.Forms.MessageBox.Show($"Fehler:\n{ex.Message}", "Fehler", 
                        System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                }
            }
            else
            {
                // Legacy RasPi-Modus
                _rasPi.rasPiExecute(PC_TEACH, ID);
                if (ID < 20)
                {
                    _PCForm._logDat.sendInfoMessage("PositionControl\nPosition " + ID + " speichern - JA oder Position ändern - NEIN");
                    System.Threading.Thread.Sleep(100);
                    System.Windows.Forms.DialogResult dialogResult = System.Windows.Forms.MessageBox.Show("Position ändern?", "Positionsverwaltung", System.Windows.Forms.MessageBoxButtons.YesNo);
                    if (dialogResult == System.Windows.Forms.DialogResult.Yes)
                    {
                        _PCForm.listBoxCamPosControl.SelectedIndex = ID;
                        _PCForm.listBoxCamPosControl.Focus();
                    }
                }
            }
        }

        /// <summary>
        /// Calibrate magnetometer sensor
        /// Initiates calibration procedure for position sensing
        /// Note: Only available in Raspberry Pi mode, not supported for Canon PTZ
        /// </summary>
        /// <param name="ID">Calibration mode ID (1=magnetometer, 2=gyroscope)</param>
        public void calibratePC(int ID)
        {
            if (_isPtzMode)
            {
                _PCForm._logDat.sendInfoMessage("JokiAutomation\nKalibrierung nicht verfügbar im PTZ-Modus");
                return;
            }
            _rasPi.PuttyRequestRasPi(PC_CALIBRATE, ID);
        }

        // ── PTZ movement tracking ─────────────────────────────────────────────
        private int _activeMoveCount = 0;

        /// <summary>
        /// Indicates whether camera positioning movement is currently in progress.
        /// In PTZ mode uses a reference counter incremented/decremented around
        /// the full <see cref="MoveToPosAsync"/> call.
        /// In RasPi mode uses the RasPi thread status.
        /// </summary>
        public bool IsMoving()
        {
            if (_isPtzMode && _canonPtzController != null)
            {
                return System.Threading.Volatile.Read(ref _activeMoveCount) > 0;
            }

            if (_rasPi?._RasPiThread != null && _rasPi._RasPiThread.IsAlive)
                return true;

            return false;
        }

        /// <summary>
        /// Moves to the specified position asynchronously.
        /// In PTZ mode the returned task resolves only after
        /// <see cref="XcCanonPtzController.RecallPresetAsync"/> finishes (command
        /// accepted by camera).  The camera may still be physically moving after
        /// the task returns because the Canon XC API confirms command acceptance,
        /// not arrival at the target position.
        /// If precise arrival confirmation is required, add a settling delay after
        /// this call (see <see cref="PtzSettlingTimeMs"/>).
        /// </summary>
        public async Task<PositionMoveResult> MoveToPosAsync(
            int id,
            System.Threading.CancellationToken cancellationToken = default)
        {
            if (_isPtzMode)
            {
                if (_canonPtzController == null)
                {
                    string msg = "PTZ-Modus aktiv, aber Canon Controller ist null!";
                    _PCForm._logDat.sendInfoMessage($"PositionControl\n{msg}");
                    return PositionMoveResult.Fail(id, msg);
                }

                if (id < 0)
                {
                    string msg = $"Ungültige Positions-ID: {id}";
                    _PCForm._logDat.sendInfoMessage($"PositionControl\n{msg}");
                    return PositionMoveResult.Fail(id, msg);
                }

                int canonPreset = MapRaspiPositionToCanonPreset(id);
                SharedPresetState.SetLastPreset(canonPreset);

                System.Threading.Interlocked.Increment(ref _activeMoveCount);
                try
                {
                    _PCForm._logDat.sendInfoMessage(
                        $"PositionControl\nPosition requested — ListBox Index={id}, Canon Preset={canonPreset}");

                    var result = await _canonPtzController.RecallPresetAsync(canonPreset);

                    if (result.Success)
                    {
                        _PCForm._logDat.sendInfoMessage(
                            $"PositionControl\nPosition command accepted — Preset {canonPreset}");

                        // Settling time: command accepted but camera is still physically moving.
                        // This is a time-based fallback — not a confirmed arrival.
                        if (PtzSettlingTimeMs > 0)
                        {
                            _PCForm._logDat.sendInfoMessage(
                                $"PositionControl\nWaiting settling time {PtzSettlingTimeMs}ms (estimated, not confirmed arrival)");
                            await Task.Delay(PtzSettlingTimeMs, cancellationToken);
                        }

                        _PCForm._logDat.sendInfoMessage(
                            $"PositionControl\nPosition settling time completed — Preset {canonPreset}");
                        return PositionMoveResult.Ok(id, canonPreset, result.Message);
                    }
                    else
                    {
                        _PCForm._logDat.sendInfoMessage(
                            $"PositionControl\nPosition command failed — Preset {canonPreset}: {result.Message}");
                        return PositionMoveResult.Fail(id, result.Message);
                    }
                }
                catch (System.OperationCanceledException)
                {
                    _PCForm._logDat.sendInfoMessage($"PositionControl\nMoveToPosAsync cancelled — Preset {id}");
                    throw;
                }
                catch (Exception ex)
                {
                    _PCForm._logDat.sendInfoMessage(
                        $"PositionControl\nMoveToPosAsync exception — Preset {id}: {ex.Message}");
                    return PositionMoveResult.Fail(id, ex.Message);
                }
                finally
                {
                    System.Threading.Interlocked.Decrement(ref _activeMoveCount);
                }
            }
            else
            {
                // Legacy RasPi mode — fire and keep existing behaviour
                _PCForm._logDat.sendInfoMessage($"PositionControl\nRasPi-Modus: Sende Position {id} an RaspberryPi");
                _rasPi.rasPiExecute(PC_MOVE, id);
                return PositionMoveResult.Ok(id, id, "RasPi move started");
            }
        }

        /// <summary>
        /// Settling time (ms) added after a Canon PTZ preset command is accepted.
        /// The Canon XC API confirms command acceptance, not physical arrival.
        /// Set to 0 to disable, or configure a suitable value (e.g. 2000–4000 ms)
        /// depending on the maximum expected travel time.
        /// Default: 3000 ms.
        /// </summary>
        public int PtzSettlingTimeMs { get; set; } = 3000;

        /// <summary>
        /// Move camcorder to specified position (legacy synchronous wrapper).
        /// Prefer <see cref="MoveToPosAsync"/> in new code.
        /// </summary>
        /// <param name="ID">Position ID to move to (0-based index)</param>
        public void moveToPos(int ID)
        {
            try
            {
                if (_isPtzMode && _canonPtzController != null)
                {
                    int canonPreset = MapRaspiPositionToCanonPreset(ID);
                    _PCForm._logDat.sendInfoMessage($"PositionControl\nPTZ-Modus: Sende Preset {canonPreset} an Canon Kamera (ListBox Index {ID})");
                    SharedPresetState.SetLastPreset(canonPreset);
                    // Fire-and-forget kept only for legacy callers that cannot await
                    Task.Run(async () =>
                    {
                        System.Threading.Interlocked.Increment(ref _activeMoveCount);
                        try
                        {
                            var result = await _canonPtzController.RecallPresetAsync(canonPreset);
                            if (result.Success)
                                _PCForm._logDat.sendInfoMessage($"PositionControl\nPreset {canonPreset} erfolgreich angefordert (moveToPos legacy)");
                            else
                                _PCForm._logDat.sendInfoMessage($"PositionControl\nFehler Preset {canonPreset}: {result.Message}");
                        }
                        catch (Exception ex)
                        {
                            _PCForm._logDat.sendInfoMessage($"PositionControl\nException Preset {canonPreset}: {ex.Message}");
                        }
                        finally
                        {
                            System.Threading.Interlocked.Decrement(ref _activeMoveCount);
                        }
                    });
                }
                else if (_isPtzMode && _canonPtzController == null)
                {
                    _PCForm._logDat.sendInfoMessage("PositionControl\nFehler: PTZ-Modus aktiv, aber Canon Controller ist null!");
                }
                else
                {
                    _PCForm._logDat.sendInfoMessage($"PositionControl\nRasPi-Modus: Sende Position {ID} an RaspberryPi");
                    _rasPi.rasPiExecute(PC_MOVE, ID);
                }
            }
            catch (Exception ex)
            {
                _PCForm._logDat.sendInfoMessage($"PositionControl\nmoveToPos Exception: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Execute switching sequence for position control with camera view and audio profile
        /// Encodes camera view and audio profile into position ID for coordinated switching
        /// </summary>
        /// <param name="position">Position name to move to</param>
        /// <param name="cam_audio">Two-character code: first char=camera view (A/G/K/L), second char=audio profile (D/G/P/T/B)
        /// Camera views: A=Altar (Canon PTZ Main), G=GoPro, K=Kanzel (Canon PTZ Preacher), L=Laptop
        /// Audio profiles: D=Diashow (slideshow), G=Gottesdienst (worship), P=Predigt (sermon), T=Text, B=Band</param>
        /// <exception cref="ArgumentException">Thrown when position is not found or cam_audio format is invalid</exception>
        public void sequence(string position, string cam_audio)
        {
            try
            {
                int positionID = 0;
                bool positionfound = false;
                char[] cam_audioSet = cam_audio.ToArray();
                for (; positionID < _PCForm.listBoxCamPosControl.Items.Count; positionID++)
                {
                    if (position == _PCForm.listBoxCamPosControl.Items[positionID].ToString())
                    {
                        if (cam_audioSet.Length == 2)
                        {
                            switch (cam_audioSet[0])        // Code view during position movement 
                            {
                                case 'L':                  // Laptop PowerPoint view (0)
                                    positionID |= 0x0000;
                                break;
                                case 'G':                  // GoPro action cam view (1)
                                    positionID |= 0x0100;
                                break;
                                default:
                                case 'A':                  // Camcorder with position control view (2)
                                    positionID |= 0x0200;
                                break;
                                case 'K':                  // Camcorder preacher view (3)
                                    positionID |= 0x0300;
                                break;
                            }
                            switch (cam_audioSet[1])       // Code audio profile
                            {
                                case 'D':                  // Audio profile slideshow (0)
                                    positionID |= 0x0000;
                                break;
                                default:
                                case 'G':                  // Audio profile worship (1)
                                    positionID |= 0x1000;
                                    break;
                                case 'P':                  // Audio profile sermon (2)
                                    positionID |= 0x2000;
                                break;
                                case 'T':                  // Audio profile text (3)
                                    positionID |= 0x3000;
                                break;
                                case 'B':                  // Audio profile band (4)
                                    positionID |= 0x4000;
                                break;

                            }
                        }

                        if (_isPtzMode && _canonPtzController != null)
                        {
                            // In PTZ mode: Only move to position, ignore encoded camera/audio switching
                            // (Camera/Audio switching is handled by Form1.CommandInterpreter)
                            int presetId = positionID & 0xFF; // Use only lower 8 bits (position ID)
                            int canonPreset = MapRaspiPositionToCanonPreset(presetId);
                            SharedPresetState.SetLastPreset(canonPreset);
                            Task.Run(async () => await _canonPtzController.RecallPresetAsync(canonPreset));
                            _PCForm._logDat.sendInfoMessage($"JokiAutomation\nCanon PTZ: Bewege zu Preset {canonPreset} (Position: {position}, ListBox Index: {presetId})");
                        }
                        else
                        {
                            _rasPi.rasPiExecute(PC_SEQUENCE, positionID); // Execute sequence command with encoded data
                        }

                        positionfound = true;
                        break;
                    }

                }
                if (!positionfound)
                {
                    throw new System.ArgumentException(" ", "Position information invalid");
                }
            }
            catch(Exception e)
            {
                _PCForm._logDat.sendInfoMessage("JokiAutomation\nFormat error in command line call PositionControl\n" + e.Message);
            }
        }

        /// <summary>
        /// Handle move button press for manual position control
        /// Allows manual joystick-style movement control
        /// </summary>
        /// <param name="ID">Button ID (1=up, 2=down, 3=left, 4=right, 5=released)</param>
        public void moveButtonPressed(int ID)
        {
            if (_isPtzMode && _canonPtzController != null)
            {
                Task.Run(async () =>
                {
                    switch (ID)
                    {
                        case PC_BUTTON_UP:
                            await _canonPtzController.StartTiltUpAsync();
                            break;
                        case PC_BUTTON_DOWN:
                            await _canonPtzController.StartTiltDownAsync();
                            break;
                        case PC_BUTTON_LEFT:
                            await _canonPtzController.StartPanLeftAsync();
                            break;
                        case PC_BUTTON_RIGHT:
                            await _canonPtzController.StartPanRightAsync();
                            break;
                        case PC_BUTTON_RELEASED:
                            await _canonPtzController.StopAllAsync();
                            break;
                    }
                });
            }
            else
            {
                _rasPi.rasPiExecute(PC_MOVE_BUTTON, ID);
            }
        }

        /// <summary>
        /// Execute position control test program
        /// Moves camcorder through top five positions in list for testing
        /// Note: Only available in Raspberry Pi mode
        /// </summary>
        /// <param name="ID">Test program ID (0=basic test, 1=advanced test with view switching)</param>
        public void testProgram(int ID)
        {
            if (_isPtzMode)
            {
                _PCForm._logDat.sendInfoMessage("JokiAutomation\nTestprogramm nicht verfügbar im PTZ-Modus");
                return;
            }
            _rasPi.rasPiExecute(PC_TEST_PROGRAM, ID);
        }

        /// <summary>
        /// Release all resources used by the PositionControl
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
        public virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    // Dispose managed resources here
                    // RasPi does not implement IDisposable, so no disposal needed
                }

                // Free unmanaged resources here if any

                disposed = true;
            }
        }

        // Private command constants
        private const int PC_MOVE = 40;            // Position control move to position
        private const int PC_TEACH = 41;           // Position control teach position
        private const int PC_CALIBRATE = 42;       // Position control calibration
        private const int PC_MOVE_BUTTON = 43;     // Move button pressed (ID: 1=up, 2=down, 3=left, 4=right, 5=released)
        private const int PC_TEST_PROGRAM = 44;    // Position control test program (moves to top five positions)
        private const int PC_SEQUENCE = 52;        // Position control sequence with camera/audio switching

        // Public button constants
        /// <summary>Move up button pressed</summary>
        public const int PC_BUTTON_UP = 1;
        
        /// <summary>Move down button pressed</summary>
        public const int PC_BUTTON_DOWN = 2;
        
        /// <summary>Move left button pressed</summary>
        public const int PC_BUTTON_LEFT = 3;
        
        /// <summary>Move right button pressed</summary>
        public const int PC_BUTTON_RIGHT = 4;
        
        /// <summary>Move button released</summary>
        public const int PC_BUTTON_RELEASED = 5;

        private static RasPi _rasPi = new RasPi(); // Raspberry Pi functionality
        private ICanonPtzController _canonPtzController; // Canon PTZ camera controller
        private bool _isPtzMode = false; // True = Canon PTZ, False = Raspberry Pi
        private string _JokiAutomationPath =
            Environment.GetEnvironmentVariable("JokiAutomation", EnvironmentVariableTarget.Process) ??
            Environment.GetEnvironmentVariable("JokiAutomation", EnvironmentVariableTarget.User) ??
            Environment.GetEnvironmentVariable("JokiAutomation", EnvironmentVariableTarget.Machine);
        private static Form1 _PCForm;
        private bool disposed = false;
    }
}
