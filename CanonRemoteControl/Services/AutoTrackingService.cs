using CanonPtzCommon;
using System;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CanonRemoteControl.Services
{
    public sealed class AutoTrackingService : IAutoTrackingService, IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;

        public AutoTrackingService(CameraConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            string protocol = config.UseHttps ? "https" : "http";
            _baseUrl = $"{protocol}://{config.IpAddress}:{config.Port}/cgi-addon/Auto_Tracking_RA-AT001/app_ctrl";

            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (sender, cert, chain, errors) => true
            };

            if (!string.IsNullOrWhiteSpace(config.Username))
            {
                var credentialCache = new CredentialCache();
                credentialCache.Add(
                    new Uri($"{protocol}://{config.IpAddress}:{config.Port}"),
                    "Digest",
                    new NetworkCredential(config.Username, config.Password ?? string.Empty));

                handler.Credentials = credentialCache;
                handler.PreAuthenticate = false;
                handler.UseDefaultCredentials = false;
            }

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(3)
            };
        }

        public async Task<bool> IsEnabledAsync()
        {
            AutoTrackingStatus status = await GetStatusAsync();
            return status != null && status.TrackingEnabled;
        }

        public Task<CommandResult> EnableAsync()
        {
            return UpdateTrackingEnableAsync(true);
        }

        public Task<CommandResult> DisableAsync()
        {
            return UpdateTrackingEnableAsync(false);
        }

        public async Task<AutoTrackingStatus> GetStatusAsync()
        {
            try
            {
                string configUrl = $"{_baseUrl}/get_config.cgi?keys=trackingEnable,zoomControlEnable,autoZoomEnable,sensitivity,targetSelection,trackingRestartEnable,multiTargetAssistEnable,faceOperationAssistEnable,sitStandAssistEnable";
                var configResponse = await _httpClient.GetAsync(configUrl);
                string configBody = await configResponse.Content.ReadAsStringAsync();

                if (!configResponse.IsSuccessStatusCode)
                {
                    return null;
                }

                string infoUrl = $"{_baseUrl}/track_info.cgi";
                var infoResponse = await _httpClient.GetAsync(infoUrl);
                string infoBody = await infoResponse.Content.ReadAsStringAsync();

                if (!infoResponse.IsSuccessStatusCode)
                {
                    return null;
                }

                var status = new AutoTrackingStatus
                {
                    TrackingEnabled = ParseBoolFlag(configBody, "trackingEnable"),
                    ZoomControlEnabled = ParseBoolFlag(configBody, "zoomControlEnable"),
                    AutoZoomEnabled = ParseBoolFlag(configBody, "autoZoomEnable"),
                    TrackingRestartEnabled = ParseBoolFlag(configBody, "trackingRestartEnable"),
                    Sensitivity = ParseInt(configBody, "sensitivity"),
                    TargetSelection = ParseInt(configBody, "targetSelection"),
                    MultiTargetSupported = ContainsKey(configBody, "multiTargetAssistEnable"),
                    FaceDirectionSupported = ContainsKey(configBody, "faceOperationAssistEnable"),
                    SitStandSupported = ContainsKey(configBody, "sitStandAssistEnable"),
                    DetectionCount = ParseInt(infoBody, "detection_num"),
                    TrackStatus = ParseInt(infoBody, "track_status"),
                    TrackResult = ParseInt(infoBody, "track_result"),
                    TargetId = ParseInt(infoBody, "target_id"),
                    PanPosition = ParseInt(infoBody, "pan"),
                    TiltPosition = ParseInt(infoBody, "tilt"),
                    ZoomPosition = ParseInt(infoBody, "zoom")
                };

                return status;
            }
            catch
            {
                return null;
            }
        }

        private async Task<CommandResult> UpdateTrackingEnableAsync(bool enabled)
        {
            string commandName = enabled ? "AutoTrackingEnable" : "AutoTrackingDisable";
            string value = enabled ? "1" : "0";

            try
            {
                string url = $"{_baseUrl}/update_config.cgi?trackingEnable={value}&roundType=1";
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[AutoTracking] {commandName}: GET {url}");
#endif
                var response = await _httpClient.GetAsync(url);
                string body = await response.Content.ReadAsStringAsync();
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[AutoTracking] Response: HTTP {(int)response.StatusCode} - {body}");
#endif

                if (!response.IsSuccessStatusCode)
                {
                    return CommandResult.Fail(commandName, $"HTTP-Fehler: {(int)response.StatusCode}", response.StatusCode, body);
                }

                string statusCode = ParseString(body, "status_code");
                if (string.Equals(statusCode, "G0_100", StringComparison.OrdinalIgnoreCase))
                {
                    string message = enabled ? "Auto Tracking aktiviert" : "Auto Tracking deaktiviert";
                    return CommandResult.Ok(commandName, message, response.StatusCode, body);
                }

                if (string.Equals(statusCode, "G0_101", StringComparison.OrdinalIgnoreCase))
                {
                    return CommandResult.Fail(commandName, "FeatureNotAvailable (G0_101: Parameter range is limited)", response.StatusCode, body);
                }

                if (!string.IsNullOrEmpty(statusCode))
                {
                    return CommandResult.Fail(commandName, $"Auto-Tracking Fehlercode: {statusCode}", response.StatusCode, body);
                }

                return CommandResult.Fail(commandName, "Ungültige Antwort vom Auto-Tracking-Service", response.StatusCode, body);
            }
            catch (Exception ex)
            {
                return CommandResult.Fail(commandName, "Kommunikationsfehler Auto Tracking", exception: ex);
            }
        }

        private static bool ContainsKey(string json, string key)
        {
            return Regex.IsMatch(json ?? string.Empty, $"\"{Regex.Escape(key)}\"\\s*:");
        }

        private static bool ParseBoolFlag(string json, string key)
        {
            return ParseString(json, key) == "1";
        }

        private static int ParseInt(string json, string key)
        {
            string value = ParseString(json, key);
            if (int.TryParse(value, out int parsed))
            {
                return parsed;
            }

            return 0;
        }

        private static string ParseString(string json, string key)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return string.Empty;
            }

            var match = Regex.Match(json, $"\"{Regex.Escape(key)}\"\\s*:\\s*\"([^\"]*)\"");
            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            return string.Empty;
        }

        public async Task<TrackInfo> GetTrackInfoAsync()
        {
            try
            {
                string url = $"{_baseUrl}/track_info.cgi";
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[AutoTracking] GetTrackInfo: GET {url}");
#endif
                var response = await _httpClient.GetAsync(url);
                string body = await response.Content.ReadAsStringAsync();
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[AutoTracking] Response: HTTP {(int)response.StatusCode}");
                System.Diagnostics.Debug.WriteLine($"[AutoTracking] Response Body: {body}");
#endif

                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                int pan = ParseInt(body, "pan");
                int tilt = ParseInt(body, "tilt");
                int zoom = ParseInt(body, "zoom");

#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[AutoTracking] Parsed values: Pan={pan}, Tilt={tilt}, Zoom={zoom}");
#endif

                return new TrackInfo
                {
                    Pan = pan,
                    Tilt = tilt,
                    Zoom = zoom
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AutoTracking] GetTrackInfo Exception: {ex.Message}");
                return null;
            }
        }

        public async Task<CommandResult> SetHomePositionAsync(string homePosition)
        {
            string commandName = "SetHomePosition";

            try
            {
                // Canon RA-AT001 verwendet homePosition mit Doppelpunkt-Trennung (pan:tilt:zoom)
                string url = $"{_baseUrl}/update_config.cgi?homePosition={homePosition}";
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[AutoTracking] {commandName}: GET {url}");
#endif
                var response = await _httpClient.GetAsync(url);
                string body = await response.Content.ReadAsStringAsync();
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[AutoTracking] Response: HTTP {(int)response.StatusCode} - {body}");
#endif

                if (!response.IsSuccessStatusCode)
                {
                    return CommandResult.Fail(commandName, $"HTTP-Fehler: {(int)response.StatusCode}", response.StatusCode, body);
                }

                string statusCode = ParseString(body, "status_code");
                if (string.Equals(statusCode, "G0_100", StringComparison.OrdinalIgnoreCase))
                {
                    return CommandResult.Ok(commandName, $"Home Position gesetzt: {homePosition}", response.StatusCode, body);
                }

                if (!string.IsNullOrEmpty(statusCode))
                {
                    return CommandResult.Fail(commandName, $"Fehlercode: {statusCode}", response.StatusCode, body);
                }

                return CommandResult.Fail(commandName, "Ungültige Antwort", response.StatusCode, body);
            }
            catch (Exception ex)
            {
                return CommandResult.Fail(commandName, "Kommunikationsfehler", exception: ex);
            }
        }

        public async Task<CommandResult> EnableRecoveryControlAsync(int recoveryTimeSeconds)
        {
            string commandName = "EnableRecoveryControl";

            try
            {
                string url = $"{_baseUrl}/update_config.cgi?recoveryControl=1&recoveryControlTime={recoveryTimeSeconds}";
                
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[AutoTracking] {commandName}: GET {url}");
#endif

                var response = await _httpClient.GetAsync(url);
                string body = await response.Content.ReadAsStringAsync();

#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[AutoTracking] Response: HTTP {(int)response.StatusCode} - {body}");
#endif

                if (!response.IsSuccessStatusCode)
                {
                    return CommandResult.Fail(commandName, $"HTTP-Fehler: {(int)response.StatusCode}", response.StatusCode, body);
                }

                string statusCode = ParseString(body, "status_code");
                if (string.Equals(statusCode, "G0_100", StringComparison.OrdinalIgnoreCase))
                {
                    return CommandResult.Ok(commandName, $"Recovery Control aktiviert ({recoveryTimeSeconds}s)", response.StatusCode, body);
                }

                if (!string.IsNullOrEmpty(statusCode))
                {
                    string description = ParseString(body, "description");
                    return CommandResult.Fail(commandName, $"API-Fehlercode: {statusCode} - {description}", response.StatusCode, body);
                }

                return CommandResult.Fail(commandName, "Ungültige Antwort vom Auto-Tracking-Service", response.StatusCode, body);
            }
            catch (Exception ex)
            {
                return CommandResult.Fail(commandName, "Kommunikationsfehler Recovery Control", exception: ex);
            }
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}
