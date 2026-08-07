using CanonPtzCommon;
using CanonRemoteControl.Services;
using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CanonRemoteControl
{
    public sealed class XcCanonPtzController : ICanonPtzController, IDisposable
    {
        private readonly CameraConfig _config;
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly IAutoTrackingService _autoTrackingService;
        private string _sessionId;
        private int _reconnectAttempts = 0;
        private const int MaxReconnectAttempts = 3;

        public bool IsConnected { get; private set; }

        public XcCanonPtzController(CameraConfig config)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine("[XcCanonPtzController] Constructor called");
#endif
            _config = config ?? throw new ArgumentNullException(nameof(config));

            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
            };

            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(3)
            };

            string protocol = _config.UseHttps ? "https" : "http";
            _baseUrl = $"{protocol}://{_config.IpAddress}:{_config.Port}";
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[XcCanonPtzController] BaseUrl: {_baseUrl}");
#endif

            if (!string.IsNullOrEmpty(_config.Username))
            {
                var credentials = Convert.ToBase64String(
                    Encoding.ASCII.GetBytes($"{_config.Username}:{_config.Password}"));
                _httpClient.DefaultRequestHeaders.Authorization = 
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
#if DEBUG
                System.Diagnostics.Debug.WriteLine("[XcCanonPtzController] Basic Auth configured");
#endif
            }
#if DEBUG
            else
            {
                System.Diagnostics.Debug.WriteLine("[XcCanonPtzController] No authentication configured");
            }
