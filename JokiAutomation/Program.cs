using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;

namespace JokiAutomation
{
    static class Program
    {
        private static Form1 JA;
        
        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                // Prüfe ob Kommandozeilen-Modus oder GUI-Modus
                if (args.Length >= 1)
                {
                    // Kommandozeilen-Modus: KEINE Form, direkte Ausführung
                    CommandInterpreter(args);
                }
                else
                {
                    // GUI-Modus: Starte WinForms-Anwendung
                    Application.SetCompatibleTextRenderingDefault(false);
                    Application.EnableVisualStyles();
                    JA = new Form1();
                    Application.Run(JA);
                }
            }
            catch (Exception ex)
            {
                // Im Kommandozeilen-Modus: Logge in Datei
                if (args.Length >= 1)
                {
                    string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RasPiAutomationLog.txt");
                    File.AppendAllText(logPath, $"\n{DateTime.Now}: KRITISCHER FEHLER - {ex.Message}\n{ex.StackTrace}\n");
                }
                else
                {
                    // Im GUI-Modus: Zeige MessageBox
                    MessageBox.Show($"Kritischer Fehler in Main(): {ex.Message}\n\n{ex.StackTrace}", 
                                   "Fehler", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private static void RunCommandLineMode(string[] args)
        {
            LogData logData = null;
            
            try
            {
                // Erstelle minimale Komponenten ohne Form
                logData = new LogData();
                logData.initLogData(null); // Kein Form → Logging geht in Datei
                
                logData.sendInfoMessage("=================================================");
                logData.sendInfoMessage("=== JokiAutomation Kommandozeilen-Modus START ===");
                logData.sendInfoMessage($"Argumente: {string.Join(" ", args)}");
                logData.sendInfoMessage($"Anzahl Argumente: {args.Length}");
                
                // DEBUG: Alle Argumente einzeln ausgeben
                for (int i = 0; i < args.Length; i++)
                {
                    logData.sendInfoMessage($"  args[{i}] = '{args[i]}'");
                }
                
                logData.sendInfoMessage($"Arbeitsverzeichnis: {AppDomain.CurrentDomain.BaseDirectory}");
                
                // Lade Netzwerk-Konfiguration
                var networkConfig = LoadNetworkConfig(logData);
                logData.sendInfoMessage($"Netzwerk-Config: {networkConfig.Count} Geräte geladen");
                
                if (args.Length < 1)
                {
                    logData.sendInfoMessage("FEHLER: Kein Kommando angegeben");
                    logData.sendInfoMessage("Verwendung: JokiAutomation.exe <Kommando> [Parameter]");
                    logData.sendInfoMessage("Verfügbare Kommandos: Altar, Predigt, GoPro, Band, Text, ATEM_Init");
                    return;
                }
                
                string command = args[0];
                logData.sendInfoMessage($">>> Kommando: {command}");
                
                // Initialisiere nur benötigte Komponenten basierend auf Kommando
                if (RequiresATEM(command))
                {
                    logData.sendInfoMessage(">>> Benötigt: ATEM Mini Pro");
                    var atem = InitializeATEM(networkConfig, logData);
                    
                    if (atem != null)
                    {
                        ExecuteATEMCommand(atem, command, args, logData);
                        
                        // WICHTIG: Warte bis Befehl vollständig ausgeführt wurde
                        Thread.Sleep(1000);
                        
                        atem.Dispose();
                        logData.sendInfoMessage(">>> ATEM Verbindung geschlossen");
                    }
                    else
                    {
                        logData.sendInfoMessage("FEHLER: ATEM konnte nicht initialisiert werden");
                    }
                }
                else if (RequiresRaspberryPi(command))
                {
                    logData.sendInfoMessage(">>> Benötigt: Raspberry Pi");
                    var raspi = InitializeRaspberryPi(networkConfig, logData);
                    ExecuteRaspberryPiCommand(raspi, command, args, logData);
                }
                else if (RequiresRoku(command))
                {
                    logData.sendInfoMessage(">>> Benötigt: Roku TV");
                    var roku = InitializeRoku(networkConfig, logData);
                    ExecuteRokuCommand(roku, command, args, logData);
                }
                else
                {
                    logData.sendInfoMessage($"FEHLER: Unbekanntes Kommando: {command}");
                    logData.sendInfoMessage("Verfügbare Kommandos: Altar, Predigt, GoPro, Band, Text, ATEM_Init");
                }
                
                logData.sendInfoMessage("=== Kommando abgeschlossen ===");
                logData.sendInfoMessage("=================================================");
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
                    // Fallback: Direkt in Datei schreiben
                    string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RasPiAutomationLog.txt");
                    File.AppendAllText(logPath, $"\n{DateTime.Now}: FEHLER - {ex.Message}\n{ex.StackTrace}\n");
                }
            }
        }

        private static void CommandInterpreter(string[] args)
        {
            LogData logData = null;
            
            try
            {
                // Erstelle minimale Komponenten ohne Form
                logData = new LogData();
                logData.initLogData(null); // Kein Form → Logging geht in Datei
                
                logData.sendInfoMessage("=================================================");
                logData.sendInfoMessage("=== JokiAutomation Kommandozeilen-Modus START ===");
                logData.sendInfoMessage($"Argumente: {string.Join(" ", args)}");
                logData.sendInfoMessage($"Anzahl Argumente: {args.Length}");
                
                // DEBUG: Alle Argumente einzeln ausgeben
                for (int i = 0; i < args.Length; i++)
                {
                    logData.sendInfoMessage($"  args[{i}] = '{args[i]}'");
                }
                
                logData.sendInfoMessage($"Arbeitsverzeichnis: {AppDomain.CurrentDomain.BaseDirectory}");
                
                // Da die Form1.CommandInterpreter Zugriff auf Form-Komponenten benötigt,
                // müssen wir eine minimale Form-Instanz erstellen
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                
                Form1 form = new Form1();
                
                // Prepare args array: CommandInterpreter in Form1 erwartet args[0] = Programmname, args[1] = Kommando
                string[] formArgs = new string[args.Length + 1];
                formArgs[0] = "JokiAutomation.exe"; // Programmname als erstes Argument
                Array.Copy(args, 0, formArgs, 1, args.Length);
                
                // Rufe CommandInterpreter aus Form1 auf
                form.CommandInterpreter(formArgs);
                
                logData.sendInfoMessage("=== Kommando abgeschlossen ===");
                logData.sendInfoMessage("=================================================");
                
                // Dispose Form
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
                    // Fallback: Direkt in Datei schreiben
                    string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RasPiAutomationLog.txt");
                    File.AppendAllText(logPath, $"\n{DateTime.Now}: FEHLER - {ex.Message}\n{ex.StackTrace}\n");
                }
            }
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
                        
                        // Entferne Kommentare nach der IP (z.B. "192.168.0.40 //192.168.178.50")
                        if (ip.Contains("//"))
                        {
                            ip = ip.Substring(0, ip.IndexOf("//")).Trim();
                        }
                        
                        int port = 9910; // ATEM Standard-Port
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
                        atem.TransitionToProgramInput(ATEMControl.VideoSource.Input1);
                        logData.sendInfoMessage(">>> ERFOLG: ATEM auf GoPro (HDMI 1) umgeschaltet");
                        break;
                        
                    case "band":
                    case "text":
                        logData.sendInfoMessage(">>> Setze Input auf HDMI 2 (Laptop)...");
                        atem.TransitionToProgramInput(ATEMControl.VideoSource.Input2);
                        logData.sendInfoMessage(">>> ERFOLG: ATEM auf Laptop (HDMI 2) umgeschaltet");
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
                        
                    default:
                        logData.sendInfoMessage($">>> WARNUNG: ATEM-Kommando '{command}' nicht implementiert");
                        break;
                }
                
                // Warte auf Ausführung
                logData.sendInfoMessage(">>> Warte 500ms auf Ausführung...");
                Thread.Sleep(500);
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
