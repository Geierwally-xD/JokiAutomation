using System;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace CanonRemoteControl
{
    public partial class App : Application
    {
        private MainWindow _mainWindow;
        private StatusOverlay _statusOverlay;
        private CanonCrn100Controller _controller;

        // Kamera-Konfiguration - aus Network.cfg übernommen
        private const string CAMERA_IP = "192.168.178.120";
        private const string CAMERA_USER = "admin";
        private const string CAMERA_PASSWORD = "passwort";

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            try
            {
                // Prüfe ob Kommandozeilen-Modus
                if (e.Args.Length > 0)
                {
                    // Kommandozeilen-Modus
                    RunCommandLineMode(e.Args);
                    Shutdown();
                    return;
                }

                // GUI-Modus
                // Initialisiere Controller
                _controller = new CanonCrn100Controller(CAMERA_IP, CAMERA_USER, CAMERA_PASSWORD);

                // Erstelle und zeige StatusOverlay
                _statusOverlay = new StatusOverlay();
                _statusOverlay.Show();

                // Erstelle MainWindow (bleibt sichtbar für Hotkeys)
                _mainWindow = new MainWindow();

                // Starte immer minimiert (nur StatusOverlay sichtbar)
                _mainWindow.WindowState = WindowState.Minimized;
                _mainWindow.ShowInTaskbar = true;

                _mainWindow.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"FEHLER beim Start:\n{ex.Message}\n\n{ex.StackTrace}", 
                    "Startfehler", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        private async void RunCommandLineMode(string[] args)
        {
            try
            {
                string command = args[0].ToLower();

                Console.WriteLine($"CanonRemoteControl - Kommandozeilen-Modus");
                Console.WriteLine($"Kommando: {command}");
                Console.WriteLine($"Kamera: {CAMERA_IP}");
                Console.WriteLine();

                var controller = new CanonCrn100Controller(CAMERA_IP, CAMERA_USER, CAMERA_PASSWORD);
                bool success = false;

                switch (command)
                {
                    case "altar":
                        Console.WriteLine("Rufe Preset: Altar (Position 2)...");
                        success = await controller.RecallAltar();
                        break;

                    case "taufstein":
                        Console.WriteLine("Rufe Preset: Taufstein (Position 1)...");
                        success = await controller.RecallTaufstein();
                        break;

                    case "kanzel":
                        Console.WriteLine("Rufe Preset: Kanzel (Position 3)...");
                        success = await controller.RecallKanzel();
                        break;

                    case "orgel":
                        Console.WriteLine("Rufe Preset: Orgel (Position 4)...");
                        success = await controller.RecallOrgel();
                        break;

                    case "track_single":
                    case "track_einzelperson":
                        Console.WriteLine("Aktiviere Live-Tracking: Einzelperson...");
                        success = await controller.EnableLiveTrackSingle();
                        break;

                    case "track_group":
                    case "track_gruppe":
                        Console.WriteLine("Aktiviere Live-Tracking: Gruppe...");
                        success = await controller.EnableLiveTrackGroup();
                        break;

                    case "track_off":
                    case "track_aus":
                        Console.WriteLine("Deaktiviere Live-Tracking...");
                        success = await controller.DisableLiveTrack();
                        break;

                    case "help":
                    case "--help":
                    case "-h":
                        ShowHelp();
                        return;

                    default:
                        Console.WriteLine($"FEHLER: Unbekanntes Kommando '{command}'");
                        Console.WriteLine();
                        ShowHelp();
                        Environment.Exit(1);
                        return;
                }

                if (success)
                {
                    Console.WriteLine("? Erfolgreich");
                    Environment.Exit(0);
                }
                else
                {
                    Console.WriteLine("? FEHLER: Kamera nicht erreichbar oder Befehl fehlgeschlagen");
                    Environment.Exit(1);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FEHLER: {ex.Message}");
                Environment.Exit(1);
            }
        }

        private void ShowHelp()
        {
            Console.WriteLine("CanonRemoteControl - Canon CRN-100 Fernsteuerung");
            Console.WriteLine();
            Console.WriteLine("VERWENDUNG:");
            Console.WriteLine("  CanonRemoteControl.exe [Kommando]");
            Console.WriteLine();
            Console.WriteLine("KOMMANDOS:");
            Console.WriteLine("  altar              - Altar-Position (Preset 2)");
            Console.WriteLine("  taufstein          - Taufstein-Position (Preset 1)");
            Console.WriteLine("  kanzel             - Kanzel-Position (Preset 3)");
            Console.WriteLine("  orgel              - Orgel-Position (Preset 4)");
            Console.WriteLine();
            Console.WriteLine("  track_single       - Live-Tracking Einzelperson ein");
            Console.WriteLine("  track_group        - Live-Tracking Gruppe ein");
            Console.WriteLine("  track_off          - Live-Tracking aus");
            Console.WriteLine();
            Console.WriteLine("  help, -h, --help   - Diese Hilfe anzeigen");
            Console.WriteLine();
            Console.WriteLine("BEISPIELE:");
            Console.WriteLine("  CanonRemoteControl.exe altar");
            Console.WriteLine("  CanonRemoteControl.exe track_single");
            Console.WriteLine();
            Console.WriteLine("GUI-MODUS:");
            Console.WriteLine("  Ohne Argumente startet die GUI mit Tastaturkürzeln (Ctrl+Shift+...)");
        }

        public void NotifyHotkeysRegistered()
        {
            _statusOverlay?.ShowStatus("BEREIT!\n14 Tastaturkuerzel aktiv\nCtrl+Shift+H = Hilfe", persistent: false);
        }

        public async Task HandleHotkey(int id)
        {
            bool success = false;
            string statusMessage = "";

            try
            {
                switch (id)
                {
                    case 1: // UP
                        success = await _controller.PanTiltUp();
                        statusMessage = "Kamera nach oben";
                        break;
                    case 2: // DOWN
                        success = await _controller.PanTiltDown();
                        statusMessage = "Kamera nach unten";
                        break;
                    case 3: // LEFT
                        success = await _controller.PanTiltLeft();
                        statusMessage = "Kamera nach links";
                        break;
                    case 4: // RIGHT
                        success = await _controller.PanTiltRight();
                        statusMessage = "Kamera nach rechts";
                        break;
                    case 5: // ZOOM_IN
                        success = await _controller.ZoomIn();
                        statusMessage = "Zoom hinein";
                        break;
                    case 6: // ZOOM_OUT
                        success = await _controller.ZoomOut();
                        statusMessage = "Zoom heraus";
                        break;
                    case 7: // T
                        success = await _controller.RecallTaufstein();
                        statusMessage = "Position: Taufstein";
                        break;
                    case 8: // A
                        success = await _controller.RecallAltar();
                        statusMessage = "Position: Altar";
                        break;
                    case 9: // K
                        success = await _controller.RecallKanzel();
                        statusMessage = "Position: Kanzel";
                        break;
                    case 10: // O
                        success = await _controller.RecallOrgel();
                        statusMessage = "Position: Orgel";
                        break;
                    case 11: // E
                        success = await _controller.EnableLiveTrackSingle();
                        if (success)
                        {
                            _statusOverlay.ShowStatus("Live-Tracking Einzelperson aktiv\nZum Beenden: Ctrl+Alt+N", persistent: true);
                        }
                        else
                        {
                            _statusOverlay.ShowStatus("FEHLER: Kamera nicht erreichbar!\nLive-Tracking konnte nicht gestartet werden", persistent: false);
                        }
                        return;
                    case 12: // G
                        success = await _controller.EnableLiveTrackGroup();
                        if (success)
                        {
                            _statusOverlay.ShowStatus("Live-Tracking Gruppe aktiv\nZum Beenden: Ctrl+Alt+N", persistent: true);
                        }
                        else
                        {
                            _statusOverlay.ShowStatus("FEHLER: Kamera nicht erreichbar!\nLive-Tracking konnte nicht gestartet werden", persistent: false);
                        }
                        return;
                    case 13: // N
                        success = await _controller.DisableLiveTrack();
                        _statusOverlay.ShowStatus("Live-Tracking deaktiviert", persistent: false);
                        return;
                    case 14: // H
                        _mainWindow.Dispatcher.Invoke(() =>
                        {
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
                        _statusOverlay.ShowStatus(statusMessage, persistent: false);
                    }
                    else
                    {
                        _statusOverlay.ShowStatus($"FEHLER: Kamera nicht erreichbar!\n{statusMessage}", persistent: false);
                    }
                }
            }
            catch (Exception ex)
            {
                _statusOverlay.ShowStatus($"FEHLER: {ex.Message}", persistent: false);
            }
        }
    }
}
