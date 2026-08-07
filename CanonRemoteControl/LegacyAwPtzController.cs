using CanonPtzCommon;
using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace CanonRemoteControl
{
    public sealed class LegacyAwPtzController : ICanonPtzController, IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;

        public bool IsConnected { get; private set; }

        public LegacyAwPtzController(string ipAddress, int port, string username, string password, bool useHttps = false)
        {
            string scheme = useHttps ? "https" : "http";
            _baseUrl = $"{scheme}://{ipAddress}:{port}";

            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(5)
            };

            if (!string.IsNullOrWhiteSpace(username))
            {
                string raw = $"{username}:{password}";
                string credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes(raw));
                _httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
            }
        }

        public async Task<CommandResult> ConnectAsync()
        {
            CommandResult result = await SendCommandAsync("Connect", "/");
            IsConnected = result.Success;
            return result;
        }

        public Task<CommandResult> DisconnectAsync()
        {
            IsConnected = false;
            return Task.FromResult(CommandResult.Ok("Disconnect", "Legacy AW disconnect"));
        }

        public Task<CommandResult> StartPanLeftAsync()
        {
            return SendCommandAsync("PanLeft", "/cgi-bin/aw_ptz?cmd=%23PLS50&res=1");
        }

        public Task<CommandResult> StartPanRightAsync()
        {
            return SendCommandAsync("PanRight", "/cgi-bin/aw_ptz?cmd=%23PRS50&res=1");
        }

        public Task<CommandResult> StartTiltUpAsync()
        {
            return SendCommandAsync("TiltUp", "/cgi-bin/aw_ptz?cmd=%23PTS50&res=1");
        }

        public Task<CommandResult> StartTiltDownAsync()
        {
            return SendCommandAsync("TiltDown", "/cgi-bin/aw_ptz?cmd=%23PTT50&res=1");
        }

        public Task<CommandResult> StartZoomInAsync()
        {
            return SendCommandAsync("ZoomIn", "/cgi-bin/aw_ptz?cmd=%23Z50&res=1");
        }

        public Task<CommandResult> StartZoomOutAsync()
        {
            return SendCommandAsync("ZoomOut", "/cgi-bin/aw_ptz?cmd=%23Z60&res=1");
        }

        public Task<CommandResult> StopPanAsync()
        {
            return SendCommandAsync("StopPan", "/cgi-bin/aw_ptz?cmd=%23PTS50&res=1");
        }

        public Task<CommandResult> StopTiltAsync()
        {
            return SendCommandAsync("StopTilt", "/cgi-bin/aw_ptz?cmd=%23PTS50&res=1");
        }

        public Task<CommandResult> StopZoomAsync()
        {
            return SendCommandAsync("StopZoom", "/cgi-bin/aw_ptz?cmd=%23Z50&res=1");
        }

        public async Task<CommandResult> StopAllAsync()
        {
            await StopPanAsync();
            await StopTiltAsync();
            await StopZoomAsync();
            return CommandResult.Ok("StopAll", "All movements stopped");
        }

        public Task<CommandResult> RecallPresetAsync(int presetNumber)
        {
            if (presetNumber < 1 || presetNumber > 99)
            {
                return Task.FromResult(CommandResult.Fail("RecallPreset", $"Ungültige Presetnummer: {presetNumber}"));
            }

            return SendCommandAsync($"Recall Preset {presetNumber}", $"/cgi-bin/aw_ptz?cmd=%23R{presetNumber:D2}&res=1");
        }

        public Task<CommandResult> StorePresetAsync(int presetNumber)
        {
            if (presetNumber < 1 || presetNumber > 99)
            {
                return Task.FromResult(CommandResult.Fail("StorePreset", $"Ungültige Presetnummer: {presetNumber}"));
            }

            return SendCommandAsync($"Store Preset {presetNumber}", $"/cgi-bin/aw_ptz?cmd=%23M{presetNumber:D2}&res=1");
        }

        public Task<CommandResult> EnableTrackingSingleAsync()
        {
            return SendCommandAsync("Tracking Single", "/cgi-bin/aw_ptz?cmd=%23TRK1&res=1");
        }

        public Task<CommandResult> EnableTrackingGroupAsync()
        {
            return SendCommandAsync("Tracking Group", "/cgi-bin/aw_ptz?cmd=%23TRK2&res=1");
        }

        public Task<CommandResult> DisableTrackingAsync()
        {
            return SendCommandAsync("Tracking Off", "/cgi-bin/aw_ptz?cmd=%23TRK0&res=1");
        }

        public Task<CameraPosition> GetPositionAsync()
        {
            return Task.FromResult(new CameraPosition { Pan = 0, Tilt = 0, Zoom = 0 });
        }

        public Task<CameraStatus> GetStatusAsync()
        {
            return Task.FromResult(new CameraStatus { PanStatus = "unknown", TiltStatus = "unknown", ZoomStatus = "unknown", IsMoving = false });
        }

        private async Task<CommandResult> SendCommandAsync(string commandName, string path)
        {
            try
            {
                using (HttpResponseMessage response = await _httpClient.GetAsync(_baseUrl + path))
                {
                    string body = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        IsConnected = true;
                        return CommandResult.Ok(commandName, "Befehl erfolgreich", response.StatusCode, body);
                    }

                    IsConnected = false;
                    return CommandResult.Fail(
                        commandName,
                        $"HTTP-Fehler: {(int)response.StatusCode} {response.ReasonPhrase}",
                        response.StatusCode,
                        body);
                }
            }
            catch (TaskCanceledException ex)
            {
                IsConnected = false;
                return CommandResult.Fail(commandName, "Timeout beim Kamerazugriff", exception: ex);
            }
            catch (HttpRequestException ex)
            {
                IsConnected = false;
                return CommandResult.Fail(commandName, "HTTP-Verbindungsfehler", exception: ex);
            }
            catch (Exception ex)
            {
                IsConnected = false;
                return CommandResult.Fail(commandName, "Unerwarteter Fehler", exception: ex);
            }
        }

        private static int ClampSpeed(int speed)
        {
            if (speed < 1) return 1;
            if (speed > 99) return 99;
            return speed;
        }

        public Task<CommandResult> SetStandbyAsync(bool standby)
        {
            // No-op implementation for legacy controller
            return Task.FromResult(CommandResult.Fail("SetStandby", "Standby mode is not supported by Legacy AW controller"));
        }

        public void Dispose()
        {
            _httpClient.Dispose();
        }
    }
}
