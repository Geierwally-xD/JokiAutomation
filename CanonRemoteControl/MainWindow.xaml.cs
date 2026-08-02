using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CanonRemoteControl
{
    public partial class MainWindow : Window
    {
        private const int WM_HOTKEY = 0x0312;
        private HwndSource _source;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_NOREPEAT = 0x4000;

        // Hotkey IDs
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

        private IntPtr _handle;

        public MainWindow()
        {
            InitializeComponent();

            this.Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var helper = new WindowInteropHelper(this);
            _handle = helper.Handle;
            _source = HwndSource.FromHwnd(_handle);
            _source.AddHook(HwndHook);

            uint ctrlShift = MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT;

            // Registriere ALLE Hotkeys mit Ctrl+Shift (weniger Konflikte als Ctrl+Alt)
            int successCount = 0;
            int failCount = 0;

            if (RegisterHotKey(_handle, HOTKEY_ID_UP, ctrlShift, 0x26)) successCount++; else { failCount++; System.Diagnostics.Debug.WriteLine("UP failed"); }
            if (RegisterHotKey(_handle, HOTKEY_ID_DOWN, ctrlShift, 0x28)) successCount++; else { failCount++; System.Diagnostics.Debug.WriteLine("DOWN failed"); }
            if (RegisterHotKey(_handle, HOTKEY_ID_LEFT, ctrlShift, 0x25)) successCount++; else { failCount++; System.Diagnostics.Debug.WriteLine("LEFT failed"); }
            if (RegisterHotKey(_handle, HOTKEY_ID_RIGHT, ctrlShift, 0x27)) successCount++; else { failCount++; System.Diagnostics.Debug.WriteLine("RIGHT failed"); }
            if (RegisterHotKey(_handle, HOTKEY_ID_ZOOM_IN, ctrlShift, 0xBB)) successCount++; else { failCount++; System.Diagnostics.Debug.WriteLine("ZOOM_IN failed"); }
            if (RegisterHotKey(_handle, HOTKEY_ID_ZOOM_OUT, ctrlShift, 0xBD)) successCount++; else { failCount++; System.Diagnostics.Debug.WriteLine("ZOOM_OUT failed"); }

            if (RegisterHotKey(_handle, HOTKEY_ID_T, ctrlShift, 0x54)) successCount++; else { failCount++; System.Diagnostics.Debug.WriteLine("T failed"); }
            if (RegisterHotKey(_handle, HOTKEY_ID_A, ctrlShift, 0x41)) successCount++; else { failCount++; System.Diagnostics.Debug.WriteLine("A failed"); }
            if (RegisterHotKey(_handle, HOTKEY_ID_K, ctrlShift, 0x4B)) successCount++; else { failCount++; System.Diagnostics.Debug.WriteLine("K failed"); }
            if (RegisterHotKey(_handle, HOTKEY_ID_O, ctrlShift, 0x4F)) successCount++; else { failCount++; System.Diagnostics.Debug.WriteLine("O failed"); }
            if (RegisterHotKey(_handle, HOTKEY_ID_E, ctrlShift, 0x45)) successCount++; else { failCount++; System.Diagnostics.Debug.WriteLine("E failed"); }
            if (RegisterHotKey(_handle, HOTKEY_ID_G, ctrlShift, 0x47)) successCount++; else { failCount++; System.Diagnostics.Debug.WriteLine("G failed"); }
            if (RegisterHotKey(_handle, HOTKEY_ID_N, ctrlShift, 0x4E)) successCount++; else { failCount++; System.Diagnostics.Debug.WriteLine("N failed"); }
            if (RegisterHotKey(_handle, HOTKEY_ID_H, ctrlShift, 0x48)) successCount++; else { failCount++; System.Diagnostics.Debug.WriteLine("H failed"); }

            System.Diagnostics.Debug.WriteLine($"Hotkeys: {successCount} erfolgreich, {failCount} fehlgeschlagen");

            if (failCount > 0)
            {
                MessageBox.Show($"WARNUNG: {failCount} Hotkeys konnten nicht registriert werden!\n\nErfolgreich: {successCount}\nFehlgeschlagen: {failCount}\n\nMöglicherweise sind einige Tastenkombinationen bereits von anderen Programmen belegt.", 
                    "Hotkey-Registrierung", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            // Hole App und StatusOverlay
            var app = (App)Application.Current;
            app.NotifyHotkeysRegistered();
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                System.Diagnostics.Debug.WriteLine($">>> Hotkey empfangen: ID={id}");

                // Zeige kurz welche Taste gedrückt wurde
                string keyName;
                switch (id)
                {
                    case 1: keyName = "Pfeil Oben"; break;
                    case 2: keyName = "Pfeil Unten"; break;
                    case 3: keyName = "Pfeil Links"; break;
                    case 4: keyName = "Pfeil Rechts"; break;
                    case 5: keyName = "Zoom In (+)"; break;
                    case 6: keyName = "Zoom Out (-)"; break;
                    case 7: keyName = "T (Taufstein)"; break;
                    case 8: keyName = "A (Altar)"; break;
                    case 9: keyName = "K (Kanzel)"; break;
                    case 10: keyName = "O (Orgel)"; break;
                    case 11: keyName = "E (Live-Track Einzel)"; break;
                    case 12: keyName = "G (Live-Track Gruppe)"; break;
                    case 13: keyName = "N (Live-Track Aus)"; break;
                    case 14: keyName = "H (Hilfe)"; break;
                    default: keyName = $"Unbekannt ({id})"; break;
                }

                System.Diagnostics.Debug.WriteLine($"    Taste: {keyName}");

                HandleHotkey(id);
                handled = true;
            }
            return IntPtr.Zero;
        }

        private async void HandleHotkey(int id)
        {
            var app = (App)Application.Current;
            await app.HandleHotkey(id);
        }

        private void ShowHelp_Click(object sender, RoutedEventArgs e)
        {
            var helpDialog = new HelpDialog();
            helpDialog.Owner = this;
            helpDialog.ShowDialog();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Unregistriere alle Hotkeys
            for (int i = 1; i <= 14; i++)
            {
                UnregisterHotKey(_handle, i);
            }

            // Verhindere das Schließen, verstecke stattdessen
            e.Cancel = true;
            this.Hide();
        }
    }
}
