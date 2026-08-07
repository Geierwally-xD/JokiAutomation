using CanonPtzCommon;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace CanonRemoteControl
{
    public partial class App : Application
    {
        private MainWindow _mainWindow;
        private StatusOverlay _statusOverlay;
        private ICanonPtzController _controller;
        private GlobalKeyboardHook _keyboardHook;
        private bool _ptzMovementActive;
        private int? _activePtzVirtualKey;

        private const int VK_LEFT = 0x25;
        private const int VK_UP = 0x26;
        private const int VK_RIGHT = 0x27;
        private const int VK_DOWN = 0x28;
        private const int VK_ESCAPE = 0x1B;
        private const int VK_S = 0x53;
        private const int VK_T = 0x54;
        private const int VK_ADD = 0x6B;
        private const int VK_SUBTRACT = 0x6D;
        private const int VK_OEM_PLUS = 0xBB;
        private const int VK_OEM_MINUS = 0xBD;
        private const int VK_SHIFT_LEFT = 0xA0;
        private const int VK_SHIFT_RIGHT = 0xA1;
        private const int VK_CONTROL_LEFT = 0xA2;
        private const int VK_CONTROL_RIGHT = 0xA3;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
            // Registriere Exit-Handler
            this.Exit += OnApplicationExit;
            
            try
            {
                CameraConfig config = LoadCameraConfig();
                _controller = CreateController(config);

                if (e.Args.Length > 0)
                {
                    CommandResult connect = await _controller.ConnectAsync();

                    if (!connect.Success)
                    {
                        Console.WriteLine(connect.ToString());
                        Shutdown(1);
                        return;
                    }

                    int exitCode = await RunCommandLineModeAsync(e.Args);
                    await _controller.DisconnectAsync();
                    Shutdown(exitCode);
                    return;
                }

                await StartGuiModeAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"FEHLER beim Start:\n{ex.Message}\n\n{ex.StackTrace}",
                    "Startfehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                Shutdown(1);
            }
        }

        private CameraConfig LoadCameraConfig()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidatePaths = new[]
            {
                Path.Combine(baseDir, "Network.cfg"),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "JokiAutomation", "Network.cfg")),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "JokiAutomation", "Network.cfg"))
            };

            foreach (string path in candidatePaths)
            {
                if (File.Exists(path))
                {
                    return NetworkCfgReader.LoadCamera(path, "Canon_CRN100");
                }
            }

            throw new FileNotFoundException("Network.cfg nicht gefunden.", candidatePaths[0]);
        }

        private ICanonPtzController CreateController(CameraConfig config)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[App] CreateController called with Protocol={config.Protocol}");
#endif

            if (string.Equals(config.Protocol, "XC", StringComparison.OrdinalIgnoreCase))
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine("[App] Creating XcCanonPtzController");
#endif
                return new XcCanonPtzController(config);
            }

#if DEBUG
            System.Diagnostics.Debug.WriteLine("[App] Creating LegacyAwPtzController (fallback)");
