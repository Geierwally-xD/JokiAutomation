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

        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_NOREPEAT = 0x4000;

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
            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var helper = new WindowInteropHelper(this);
            _handle = helper.Handle;
            _source = HwndSource.FromHwnd(_handle);
            _source.AddHook(HwndHook);

            uint ctrlShift = MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT;

            var failedHotkeys = new System.Collections.Generic.List<string>();

            // PTZ movement (IDs 1-6) removed - now handled by GlobalKeyboardHook
            // Only register Presets, Tracking and Help
            if (!RegisterHotKey(_handle, HOTKEY_ID_T, ctrlShift, 0x54)) failedHotkeys.Add("Ctrl+Shift+T (Taufstein)");
            if (!RegisterHotKey(_handle, HOTKEY_ID_A, ctrlShift, 0x41)) failedHotkeys.Add("Ctrl+Shift+A (Altar)");
            if (!RegisterHotKey(_handle, HOTKEY_ID_K, ctrlShift, 0x4B)) failedHotkeys.Add("Ctrl+Shift+K (Kanzel)");
            if (!RegisterHotKey(_handle, HOTKEY_ID_O, ctrlShift, 0x4F)) failedHotkeys.Add("Ctrl+Shift+O (Orgel)");
            if (!RegisterHotKey(_handle, HOTKEY_ID_E, ctrlShift, 0x45)) failedHotkeys.Add("Ctrl+Shift+E (Track Einzel)");
            if (!RegisterHotKey(_handle, HOTKEY_ID_G, ctrlShift, 0x47)) failedHotkeys.Add("Ctrl+Shift+G (Track Gruppe)");
            if (!RegisterHotKey(_handle, HOTKEY_ID_N, ctrlShift, 0x4E)) failedHotkeys.Add("Ctrl+Shift+N (Track Aus)");
            if (!RegisterHotKey(_handle, HOTKEY_ID_H, ctrlShift, 0x48)) failedHotkeys.Add("Ctrl+Shift+H (Hilfe)");

            if (failedHotkeys.Count > 0)
            {
                string failedList = string.Join("\n", failedHotkeys);
                MessageBox.Show(
                    $"WARNUNG: {failedHotkeys.Count} Hotkeys konnten nicht registriert werden!\n\n" +
                    $"Fehlgeschlagene Hotkeys:\n{failedList}\n\n" +
                    $"Mögliche Ursachen:\n" +
                    $"- Eine andere Instanz läuft bereits\n" +
                    $"- Eine andere App verwendet diese Tastenkombinationen\n\n" +
                    $"Bitte beenden Sie andere Instanzen oder andere Apps, die diese Tasten verwenden.",
                    "Hotkey-Registrierung fehlgeschlagen",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            else
            {
                var app = (App)Application.Current;
                app.NotifyHotkeysRegistered();
            }
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
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
            var helpDialog = new HelpDialog { Owner = this };
            helpDialog.ShowDialog();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);
            
            // Hotkeys deregistrieren
            UnregisterAllHotkeys();
            
            // Anwendung komplett beenden
            Application.Current.Shutdown();
        }

        private void UnregisterAllHotkeys()
        {
            if (_handle != IntPtr.Zero)
            {
                UnregisterHotKey(_handle, HOTKEY_ID_T);
                UnregisterHotKey(_handle, HOTKEY_ID_A);
                UnregisterHotKey(_handle, HOTKEY_ID_K);
                UnregisterHotKey(_handle, HOTKEY_ID_O);
                UnregisterHotKey(_handle, HOTKEY_ID_E);
                UnregisterHotKey(_handle, HOTKEY_ID_G);
                UnregisterHotKey(_handle, HOTKEY_ID_N);
                UnregisterHotKey(_handle, HOTKEY_ID_H);
            }
        }
    }
}
