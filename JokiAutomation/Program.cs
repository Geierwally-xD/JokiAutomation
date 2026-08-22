using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using System.Runtime.InteropServices;

namespace JokiAutomation
{
    static class Program
    {
        private static Form1 JA;
        
        [STAThread]
        static void Main(string[] args)
        {
            Mutex singleInstanceMutex = null;
            bool mutexAcquired = false;

            try
            {
                singleInstanceMutex = new Mutex(false, @"Global\JokiAutomation_SingleInstance");

                try
                {
                    mutexAcquired = singleInstanceMutex.WaitOne(0, false);
                }
                catch (AbandonedMutexException)
                {
                    mutexAcquired = true;
                }

                if (!mutexAcquired)
                {
                    LogStartupMessage("JokiAutomation instance already running.");
                    Environment.ExitCode = 2;
                    return;
                }

                try
                {
                    if (args.Length >= 1)
                    {
                        CommandInterpreter(args);
                    }
                    else
                    {
                        Application.SetCompatibleTextRenderingDefault(false);
                        Application.EnableVisualStyles();

                        // TODO: DLL-Check — nach Test entfernen!
                        try
                        {
                            object discovery = new BMDSwitcherAPI.CBMDSwitcherDiscovery();
                            Debug.WriteLine("DLL Check: COM-Aktivierung OK - BMDSwitcherAPI64.dll korrekt registriert.");
                            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(discovery);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"DLL Check: COM-Aktivierung FEHLGESCHLAGEN - {ex.Message} (HRESULT: 0x{ex.HResult:X8})");
                        }

                        JA = new Form1();
                        Application.Run(JA);
                    }
                }
                finally
                {
                    if (mutexAcquired)
                    {
                        singleInstanceMutex.ReleaseMutex();
                    }
                }
            }
            catch (Exception ex)
            {
                if (args.Length >= 1)
                {
                    string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RasPiAutomationLog.txt");
                    File.AppendAllText(logPath, $"\n{DateTime.Now}: KRITISCHER FEHLER - {ex.Message}\n{ex.StackTrace}\n");
                }
                else
                {
                    MessageBox.Show($"Kritischer Fehler in Main(): {ex.Message}\n\n{ex.StackTrace}", 
                                   "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            finally
            {
                singleInstanceMutex?.Dispose();
            }
        }

        private static void CommandInterpreter(string[] args)
        {
            LogData logData = null;
            
            try
            {
                logData = new LogData();
                logData.initLogData(null);
        
                logData.sendInfoMessage("=================================================");
                logData.sendInfoMessage("=== JokiAutomation Kommandozeilen-Modus START ===");
                logData.sendInfoMessage($"Argumente: {string.Join(" ", args)}");
                logData.sendInfoMessage($"Anzahl Argumente: {args.Length}");
        
                for (int i = 0; i < args.Length; i++)
                {
                    logData.sendInfoMessage($"  args[{i}] = '{args[i]}'");
                }
        
                logData.sendInfoMessage($"Arbeitsverzeichnis: {AppDomain.CurrentDomain.BaseDirectory}");
                logData.sendInfoMessage($"Build-Konfiguration: {GetBuildConfiguration()}");
        
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                Form1 form = new Form1();

                logData.sendInfoMessage(">>> Form1 erstellt");
                logData.sendInfoMessage(">>> Starte Message Loop und warte auf Shown-Event...");

                // ── Befehl wird im Form.Shown-Event ausgeführt ──────────────
                // Das stellt sicher, dass:
                // 1. Fensterhandle vorhanden ist
                // 2. Message Loop läuft
                // 3. SynchronizationContext auf UI-Thread etabliert ist
                
                bool commandExecuted = false;
                bool commandFailed = false;
                string commandError = "";

                form.Shown += async (sender, e) =>
                {
                    if (commandExecuted) return;
                    commandExecuted = true;

                    logData.sendInfoMessage(">>> Form.Shown-Event empfangen, Message Loop aktiv");

                    try
                    {
                        logData.sendInfoMessage(">>> Warte auf PTZ-Initialisierung (max. 15s)...");

                        int maxWaitMs = 15000;
                        int waitedMs = 0;
                        int checkIntervalMs = 200;

                        while (waitedMs < maxWaitMs)
                        {
                            bool isInitialized = form.IsPtzInitialized();
                            
                            if (isInitialized)
                            {
                                logData.sendInfoMessage($">>> PTZ-Kamera erfolgreich initialisiert nach {waitedMs}ms");
                                break;
                            }
                            
                            await Task.Delay(checkIntervalMs);
                            waitedMs += checkIntervalMs;
                            
                            if (waitedMs % 1000 == 0)
                            {
                                logData.sendInfoMessage($">>> Warte auf PTZ-Initialisierung... ({waitedMs}ms)");
                            }
                        }

                        if (!form.IsPtzInitialized())
                        {
                            logData.sendInfoMessage($">>> WARNUNG: PTZ-Kamera nicht initialisiert nach {maxWaitMs}ms");
                        }

                        string[] formArgs = new string[args.Length + 1];
                        formArgs[0] = "JokiAutomation.exe";
                        Array.Copy(args, 0, formArgs, 1, args.Length);

                        logData.sendInfoMessage(">>> Führe Kommando aus...");
                        await form.CommandInterpreterAsync(formArgs);

                        // Give the WinForms message loop a short grace period so UI-driven
                        // audio/profile updates can finish processing before shutdown.
                        await Task.Delay(250);

                        logData.sendInfoMessage(">>> Kommando abgeschlossen");
                    }
                    catch (Exception ex)
                    {
                        logData.sendInfoMessage($"FEHLER beim Ausführen des Kommandos: {ex.Message}");
                        logData.sendInfoMessage($"StackTrace:\n{ex.StackTrace}");
                        commandFailed = true;
                        commandError = ex.Message;
                    }
                    finally
                    {
                        // Schließe Form nach Befehlsausführung
                        form.BeginInvoke(new Action(() =>
                        {
                            logData.sendInfoMessage(">>> Schließe Anwendung...");
                            Application.Exit();
                        }));
                    }
                };

                // ── Verstecke Form im Kommandozeilenmodus ──────────────────
                form.Hide();
                form.ShowInTaskbar = false;
                form.Opacity = 0;

                logData.sendInfoMessage(">>> Starte Application.Run() - Message Loop aktiv");

                // Starte Message Loop - Form.Shown wird ausgelöst
                Application.Run(form);

                Thread.Sleep(500);

                if (commandFailed)
                {
                    logData.sendInfoMessage($"=== Kommando FEHLGESCHLAGEN: {commandError} ===");
                }
                else
                {
                    logData.sendInfoMessage("=== Kommando erfolgreich abgeschlossen ===");
                }

                logData.sendInfoMessage("=================================================");

                form.Dispose();
            }
            catch (Exception ex)
            {
                if (logData != null)
                {
                    logData.sendInfoMessage($"KRITISCHER FEHLER: {ex.Message}");
                    logData.sendInfoMessage($"StackTrace: {ex.StackTrace}");
                }
                else
                {
                    string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RasPiAutomationLog.txt");
                    File.AppendAllText(logPath, $"\n{DateTime.Now}: FEHLER - {ex.Message}\n{ex.StackTrace}\n");
                }
            }
        }

        private static void LogStartupMessage(string message)
        {
            string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RasPiAutomationLog.txt");
            File.AppendAllText(logPath, $"{DateTime.Now}: {message}\n");
        }

        private static string GetBuildConfiguration()
        {
            #if DEBUG
                return "DEBUG";
            #else
                return "RELEASE";
            #endif
        }

        private static Dictionary<string, NetworkDevice> LoadNetworkConfig(LogData logData)
        {
            var config = new Dictionary<string, NetworkDevice>();
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Network.cfg");
            
            logData.sendInfoMessage($"Suche Network.cfg: {configPath}");
            
            if (!File.Exists(configPath))
            {
                logData.sendInfoMessage($"WARNUNG: Network.cfg nicht gefunden!");
                return config;
            }
            
            try
            {
                string[] lines = File.ReadAllLines(configPath);
                logData.sendInfoMessage($"Network.cfg gelesen: {lines.Length} Zeilen");
                
                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#"))
                        continue;
                    
                    string[] parts = line.Split(';');
                    if (parts.Length >= 2)
                    {
                        string name = parts[0].Trim();
                        string ip = parts[1].Trim();
                        
                        if (ip.Contains("//"))
                        {
                            ip = ip.Substring(0, ip.IndexOf("//")).Trim();
                        }
                        
                        int port = 9910;
                        if (parts.Length >= 3 && int.TryParse(parts[2].Trim(), out int p))
                        {
                            port = p;
                        }
                        
                        config[name] = new NetworkDevice { IPAddress = ip, Port = port };
                        logData.sendInfoMessage($"  -> {name}: {ip}:{port}");
                    }
                }
                
                logData.sendInfoMessage($"Network.cfg erfolgreich geladen: {config.Count} Geräte");
            }
            catch (Exception ex)
            {
                logData.sendInfoMessage($"FEHLER beim Laden von Network.cfg: {ex.Message}");
            }
            
            return config;
        }

        private static bool RequiresATEM(string command)
        {
            string[] atemCommands = { "Altar", "Predigt", "GoPro", "Band", "Text", "ATEM_Init", 
                                     "ATEM_MIC1_On", "ATEM_MIC1_Off", "ATEM_MIC2_On", "ATEM_MIC2_Off" };
            return Array.Exists(atemCommands, cmd => cmd.Equals(command, StringComparison.OrdinalIgnoreCase));
        }

        private static bool RequiresRaspberryPi(string command)
        {
            string[] raspiCommands = { "Pause", "Timer", "Backup_Start", 
                                      "Backup_Stop", "BEAMER_ON", "BEAMER_Mute" };
            return Array.Exists(raspiCommands, cmd => cmd.Equals(command, StringComparison.OrdinalIgnoreCase));
        }

        private static bool RequiresRoku(string command)
        {
            return command.StartsWith("ROKU_", StringComparison.OrdinalIgnoreCase);
        }

        private static ATEMControl InitializeATEM(Dictionary<string, NetworkDevice> config, LogData logData)
        {
            if (!config.ContainsKey("ATEM_MiniPro"))
            {
                logData.sendInfoMessage("FEHLER: ATEM_MiniPro nicht in Network.cfg gefunden");
                logData.sendInfoMessage($"Verfügbare Geräte: {string.Join(", ", config.Keys)}");
                return null;
            }
            
            string ip = config["ATEM_MiniPro"].IPAddress;
            logData.sendInfoMessage($">>> Verbinde mit ATEM Mini Pro: {ip}:9910");
            
            try
            {
                var atem = new ATEMControl(ip);
                logData.sendInfoMessage(">>> ATEMControl Objekt erstellt");
                
                bool connected = atem.Connect();
                logData.sendInfoMessage($">>> Connect() Ergebnis: {connected}");
                
                if (connected)
                {
                    logData.sendInfoMessage(">>> ATEM ERFOLGREICH VERBUNDEN!");
                    return atem;
                }
                else
                {
                    logData.sendInfoMessage(">>> ATEM Verbindung FEHLGESCHLAGEN");
                    logData.sendInfoMessage("Prüfen Sie:");
                    logData.sendInfoMessage("  - ATEM ist eingeschaltet");
                    logData.sendInfoMessage("  - Netzwerkkabel ist verbunden");
                    logData.sendInfoMessage($"  - IP-Adresse {ip} ist korrekt");
                    logData.sendInfoMessage("  - Firewall blockiert Port 9910 nicht");
                    return null;
                }
            }
            catch (Exception ex)
            {
                logData.sendInfoMessage($">>> AUSNAHME beim ATEM Connect: {ex.Message}");
                logData.sendInfoMessage($">>> StackTrace: {ex.StackTrace}");
                return null;
            }
        }

        private static RasPi InitializeRaspberryPi(Dictionary<string, NetworkDevice> config, LogData logData)
        {
            logData.sendInfoMessage(">>> RaspberryPi Initialisierung (TODO)");
            return null;
        }

        private static ROKU_TV_Remote InitializeRoku(Dictionary<string, NetworkDevice> config, LogData logData)
        {
            if (!config.ContainsKey("Roku_TV"))
            {
                logData.sendInfoMessage("FEHLER: Roku_TV nicht in Network.cfg gefunden");
                return null;
            }
            
            string ip = config["Roku_TV"].IPAddress;
            logData.sendInfoMessage($">>> Verbinde mit Roku TV: {ip}");
            return new ROKU_TV_Remote(ip);
        }

        private static void ExecuteATEMCommand(ATEMControl atem, string command, string[] args, LogData logData)
        {
            if (atem == null || !atem.IsConnected)
            {
                logData.sendInfoMessage(">>> FEHLER: ATEM nicht verbunden!");
                return;
            }
            
            logData.sendInfoMessage($">>> Führe ATEM-Kommando aus: {command}");
            
            try
            {
                switch (command.ToLower())
                {
                    case "altar":
                        logData.sendInfoMessage(">>> Setze Input auf HDMI 3 (Altar)...");
                        atem.TransitionToProgramInput(ATEMControl.VideoSource.Input3);
                        logData.sendInfoMessage(">>> ERFOLG: ATEM auf Altar (HDMI 3) umgeschaltet");
                        break;
                        
                    case "predigt":
                        logData.sendInfoMessage(">>> Setze Input auf HDMI 4 (Predigt)...");
                        atem.TransitionToProgramInput(ATEMControl.VideoSource.Input4);
                        logData.sendInfoMessage(">>> ERFOLG: ATEM auf Predigt (HDMI 4) umgeschaltet");
                        break;
                        
                    case "gopro":
                        logData.sendInfoMessage(">>> Setze Input auf HDMI 1 (GoPro)...");
                        atem.TransitionToProgramInput(ATEMControl.VideoSource.Input2);
                        logData.sendInfoMessage(">>> ERFOLG: ATEM auf GoPro (HDMI 2) umgeschaltet");
                        break;
                        
                    case "band":
                    case "text":
                        logData.sendInfoMessage(">>> Setze Input auf HDMI 2 (Laptop)...");
                        atem.TransitionToProgramInput(ATEMControl.VideoSource.Input1);
                        logData.sendInfoMessage(">>> ERFOLG: ATEM auf Laptop (HDMI 1) umgeschaltet");
                        break;
                        
                    case "atem_init":
                        logData.sendInfoMessage(">>> Initialisiere ATEM zu Default-Zustand...");
                        atem.InitializeToDefaultState();
                        logData.sendInfoMessage(">>> ERFOLG: ATEM Initialisierung abgeschlossen");
                        break;
                        
                    case "atem_mic1_on":
                        logData.sendInfoMessage(">>> Aktiviere MIC 1...");
                        atem.SetAudioMixerInput(1, true);
                        logData.sendInfoMessage(">>> ERFOLG: MIC 1 aktiviert");
                        break;
                        
                    case "atem_mic1_off":
                        logData.sendInfoMessage(">>> Deaktiviere MIC 1...");
                        atem.SetAudioMixerInput(1, false);
                        logData.sendInfoMessage(">>> ERFOLG: MIC 1 deaktiviert");
                        break;
                    case "atem_mic2_on":
                        logData.sendInfoMessage(">>> Aktiviere MIC 2...");
                        atem.SetAudioMixerInput(2, true);
                        logData.sendInfoMessage(">>> ERFOLG: MIC 2 aktiviert");
                        break;
                    case "atem_mic2_off":
                        logData.sendInfoMessage(">>> Deaktiviere MIC 2...");
                        atem.SetAudioMixerInput(2, false);
                        logData.sendInfoMessage(">>> ERFOLG: MIC 2 deaktiviert");
                        break;
                        
                    default:
                        logData.sendInfoMessage($">>> WARNUNG: ATEM-Kommando '{command}' nicht implementiert");
                        break;
                }
                
            }
            catch (Exception ex)
            {
                logData.sendInfoMessage($">>> FEHLER bei ATEM-Kommando: {ex.Message}");
                logData.sendInfoMessage($">>> StackTrace: {ex.StackTrace}");
            }
        }

        private static void ExecuteRaspberryPiCommand(RasPi raspi, string command, string[] args, LogData logData)
        {
            logData.sendInfoMessage($">>> RaspberryPi-Kommando '{command}' (noch nicht implementiert)");
        }

        private static void ExecuteRokuCommand(ROKU_TV_Remote roku, string command, string[] args, LogData logData)
        {
            logData.sendInfoMessage($">>> Roku-Kommando '{command}' (noch nicht implementiert)");
        }
    }

    internal class NetworkDevice
    {
        public string IPAddress { get; set; }
        public int Port { get; set; }
    }
}