#endif
            return new LegacyAwPtzController(
                config.IpAddress,
                config.Port,
                config.Username,
                config.Password,
                config.UseHttps);
        }

        private async Task StartGuiModeAsync()
        {
            CommandResult connect = await _controller.ConnectAsync();

            _statusOverlay = new StatusOverlay();
            _statusOverlay.Show();

            if (connect.Success)
            {
                _statusOverlay.ShowStatus("BEREIT!\nKamera verbunden", persistent: false);
            }
            else
            {
                _statusOverlay.ShowStatus($"FEHLER:\n{connect.Message}", persistent: true);
            }

            InstallKeyboardHook();

            _mainWindow = new MainWindow();
            _mainWindow.WindowState = WindowState.Minimized;
            _mainWindow.ShowInTaskbar = true;
            _mainWindow.Show();
        }

        private async Task<int> RunCommandLineModeAsync(string[] args)
        {
            string command = args[0].ToLowerInvariant();
            CommandResult result;

            switch (command)
            {
                case "exit":
                case "quit":
                case "shutdown":
                    return await ShutdownRunningInstanceAsync();
                    
                case "altar":
                    result = await _controller.RecallPresetAsync(1);
                    break;
                case "taufstein":
                    result = await _controller.RecallPresetAsync(2);
                    break;
                case "kanzel":
                    result = await _controller.RecallPresetAsync(3);
                    break;
                case "orgel":
                    result = await _controller.RecallPresetAsync(4);
                    break;
                case "track_single":
                case "track_einzelperson":
                    result = await _controller.EnableTrackingSingleAsync();
                    break;
                case "track_group":
                case "track_gruppe":
                    result = await _controller.EnableTrackingGroupAsync();
                    break;
                case "track_off":
                case "track_aus":
                    result = await _controller.DisableTrackingAsync();
                    break;
                case "help":
                case "-h":
                case "--help":
                    ShowHelp();
                    return 0;
                default:
                    Console.WriteLine($"FEHLER: Unbekanntes Kommando '{command}'");
                    ShowHelp();
                    return 1;
            }

            Console.WriteLine(result.ToString());
            return result.Success ? 0 : 1;
        }

        private async Task<int> ShutdownRunningInstanceAsync()
        {
            try
            {
                var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
                var processes = System.Diagnostics.Process.GetProcessesByName(currentProcess.ProcessName);

                foreach (var process in processes)
                {
                    if (process.Id != currentProcess.Id)
                    {
                        Console.WriteLine($"Beende Prozess {process.Id}...");
                        process.Kill();
                        process.WaitForExit(5000);
                        Console.WriteLine("Prozess erfolgreich beendet.");
                        return 0;
                    }
                }

                Console.WriteLine("Keine laufende Instanz gefunden.");
                return 1;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FEHLER beim Beenden: {ex.Message}");
                return 1;
            }
        }

        private void ShowHelp()
        {
            Console.WriteLine("CanonRemoteControl - Canon CR-N100 Fernsteuerung");
            Console.WriteLine();
            Console.WriteLine("VERWENDUNG:");
            Console.WriteLine("  CanonRemoteControl.exe [Kommando]");
            Console.WriteLine();
            Console.WriteLine("KOMMANDOS:");
            Console.WriteLine("  altar              - Altar-Position");
            Console.WriteLine("  taufstein          - Taufstein-Position");
            Console.WriteLine("  kanzel             - Kanzel-Position");
            Console.WriteLine("  orgel              - Orgel-Position");
            Console.WriteLine("  track_single       - Tracking Einzelperson aktivieren");
            Console.WriteLine("  track_group        - Tracking Gruppe aktivieren");
            Console.WriteLine("  track_off          - Tracking deaktivieren");
            Console.WriteLine("  exit/quit/shutdown - Beendet laufende Instanz");
            Console.WriteLine("  help               - Diese Hilfe anzeigen");
        }

        public void NotifyHotkeysRegistered()
        {
            _statusOverlay?.ShowStatus("BEREIT!\n14 Tastaturkuerzel aktiv\nCtrl+Shift+H = Hilfe", persistent: false);
        }

        public async Task HandleHotkey(int id)
        {
            if (_controller == null)
            {
                _statusOverlay?.ShowStatus("FEHLER: Kamera-Controller nicht initialisiert", persistent: false);
                return;
            }

            CommandResult result = await ExecuteHotkeyAsync(id);

            if (result == null)
            {
                return;
            }

            if (result.Success)
            {
                _statusOverlay?.ShowStatus(result.Message, persistent: false);
            }
            else
            {
                _statusOverlay?.ShowStatus($"FEHLER:\n{result.Message}", persistent: false);
            }
        }

        private Task<CommandResult> ExecuteHotkeyAsync(int id)
        {
            switch (id)
            {
                // IDs 1 bis 6 waren früher PTZ.
                // PTZ wird jetzt ausschließlich über GlobalKeyboardHook KeyDown/KeyUp gesteuert.
                case 1:
                case 2:
                case 3:
                case 4:
                case 5:
                case 6:
                    return Task.FromResult(CommandResult.Ok("PTZ", "PTZ wird über KeyboardHook gesteuert"));

                case 7:  // T-Taste → Taufstein (Preset 2)
                    SharedPresetState.SetLastPreset(2);
                    return _controller.RecallPresetAsync(2);
                case 8:  // A-Taste → Altar (Preset 1)
                    SharedPresetState.SetLastPreset(1);
                    return _controller.RecallPresetAsync(1);
                case 9:  // K-Taste → Kanzel (Preset 3)
                    SharedPresetState.SetLastPreset(3);
                    return _controller.RecallPresetAsync(3);
                case 10: // O-Taste → Orgel (Preset 4)
                    SharedPresetState.SetLastPreset(4);
                    return _controller.RecallPresetAsync(4);
                case 11:
                    return _controller.EnableTrackingSingleAsync();
                case 12:
                    return _controller.EnableTrackingGroupAsync();
                case 13:
                    return _controller.DisableTrackingAsync();
                case 14:
                    _mainWindow?.Dispatcher.Invoke(() =>
                    {
                        var helpDialog = new HelpDialog
                        {
                            WindowStartupLocation = WindowStartupLocation.CenterScreen,
                            Topmost = true
                        };
                        helpDialog.ShowDialog();
                    });
                    return Task.FromResult<CommandResult>(null);
                default:
                    return Task.FromResult(CommandResult.Fail("Hotkey", $"Unbekannte Hotkey-ID: {id}"));
            }
        }

        private void InstallKeyboardHook()
        {
            if (_keyboardHook != null)
            {
                return;
            }

            _keyboardHook = new GlobalKeyboardHook();
            _keyboardHook.KeyDown += KeyboardHook_KeyDown;
            _keyboardHook.KeyUp += KeyboardHook_KeyUp;
            _keyboardHook.Install();
        }

        private async void KeyboardHook_KeyDown(object sender, KeyboardHookEventArgs e)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[App] KeyDown: VKey={e.VirtualKeyCode:X2}, Ctrl={e.CtrlPressed}, Shift={e.ShiftPressed}");
