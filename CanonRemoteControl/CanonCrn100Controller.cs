using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace CanonRemoteControl
{
    public class CanonCrn100Controller : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private string _currentTrackingMode = "Aus";

        public CanonCrn100Controller(string ipAddress, string username = "admin", string password = "")
        {
            _baseUrl = $"http://{ipAddress}";
            _httpClient = new HttpClient();

            if (!string.IsNullOrEmpty(username))
            {
                var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{password}"));
                _httpClient.DefaultRequestHeaders.Authorization = 
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
            }

            _httpClient.Timeout = TimeSpan.FromSeconds(5);
        }

        public async Task<bool> PanTiltUp()
        {
            return await SendCommand("/cgi-bin/aw_ptz?cmd=%23PTS50&res=1");
        }

        public async Task<bool> PanTiltDown()
        {
            return await SendCommand("/cgi-bin/aw_ptz?cmd=%23PTT50&res=1");
        }

        public async Task<bool> PanTiltLeft()
        {
            return await SendCommand("/cgi-bin/aw_ptz?cmd=%23PLS50&res=1");
        }

        public async Task<bool> PanTiltRight()
        {
            return await SendCommand("/cgi-bin/aw_ptz?cmd=%23PRS50&res=1");
        }

        public async Task<bool> ZoomIn()
        {
            return await SendCommand("/cgi-bin/aw_ptz?cmd=%23Z50&res=1");
        }

        public async Task<bool> ZoomOut()
        {
            return await SendCommand("/cgi-bin/aw_ptz?cmd=%23Z60&res=1");
        }

        public async Task<bool> RecallPreset(int presetNumber)
        {
            return await SendCommand($"/cgi-bin/aw_ptz?cmd=%23R{presetNumber:D2}&res=1");
        }

        public async Task<bool> RecallTaufstein()
        {
            return await RecallPreset(1);
        }

        public async Task<bool> RecallAltar()
        {
            return await RecallPreset(2);
        }

        public async Task<bool> RecallKanzel()
        {
            return await RecallPreset(3);
        }

        public async Task<bool> RecallOrgel()
        {
            return await RecallPreset(4);
        }

        public async Task<bool> EnableLiveTrackSingle()
        {
            _currentTrackingMode = "Einzelperson";
            return await SendCommand("/cgi-bin/aw_ptz?cmd=%23TRK1&res=1");
        }

        public async Task<bool> EnableLiveTrackGroup()
        {
            _currentTrackingMode = "Gruppe";
            return await SendCommand("/cgi-bin/aw_ptz?cmd=%23TRK2&res=1");
        }

        public async Task<bool> DisableLiveTrack()
        {
            _currentTrackingMode = "Aus";
            return await SendCommand("/cgi-bin/aw_ptz?cmd=%23TRK0&res=1");
        }

        public string GetCurrentTrackingMode()
        {
            return _currentTrackingMode;
        }

        private async Task<bool> SendCommand(string path)
        {
            try
            {
                var response = await _httpClient.GetAsync(_baseUrl + path);
                return response.IsSuccessStatusCode;
            }
            catch (HttpRequestException)
            {
                // Kamera nicht erreichbar
                return false;
            }
            catch (TaskCanceledException)
            {
                // Timeout
                return false;
            }
            catch (Exception)
            {
                // Andere Fehler
                return false;
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
