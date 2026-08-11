using System;
using System.IO;

namespace CanonPtzCommon
{
    public static class NetworkCfgReader
    {
        public static CameraConfig LoadCamera(string configPath, string deviceName = "Canon_CRN100")
        {
            if (!File.Exists(configPath))
            {
                throw new FileNotFoundException("Network.cfg nicht gefunden", configPath);
            }

            string[] lines = File.ReadAllLines(configPath);

            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();

                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                {
                    continue;
                }

                string[] parts = line.Split(';');
                if (parts.Length < 2)
                {
                    continue;
                }

                if (!string.Equals(parts[0].Trim(), deviceName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var config = new CameraConfig
                {
                    DeviceName = parts[0].Trim(),
                    IpAddress = parts[1].Trim(),
                    Port = 80,
                    PanSpeed = 1500,
                    TiltSpeed = 1500,
                    ZoomSpeed = 30,
                    AutoTrackingHomePreset = 1,        // Default: Preset 1 (Altar)
                    AutoTrackingStartupDelayMs = 5000, // Erhöht auf 5 Sekunden!
                    AutoTrackingRecoveryTimeSeconds = 10,
                    AutoTrackingHomePosition = null     // Optional: stored PTZ position
                };

                if (parts.Length >= 3 && int.TryParse(parts[2].Trim(), out int port))
                {
                    config.Port = port;
                }

                if (parts.Length >= 4)
                {
                    config.Username = parts[3].Trim();
                }

                if (parts.Length >= 5)
                {
                    config.Password = parts[4].Trim();
                }

                if (parts.Length >= 6)
                {
                    config.Protocol = parts[5].Trim();
                }

                if (parts.Length >= 7 && int.TryParse(parts[6].Trim(), out int panSpeed))
                {
                    config.PanSpeed = panSpeed;
                }

                if (parts.Length >= 8 && int.TryParse(parts[7].Trim(), out int tiltSpeed))
                {
                    config.TiltSpeed = tiltSpeed;
                }

                if (parts.Length >= 9 && int.TryParse(parts[8].Trim(), out int zoomSpeed))
                {
                    config.ZoomSpeed = zoomSpeed;
                }

                // Neue AutoTracking-Parameter (optional in Network.cfg)
                if (parts.Length >= 10 && int.TryParse(parts[9].Trim(), out int homePreset))
                {
                    config.AutoTrackingHomePreset = homePreset;
                }

                if (parts.Length >= 11 && int.TryParse(parts[10].Trim(), out int startupDelay))
                {
                    config.AutoTrackingStartupDelayMs = startupDelay;
                }

                if (parts.Length >= 12 && int.TryParse(parts[11].Trim(), out int recoveryTime))
                {
                    config.AutoTrackingRecoveryTimeSeconds = recoveryTime;
                }

                // AutoTrackingHomePosition (optional, Format: "pan:tilt:zoom")
                if (parts.Length >= 13 && !string.IsNullOrWhiteSpace(parts[12].Trim()))
                {
                    config.AutoTrackingHomePosition = parts[12].Trim();
                }

                config.UseHttps = config.Port == 443;
                return config;
            }

            throw new InvalidOperationException($"Gerät '{deviceName}' nicht in Network.cfg gefunden.");
        }
    }
}