#endif

            if (_controller == null)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine("[App] KeyDown ignored: controller is null");
#endif
                return;
            }

            bool isModifierCombination = e.CtrlPressed && e.ShiftPressed;

            if (!isModifierCombination)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine("[App] KeyDown ignored: no Ctrl+Shift modifier");
#endif
                return;
            }

            if (e.VirtualKeyCode == VK_ESCAPE)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine("[App] KeyDown: ESC pressed, stopping movement");
#endif
                await StopMovementAsync("EmergencyStop");
                return;
            }

            if (e.VirtualKeyCode == VK_S)
            {
                int lastPreset = SharedPresetState.GetLastPreset();
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[App] KeyDown: S pressed, teaching preset {lastPreset}");
#endif
                await TeachLastPresetAsync();
                return;
            }

            if (!IsPtzKey(e.VirtualKeyCode))
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine("[App] KeyDown ignored: not a PTZ key");
#endif
                return;
            }

            if (_ptzMovementActive)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine("[App] KeyDown ignored: PTZ movement already active");
#endif
                return;
            }

#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[App] Starting PTZ movement for key {e.VirtualKeyCode:X2}");
#endif
            _ptzMovementActive = true;
            _activePtzVirtualKey = e.VirtualKeyCode;

#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[App] About to call StartMovementForKeyAsync, _controller type: {_controller?.GetType().Name}");
#endif
            CommandResult result = await StartMovementForKeyAsync(e.VirtualKeyCode);
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[App] StartMovementForKeyAsync returned, result is null: {result == null}, Success: {result?.Success}");
#endif

            if (result == null)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine("[App] Result is null, returning");
#endif
                return;
            }

            if (result.Success)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[App] Success: {result.Message}");
#endif
                _statusOverlay?.ShowStatus(result.Message, persistent: true);
            }
            else
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[App] Failure: {result.Message}");
#endif
                _statusOverlay?.ShowStatus($"FEHLER:\n{result.Message}", persistent: false);
                _ptzMovementActive = false;
                _activePtzVirtualKey = null;
            }
        }

        private async void KeyboardHook_KeyUp(object sender, KeyboardHookEventArgs e)
        {
            if (!_ptzMovementActive)
            {
                return;
            }

            bool modifierReleased =
                e.VirtualKeyCode == VK_SHIFT_LEFT ||
                e.VirtualKeyCode == VK_SHIFT_RIGHT ||
                e.VirtualKeyCode == VK_CONTROL_LEFT ||
                e.VirtualKeyCode == VK_CONTROL_RIGHT;

            bool activePtzKeyReleased =
                _activePtzVirtualKey != null &&
                e.VirtualKeyCode == _activePtzVirtualKey.Value;

            if (!modifierReleased && !activePtzKeyReleased)
            {
                return;
            }

            await StopMovementAsync("KeyUpStop");
        }

        private static bool IsPtzKey(int virtualKeyCode)
        {
            return virtualKeyCode == VK_LEFT
                || virtualKeyCode == VK_RIGHT
                || virtualKeyCode == VK_UP
                || virtualKeyCode == VK_DOWN
                || virtualKeyCode == VK_ADD
                || virtualKeyCode == VK_SUBTRACT
                || virtualKeyCode == VK_OEM_PLUS
                || virtualKeyCode == VK_OEM_MINUS;
        }

        private Task<CommandResult> StartMovementForKeyAsync(int virtualKeyCode)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[App] StartMovementForKeyAsync: VKey={virtualKeyCode:X2}");