#endif

            // Initialize RA-AT001 Auto Tracking service
            _autoTrackingService = new AutoTrackingService(_config);
        }

        public async Task<CommandResult> ConnectAsync()
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine("[XcCanonPtzController] ConnectAsync called");
#endif
            try
            {
                var openResult = await SendCgiRequestAsync("open.cgi", "SessionOpen");

                if (!openResult.Success)
                {
                    IsConnected = false;
#if DEBUG
                    System.Diagnostics.Debug.WriteLine("[XcCanonPtzController] Connect failed, IsConnected=false");
#endif
                    return openResult;
                }

                _sessionId = ExtractSessionId(openResult.ResponseBody);
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[XcCanonPtzController] SessionId extracted: {_sessionId}");
#endif

                IsConnected = true;
                _reconnectAttempts = 0;
#if DEBUG
                System.Diagnostics.Debug.WriteLine("[XcCanonPtzController] Connect successful, IsConnected=true");
#endif

                return CommandResult.Ok("Connect", "Verbindung hergestellt");
            }
            catch (Exception ex)
            {
                IsConnected = false;
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[XcCanonPtzController] Connect exception: {ex.Message}");
#endif
                return CommandResult.Fail("Connect", "Verbindungsfehler", exception: ex);
            }
        }

        public async Task<CommandResult> DisconnectAsync()
        {
            try
            {
                if (!string.IsNullOrEmpty(_sessionId))
                {
                    await SendCgiRequestAsync($"yield.cgi?s={_sessionId}", "SessionYield");
                    await SendCgiRequestAsync($"close.cgi?s={_sessionId}", "SessionClose");
                }

                IsConnected = false;
                _sessionId = null;
                return CommandResult.Ok("Disconnect", "Verbindung getrennt");
            }
            catch (Exception ex)
            {
                IsConnected = false;
                return CommandResult.Fail("Disconnect", "Fehler beim Trennen", exception: ex);
            }
        }

        public Task<CommandResult> StartPanLeftAsync()
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine("[XcCanonPtzController] StartPanLeftAsync called");
#endif
            return SendControlParametersAsync(
                $"c.1.pan.speed.mode.dir=manual&c.1.pan.speed.dir={_config.PanSpeed}&c.1.pan=left",
                "PanLeft");
        }

        public Task<CommandResult> StartPanRightAsync()
        {
            return SendControlParametersAsync(
                $"c.1.pan.speed.mode.dir=manual&c.1.pan.speed.dir={_config.PanSpeed}&c.1.pan=right",
                "PanRight");
        }

        public Task<CommandResult> StartTiltUpAsync()
        {
            return SendControlParametersAsync(
                $"c.1.tilt.speed.mode.dir=manual&c.1.tilt.speed.dir={_config.TiltSpeed}&c.1.tilt=up",
                "TiltUp");
        }

        public Task<CommandResult> StartTiltDownAsync()
        {
            return SendControlParametersAsync(
                $"c.1.tilt.speed.mode.dir=manual&c.1.tilt.speed.dir={_config.TiltSpeed}&c.1.tilt=down",
                "TiltDown");
        }

        public Task<CommandResult> StartZoomInAsync()
        {
            return SendControlParametersAsync(
                $"c.1.zoom.speed.dir={_config.ZoomSpeed}&c.1.zoom=tele",
                "ZoomIn");
        }

        public Task<CommandResult> StartZoomOutAsync()
        {
            return SendControlParametersAsync(
                $"c.1.zoom.speed.dir={_config.ZoomSpeed}&c.1.zoom=wide",
                "ZoomOut");
        }

        public Task<CommandResult> StopPanAsync()
        {
            return SendControlParametersAsync("c.1.pan=stop", "StopPan");
        }

        public Task<CommandResult> StopTiltAsync()
        {
            return SendControlParametersAsync("c.1.tilt=stop", "StopTilt");
        }

        public Task<CommandResult> StopZoomAsync()
        {
            return SendControlParametersAsync("c.1.zoom=stop", "StopZoom");
        }

        public async Task<CommandResult> StopAllAsync()
        {
            var panResult = await StopPanAsync();
            var tiltResult = await StopTiltAsync();
            var zoomResult = await StopZoomAsync();

            if (panResult.Success && tiltResult.Success && zoomResult.Success)
            {
                return CommandResult.Ok("StopAll", "Alle Bewegungen gestoppt");
            }

            return CommandResult.Fail("StopAll", "Fehler beim Stoppen");
        }

        public async Task<CommandResult> RecallPresetAsync(int presetNumber)
        {
            return await SendControlParametersAsync($"p={presetNumber}", $"RecallPreset{presetNumber}");
        }

        public async Task<CommandResult> StorePresetAsync(int presetNumber)
        {
            if (!IsConnected)
            {
                return CommandResult.Fail("StorePreset", "Nicht verbunden");
            }

            string path = $"preset/set?s={_sessionId}&p={presetNumber}&all=enabled";
            return await SendCgiRequestAsync(path, $"StorePreset{presetNumber}");
        }

        public Task<CommandResult> EnableTrackingSingleAsync()
        {
            // Use RA-AT001 Auto Tracking API instead of XC focus tracking
            return _autoTrackingService.EnableAsync();
        }

        public Task<CommandResult> EnableTrackingGroupAsync()
        {
            // RA-AT001 doesn't distinguish between single and group tracking
            // Both use the same enable command
            return _autoTrackingService.EnableAsync();
        }

        public Task<CommandResult> DisableTrackingAsync()
        {
            // Use RA-AT001 Auto Tracking API
            return _autoTrackingService.DisableAsync();
        }

        public async Task<CameraPosition> GetPositionAsync()
        {
            try
            {
                var result = await SendCgiRequestAsync("info.cgi?item=c.1.pan,c.1.tilt,c.1.zoom", "GetPosition");
                if (!result.Success)
                {
                    return null;
                }

                return ParsePosition(result.ResponseBody);
            }
            catch
            {
                return null;
            }
        }

        public async Task<CameraStatus> GetStatusAsync()
        {
            try
            {
                var result = await SendCgiRequestAsync(
                    "info.cgi?item=c.1.pan.status,c.1.tilt.status,c.1.zoom.status",
                    "GetStatus");
                if (!result.Success)
                {
                    return null;
                }

                return ParseStatus(result.ResponseBody);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Sets the camera standby/idle mode
        /// </summary>
        /// <param name="standby">True to enter standby mode, false to enter idle (active) mode</param>
        /// <returns>CommandResult with HTTP status and XC response</returns>
        public async Task<CommandResult> SetStandbyAsync(bool standby)
        {
            string command = standby ? "standby" : "idle";
            string commandName = standby ? "EnterStandby" : "ExitStandby";
            
            return await SendCgiRequestAsync($"standby.cgi?cmd={command}", commandName);
        }

        private async Task<CommandResult> SendControlParametersAsync(string parameterQuery, string commandName)
        {
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[XcCanonPtzController] SendControlParametersAsync called: {commandName}, IsConnected={IsConnected}");
#endif

            if (!IsConnected)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[XcCanonPtzController] {commandName} - Not connected, returning failure");
#endif
                return CommandResult.Fail(commandName, "Nicht verbunden");
            }

            string path = $"control.cgi?{parameterQuery}";
#if DEBUG
            System.Diagnostics.Debug.WriteLine($"[XcCanonPtzController] Calling SendCgiRequestAsync with path: {path}");
#endif
            return await SendCgiRequestAsync(path, commandName);
        }

        private async Task<CommandResult> SendCgiRequestAsync(string path, string commandName)
        {
            try
            {
                string url = $"{_baseUrl}/-wvhttp-01-/{path}";
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[XcCanonPtzController] {commandName}: GET {url}");
                if (_httpClient.DefaultRequestHeaders.Authorization != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[XcCanonPtzController] Authorization: {_httpClient.DefaultRequestHeaders.Authorization.Scheme} {_httpHttpClient.DefaultRequestHeaders.Authorization.Parameter}");
                }
#endif

                var response = await _httpClient.GetAsync(url);
                string body = await response.Content.ReadAsStringAsync();

#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[XcCanonPtzController] Response: HTTP {(int)response.StatusCode} - {body}");
#endif

                int? livescopeStatus = GetLivescopeStatus(response);

                if (response.IsSuccessStatusCode && (!livescopeStatus.HasValue || livescopeStatus.Value == 0))
                {
                    return CommandResult.Ok(commandName, "OK", response.StatusCode, body);
                }

                return CommandResult.Fail(
                    commandName,
                    $"HTTP/Livescope Fehler: HTTP {(int)response.StatusCode}, Livescope {livescopeStatus}",
                    response.StatusCode,
                    body);
            }
            catch (HttpRequestException ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[XcCanonPtzController] {commandName} - HttpRequestException: {ex.Message}");
#endif
                await HandleReconnectAsync();
                return CommandResult.Fail(commandName, "Netzwerkfehler", exception: ex);
            }
            catch (TaskCanceledException ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[XcCanonPtzController] {commandName} - TaskCanceledException: {ex.Message}");
#endif
                await HandleReconnectAsync();
                return CommandResult.Fail(commandName, "Timeout", exception: ex);
            }
            catch (Exception ex)
            {
#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[XcCanonPtzController] {commandName} - Exception: {ex.Message}");
#endif
                return CommandResult.Fail(commandName, "Unbekannter Fehler", exception: ex);
            }
        }

        private static int? GetLivescopeStatus(HttpResponseMessage response)
        {
            if (response.Headers.TryGetValues("livescope-status", out var values))
            {
                foreach (string value in values)
                {
                    if (int.TryParse(value, out int status))
                    {
                        return status;
                    }
                }
            }

            return null;
        }

        private async Task HandleReconnectAsync()
        {
            if (_reconnectAttempts >= MaxReconnectAttempts)
            {
                IsConnected = false;
                return;
            }

            _reconnectAttempts++;
            await DisconnectAsync();
            await Task.Delay(1000);
            await ConnectAsync();
        }

        private string ExtractSessionId(string responseBody)
        {
            var values = ParseXcKeyValueResponse(responseBody);

            if (values.TryGetValue("s", out string sessionId))
            {
                return sessionId;
            }

            return string.Empty;
        }

        private CameraPosition ParsePosition(string responseBody)
        {
            var values = ParseXcKeyValueResponse(responseBody);

            return new CameraPosition
            {
                Pan = TryGetInt(values, "c.1.pan"),
                Tilt = TryGetInt(values, "c.1.tilt"),
                Zoom = TryGetInt(values, "c.1.zoom")
            };
        }

        private CameraStatus ParseStatus(string responseBody)
        {
            var values = ParseXcKeyValueResponse(responseBody);

            string panStatus = TryGetString(values, "c.1.pan.status", "0");
            string tiltStatus = TryGetString(values, "c.1.tilt.status", "0");
            string zoomStatus = TryGetString(values, "c.1.zoom.status", "0");

            return new CameraStatus
            {
                PanStatus = panStatus,
                TiltStatus = tiltStatus,
                ZoomStatus = zoomStatus,
                IsMoving = panStatus != "0" || tiltStatus != "0" || zoomStatus != "0"
            };
        }

        private static System.Collections.Generic.Dictionary<string, string> ParseXcKeyValueResponse(string body)
        {
            var result = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(body))
            {
                return result;
            }

            string[] lines = body.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();

                if (line.Length == 0)
                {
                    continue;
                }

                var matches = Regex.Matches(
                    line,
                    @"(?<key>[A-Za-z0-9][A-Za-z0-9._-]*)\s*(?<sep>:=|==)\s*(?<value>.*?)(?=(?:\s+[A-Za-z0-9][A-ZaZ0-9._-]*\s*(?:==|:=))|$)");

                foreach (Match match in matches)
                {
                    if (!match.Success)
                    {
                        continue;
                    }

                    string key = match.Groups["key"].Value.Trim();
                    string value = match.Groups["value"].Value.Trim();

                    if (key.Length == 0)
                    {
                        continue;
                    }

                    result[key] = value;
                }
            }

            return result;
        }

        private static int TryGetInt(System.Collections.Generic.Dictionary<string, string> values, string key)
        {
            if (values.TryGetValue(key, out string value) && int.TryParse(value, out int parsed))
            {
                return parsed;
            }

            return 0;
        }

        private static string TryGetString(System.Collections.Generic.Dictionary<string, string> values, string key, string fallback)
        {
            if (values.TryGetValue(key, out string value))
            {
                return value;
            }

            return fallback;
        }

        /// <summary>
        /// Get the AutoTrackingService instance for status checking
        /// </summary>
        public IAutoTrackingService GetAutoTrackingService()
        {
            return _autoTrackingService;
        }

        public void Dispose()
        {
            DisconnectAsync().Wait();
            if (_autoTrackingService is IDisposable disposable)
            {
                disposable.Dispose();
            }
            _httpClient?.Dispose();
        }
    }
}
