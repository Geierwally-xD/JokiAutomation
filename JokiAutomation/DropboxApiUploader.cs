using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace JokiAutomation
{
    /// <summary>
    /// Uploads files to Dropbox using the official Dropbox API v2 with OAuth2 authentication.
    /// </summary>
    public sealed class DropboxApiUploader
    {
        private readonly HttpClient _httpClient;
        private readonly string _appKey;
        private readonly string _appSecret;
        private readonly string _refreshToken;
        private string _accessToken;
        private DateTime _accessTokenExpiry;

        private const string TokenUrl = "https://api.dropboxapi.com/oauth2/token";
        private const string UploadUrl = "https://content.dropboxapi.com/2/files/upload";
        private const string DropboxFolder = "/JokiAutomation_Logs";

        public DropboxApiUploader(HttpClient httpClient, string appKey, string appSecret, string refreshToken)
        {
            if (httpClient == null)
            {
                throw new ArgumentNullException(nameof(httpClient));
            }

            if (string.IsNullOrWhiteSpace(appKey))
            {
                throw new ArgumentException("App key is required.", nameof(appKey));
            }

            if (string.IsNullOrWhiteSpace(appSecret))
            {
                throw new ArgumentException("App secret is required.", nameof(appSecret));
            }

            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                throw new ArgumentException("Refresh token is required.", nameof(refreshToken));
            }

            _httpClient = httpClient;
            _appKey = appKey;
            _appSecret = appSecret;
            _refreshToken = refreshToken;
            _accessToken = null;
            _accessTokenExpiry = DateTime.MinValue;
        }

        /// <summary>
        /// Uploads a file to Dropbox asynchronously.
        /// </summary>
        /// <param name="localFilePath">Full path to the local file to upload.</param>
        /// <param name="targetFileName">Target filename on Dropbox (without path).</param>
        /// <returns>True if upload succeeded, false otherwise.</returns>
        public async Task<bool> UploadFileAsync(string localFilePath, string targetFileName)
        {
            if (string.IsNullOrWhiteSpace(localFilePath))
            {
                throw new ArgumentException("Local file path is required.", nameof(localFilePath));
            }

            if (string.IsNullOrWhiteSpace(targetFileName))
            {
                throw new ArgumentException("Target file name is required.", nameof(targetFileName));
            }

            if (!File.Exists(localFilePath))
            {
                throw new FileNotFoundException($"File not found: {localFilePath}");
            }

            try
            {
                // Refresh access token if needed
                if (DateTime.UtcNow >= _accessTokenExpiry)
                {
                    bool tokenRefreshed = await RefreshAccessTokenAsync();
                    if (!tokenRefreshed)
                    {
                        System.Diagnostics.Debug.WriteLine("Failed to refresh Dropbox access token");
                        return false;
                    }
                }

                // Upload file to Dropbox
                return await UploadToDropboxAsync(localFilePath, targetFileName);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Dropbox upload error: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> RefreshAccessTokenAsync()
        {
            try
            {
                var requestBody = new StringBuilder();
                requestBody.Append("grant_type=refresh_token");
                requestBody.Append("&refresh_token=").Append(Uri.EscapeDataString(_refreshToken));
                requestBody.Append("&client_id=").Append(Uri.EscapeDataString(_appKey));
                requestBody.Append("&client_secret=").Append(Uri.EscapeDataString(_appSecret));

                using (var request = new HttpRequestMessage(HttpMethod.Post, TokenUrl))
                {
                    request.Content = new StringContent(requestBody.ToString(), Encoding.UTF8, "application/x-www-form-urlencoded");

                    using (HttpResponseMessage response = await _httpClient.SendAsync(request))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            string errorContent = await response.Content.ReadAsStringAsync();
                            System.Diagnostics.Debug.WriteLine($"Token refresh failed: {response.StatusCode} - {errorContent}");
                            return false;
                        }

                        string responseContent = await response.Content.ReadAsStringAsync();
                        return ExtractTokenFromJson(responseContent);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Token refresh exception: {ex.Message}");
                return false;
            }
        }

        private bool ExtractTokenFromJson(string jsonContent)
        {
            try
            {
                // Extract access_token using regex
                var tokenMatch = Regex.Match(jsonContent, @"""access_token""\s*:\s*""([^""]+)""");
                if (!tokenMatch.Success)
                    return false;

                _accessToken = tokenMatch.Groups[1].Value;

                // Extract expires_in using regex
                var expiresMatch = Regex.Match(jsonContent, @"""expires_in""\s*:\s*(\d+)");
                if (expiresMatch.Success && int.TryParse(expiresMatch.Groups[1].Value, out int expiresInSeconds))
                {
                    _accessTokenExpiry = DateTime.UtcNow.AddSeconds(expiresInSeconds - 60); // Refresh 60 seconds early
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Token extraction error: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> UploadToDropboxAsync(string localFilePath, string targetFileName)
        {
            try
            {
                string dropboxPath = $"{DropboxFolder}/{targetFileName}";

                using (var fileStream = new FileStream(localFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var request = new HttpRequestMessage(HttpMethod.Post, UploadUrl))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

                    // Dropbox API v2 requires path and mode in Dropbox-API-Arg header as JSON
                    string apiArgJson = $@"{{""path"":""{EscapeJsonString(dropboxPath)}"",""mode"":""add"",""autorename"":true,""mute"":false,""strict_conflict"":false}}";
                    request.Headers.Add("Dropbox-API-Arg", apiArgJson);
                    request.Content = new StreamContent(fileStream);
                    request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");

                    using (HttpResponseMessage response = await _httpClient.SendAsync(request))
                    {
                        if (response.IsSuccessStatusCode)
                        {
                            System.Diagnostics.Debug.WriteLine($"File uploaded successfully to {dropboxPath}");
                            return true;
                        }
                        else
                        {
                            string errorContent = await response.Content.ReadAsStringAsync();
                            System.Diagnostics.Debug.WriteLine($"Upload failed: {response.StatusCode} - {errorContent}");
                            return false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Upload exception: {ex.Message}");
                return false;
            }
        }

        private static string EscapeJsonString(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;

            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }
    }
}
