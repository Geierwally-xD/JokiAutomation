using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace CanonRemoteControl
{
    public class GlobalHotKeyManager
    {
        private const int WM_HOTKEY = 0x0312;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private enum KeyModifier
        {
            None = 0,
            Alt = 1,
            Control = 2,
            Shift = 4,
            WinKey = 8,
            NoRepeat = 0x4000
        }

        private readonly Window _window;
        private readonly CanonCrn100Controller _controller;
        private readonly StatusOverlay _statusOverlay;
        private IntPtr _windowHandle;
        private HwndSource _source;

        private const int HOTKEY_ID_UP = 1;
        private const int HOTKEY_ID_DOWN = 2;
        private const int HOTKEY_ID_LEFT = 3;
        private const int HOTKEY_ID_RIGHT = 4;
        private const int HOTKEY_ID_ZOOM_IN = 5;
        private const int HOTKEY_ID_ZOOM_OUT = 6;
        private const int HOTKEY_ID_T = 7;
        private const int HOTKEY_ID_A = 8;
        private const int HOTKEY_ID_K = 9;
        private const int HOTKEY_ID_O = 10;
        private const int HOTKEY_ID_E = 11;
        private const int HOTKEY_ID_G = 12;
        private const int HOTKEY_ID_N = 13;
        private const int HOTKEY_ID_H = 14;

        public GlobalHotKeyManager(Window window, CanonCrn100Controller controller, StatusOverlay statusOverlay)
        {
            _window = window;
            _controller = controller;
            _statusOverlay = statusOverlay;
        }

        public void RegisterAllHotKeys()
        {
            var helper = new WindowInteropHelper(_window);
            _windowHandle = helper.Handle;

            if (_windowHandle == IntPtr.Zero)
            {
                _statusOverlay.ShowStatus("FEHLER: Window Handle ist null!", persistent: true);
                return;
            }

            _source = HwndSource.FromHwnd(_windowHandle);
            _source.AddHook(HwndHook);
            System.Diagnostics.Debug.WriteLine("HwndHook wurde hinzugefügt");

            uint ctrlAlt = (uint)(KeyModifier.Control | KeyModifier.Alt | KeyModifier.NoRepeat);

            int registeredCount = 0;
            int failedCount = 0;

            System.Diagnostics.Debug.WriteLine("Beginne Hotkey-Registrierung...");

            if (RegisterHotKey(_windowHandle, HOTKEY_ID_UP, ctrlAlt, (uint)Key.Up)) { registeredCount++; System.Diagnostics.Debug.WriteLine("  UP: OK"); } else { failedCount++; System.Diagnostics.Debug.WriteLine("  UP: FEHLER"); }
            if (RegisterHotKey(_windowHandle, HOTKEY_ID_DOWN, ctrlAlt, (uint)Key.Down)) { registeredCount++; System.Diagnostics.Debug.WriteLine("  DOWN: OK"); } else { failedCount++; System.Diagnostics.Debug.WriteLine("  DOWN: FEHLER"); }
            if (RegisterHotKey(_windowHandle, HOTKEY_ID_LEFT, ctrlAlt, (uint)Key.Left)) { registeredCount++; System.Diagnostics.Debug.WriteLine("  LEFT: OK"); } else { failedCount++; System.Diagnostics.Debug.WriteLine("  LEFT: FEHLER"); }
            if (RegisterHotKey(_windowHandle, HOTKEY_ID_RIGHT, ctrlAlt, (uint)Key.Right)) { registeredCount++; System.Diagnostics.Debug.WriteLine("  RIGHT: OK"); } else { failedCount++; System.Diagnostics.Debug.WriteLine("  RIGHT: FEHLER"); }
            if (RegisterHotKey(_windowHandle, HOTKEY_ID_ZOOM_IN, ctrlAlt, (uint)Key.OemPlus)) { registeredCount++; System.Diagnostics.Debug.WriteLine("  ZOOM_IN: OK"); } else { failedCount++; System.Diagnostics.Debug.WriteLine("  ZOOM_IN: FEHLER"); }
            if (RegisterHotKey(_windowHandle, HOTKEY_ID_ZOOM_OUT, ctrlAlt, (uint)Key.OemMinus)) { registeredCount++; System.Diagnostics.Debug.WriteLine("  ZOOM_OUT: OK"); } else { failedCount++; System.Diagnostics.Debug.WriteLine("  ZOOM_OUT: FEHLER"); }

            if (RegisterHotKey(_windowHandle, HOTKEY_ID_T, ctrlAlt, (uint)Key.T)) { registeredCount++; System.Diagnostics.Debug.WriteLine("  T: OK"); } else { failedCount++; System.Diagnostics.Debug.WriteLine("  T: FEHLER"); }
            if (RegisterHotKey(_windowHandle, HOTKEY_ID_A, ctrlAlt, (uint)Key.A)) { registeredCount++; System.Diagnostics.Debug.WriteLine("  A: OK"); } else { failedCount++; System.Diagnostics.Debug.WriteLine("  A: FEHLER"); }
            if (RegisterHotKey(_windowHandle, HOTKEY_ID_K, ctrlAlt, (uint)Key.K)) { registeredCount++; System.Diagnostics.Debug.WriteLine("  K: OK"); } else { failedCount++; System.Diagnostics.Debug.WriteLine("  K: FEHLER"); }
            if (RegisterHotKey(_windowHandle, HOTKEY_ID_O, ctrlAlt, (uint)Key.O)) { registeredCount++; System.Diagnostics.Debug.WriteLine("  O: OK"); } else { failedCount++; System.Diagnostics.Debug.WriteLine("  O: FEHLER"); }
            if (RegisterHotKey(_windowHandle, HOTKEY_ID_E, ctrlAlt, (uint)Key.E)) { registeredCount++; System.Diagnostics.Debug.WriteLine("  E: OK"); } else { failedCount++; System.Diagnostics.Debug.WriteLine("  E: FEHLER"); }
            if (RegisterHotKey(_windowHandle, HOTKEY_ID_G, ctrlAlt, (uint)Key.G)) { registeredCount++; System.Diagnostics.Debug.WriteLine("  G: OK"); } else { failedCount++; System.Diagnostics.Debug.WriteLine("  G: FEHLER"); }
            if (RegisterHotKey(_windowHandle, HOTKEY_ID_N, ctrlAlt, (uint)Key.N)) { registeredCount++; System.Diagnostics.Debug.WriteLine("  N: OK"); } else { failedCount++; System.Diagnostics.Debug.WriteLine("  N: FEHLER"); }
            if (RegisterHotKey(_windowHandle, HOTKEY_ID_H, ctrlAlt, (uint)Key.H)) { registeredCount++; System.Diagnostics.Debug.WriteLine("  H: OK"); } else { failedCount++; System.Diagnostics.Debug.WriteLine("  H: FEHLER"); }

            System.Diagnostics.Debug.WriteLine($"==> Ergebnis: {registeredCount} erfolgreich, {failedCount} fehlgeschlagen");

            // Zeige Statusmeldung ob Hotkeys erfolgreich registriert wurden
            if (failedCount == 0)
            {
                System.Diagnostics.Debug.WriteLine("Alle Hotkeys erfolgreich registriert");
                System.Windows.MessageBox.Show($"ERFOLG!\nAlle {registeredCount} Hotkeys wurden registriert!\n\nTesten Sie: Ctrl+Alt+T", 
                    "Hotkey-Registrierung", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                _statusOverlay.ShowStatus($"BEREIT!\n{registeredCount} Tastaturkuerzel aktiv\nCtrl+Alt+H = Hilfe\nCtrl+Alt+T = Test", persistent: false);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"FEHLER: {failedCount} Hotkeys konnten nicht registriert werden");
                System.Windows.MessageBox.Show($"WARNUNG!\nNur {registeredCount} von 14 Hotkeys wurden registriert.\n{failedCount} sind fehlgeschlagen!", 
                    "Hotkey-Registrierung", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                _statusOverlay.ShowStatus($"WARNUNG!\nNur {registeredCount} von 14 Hotkeys aktiv\n{failedCount} fehlgeschlagen", persistent: true);
            }
        }

        public void UnregisterAllHotKeys()
        {
            for (int i = 1; i <= 14; i++)
            {
                UnregisterHotKey(_windowHandle, i);
            }

            _source?.RemoveHook(HwndHook);
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                System.Diagnostics.Debug.WriteLine($"*** HOTKEY EMPFANGEN: ID={id} ***");
                _statusOverlay.ShowStatus($"Hotkey gedrueckt: ID={id}\nVerarbeite Befehl...", persistent: false);
                HandleHotKeyWithErrorHandling(id);
                handled = true;
            }
            return IntPtr.Zero;
        }

        private async void HandleHotKey(int id)
        {
            bool success = false;
            string statusMessage = "";

            switch (id)
            {
                case HOTKEY_ID_UP:
                    success = await _controller.PanTiltUp();
                    statusMessage = "Kamera nach oben";
                    break;
                case HOTKEY_ID_DOWN:
                    success = await _controller.PanTiltDown();
                    statusMessage = "Kamera nach unten";
                    break;
                case HOTKEY_ID_LEFT:
                    success = await _controller.PanTiltLeft();
                    statusMessage = "Kamera nach links";
                    break;
                case HOTKEY_ID_RIGHT:
                    success = await _controller.PanTiltRight();
                    statusMessage = "Kamera nach rechts";
                    break;
                case HOTKEY_ID_ZOOM_IN:
                    success = await _controller.ZoomIn();
                    statusMessage = "Zoom hinein";
                    break;
                case HOTKEY_ID_ZOOM_OUT:
                    success = await _controller.ZoomOut();
                    statusMessage = "Zoom heraus";
                    break;
                case HOTKEY_ID_T:
                    success = await _controller.RecallTaufstein();
                    statusMessage = "Position: Taufstein";
                    break;
                case HOTKEY_ID_A:
                    success = await _controller.RecallAltar();
                    statusMessage = "Position: Altar";
                    break;
                case HOTKEY_ID_K:
                    success = await _controller.RecallKanzel();
                    statusMessage = "Position: Kanzel";
                    break;
                case HOTKEY_ID_O:
                    success = await _controller.RecallOrgel();
                    statusMessage = "Position: Orgel";
                    break;
                case HOTKEY_ID_E:
                    success = await _controller.EnableLiveTrackSingle();
                    statusMessage = "Live-Tracking Einzelperson aktiv\nZum Beenden: Ctrl+Alt+N";
                    if (success)
                    {
                        _statusOverlay.ShowStatus(statusMessage, persistent: true);
                    }
                    else
                    {
                        _statusOverlay.ShowStatus("FEHLER: Kamera nicht erreichbar!\nLive-Tracking konnte nicht gestartet werden", persistent: false);
                    }
                    return;
                case HOTKEY_ID_G:
                    success = await _controller.EnableLiveTrackGroup();
                    statusMessage = "Live-Tracking Gruppe aktiv\nZum Beenden: Ctrl+Alt+N";
                    if (success)
                    {
                        _statusOverlay.ShowStatus(statusMessage, persistent: true);
                    }
                    else
                    {
                        _statusOverlay.ShowStatus("FEHLER: Kamera nicht erreichbar!\nLive-Tracking konnte nicht gestartet werden", persistent: false);
                    }
                    return;
                case HOTKEY_ID_N:
                    success = await _controller.DisableLiveTrack();
                    statusMessage = "Live-Tracking deaktiviert";
                    _statusOverlay.HideStatus();
                    break;
                case HOTKEY_ID_H:
                    _window.Dispatcher.Invoke(() =>
                    {
                        // Zeige das Hauptfenster nicht an, öffne nur den Dialog
                        var helpDialog = new HelpDialog();
                        helpDialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                        helpDialog.Topmost = true;
                        helpDialog.ShowDialog();
                    });
                    return;
            }

            if (!string.IsNullOrEmpty(statusMessage))
            {
                if (success)
                {
                    _statusOverlay.ShowStatus(statusMessage);
                }
                else
                {
                    _statusOverlay.ShowStatus("FEHLER: Kamera nicht erreichbar!\n" + statusMessage, persistent: false);
                }
            }
        }

        private void HandleHotKeyWithErrorHandling(int hotkeyId)
        {
            try
            {
                HandleHotKey(hotkeyId);
            }
            catch (Exception ex)
            {
                _statusOverlay.ShowStatus($"FEHLER: {ex.Message}", persistent: false);
            }
        }
    }
}