#endif

            switch (virtualKeyCode)
            {
                case VK_LEFT:
#if DEBUG
                    System.Diagnostics.Debug.WriteLine("[App] Calling StartPanLeftAsync");
#endif
                    return _controller.StartPanLeftAsync();

                case VK_RIGHT:
#if DEBUG
                    System.Diagnostics.Debug.WriteLine("[App] Calling StartPanRightAsync");
#endif
                    return _controller.StartPanRightAsync();

                case VK_UP:
#if DEBUG
                    System.Diagnostics.Debug.WriteLine("[App] Calling StartTiltUpAsync");
#endif
                    return _controller.StartTiltUpAsync();

                case VK_DOWN:
#if DEBUG
                    System.Diagnostics.Debug.WriteLine("[App] Calling StartTiltDownAsync");
#endif
                    return _controller.StartTiltDownAsync();

                case VK_ADD:
                case VK_OEM_PLUS:
#if DEBUG
                    System.Diagnostics.Debug.WriteLine("[App] Calling StartZoomInAsync");
#endif
                    return _controller.StartZoomInAsync();

                case VK_SUBTRACT:
                case VK_OEM_MINUS:
#if DEBUG
                    System.Diagnostics.Debug.WriteLine("[App] Calling StartZoomOutAsync");
#endif
                    return _controller.StartZoomOutAsync();

                default:
#if DEBUG
                    System.Diagnostics.Debug.WriteLine($"[App] Unknown PTZ key: {virtualKeyCode:X2}");
#endif
                    return Task.FromResult(CommandResult.Fail("PTZ", $"Nicht unterstützte PTZ-Taste: {virtualKeyCode}"));
            }
        }

        private async Task StopMovementAsync(string reason)
        {
            if (_controller == null)
            {
                _ptzMovementActive = false;
                _activePtzVirtualKey = null;
                return;
            }

            CommandResult result = await _controller.StopAllAsync();

            _ptzMovementActive = false;
            _activePtzVirtualKey = null;

            if (result.Success)
            {
                _statusOverlay?.ShowStatus("PTZ gestoppt", persistent: false);
            }
            else
            {
                _statusOverlay?.ShowStatus($"FEHLER beim Stop:\n{result.Message}", persistent: false);
            }
        }

        private async Task TeachLastPresetAsync()
        {
            if (_controller == null)
            {
                return;
            }

            int lastPreset = SharedPresetState.GetLastPreset();
            _statusOverlay?.ShowStatus($"Speichere aktuelle Position als Preset {lastPreset}...", persistent: true);

            CommandResult result = await _controller.StorePresetAsync(lastPreset);

            if (result.Success)
            {
                _statusOverlay?.ShowStatus($"? Preset {lastPreset} gespeichert", persistent: false);
            }
            else
            {
                _statusOverlay?.ShowStatus($"FEHLER beim Speichern:\n{result.Message}", persistent: false);
            }
        }

        private async void OnApplicationExit(object sender, ExitEventArgs e)
        {
            try
            {
                // Cleanup: Disconnect PTZ controller
                if (_controller != null)
                {
                    await _controller.DisconnectAsync();
                }
                
                // Cleanup: Unregister keyboard hook
                if (_keyboardHook != null)
                {
                    _keyboardHook.Dispose();
                    _keyboardHook = null;
                }
                
                // Cleanup: Close status overlay
                if (_statusOverlay != null)
                {
                    _statusOverlay.Close();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during cleanup: {ex.Message}");
            }
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            try
            {
                if (_keyboardHook != null)
                {
                    _keyboardHook.KeyDown -= KeyboardHook_KeyDown;
                    _keyboardHook.KeyUp -= KeyboardHook_KeyUp;
                    _keyboardHook.Dispose();
                    _keyboardHook = null;
                }

                if (_controller != null)
                {
                    await _controller.StopAllAsync();
                    await _controller.DisconnectAsync();
                }
            }
            catch
            {
                // Best effort shutdown.
            }

            base.OnExit(e);
        }
    }
}
