using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using JokiAutomation;
using NUnit.Framework;

namespace JokiAutomation.Tests
{
    [TestFixture]
    public sealed class DropboxApiUploaderTests
    {
        [Test]
        [Category("Dropbox")]
        public async Task UploadFileAsync_SendsCorrectAuthorizationHeader()
        {
            // Arrange
            string filePath = Path.Combine(Path.GetTempPath(), "JokiAutomationUploadTest_" + Guid.NewGuid().ToString("N") + ".log");
            const string fileContent = "test-log-content";
            const string targetFileName = "20260101_120000_test.log";
            const string appKey = "test_app_key";
            const string appSecret = "test_app_secret";
            const string refreshToken = "test_refresh_token";
            var handler = new FakeCombinedHandler();
            var httpClient = new HttpClient(handler);

            try
            {
                File.WriteAllText(filePath, fileContent);

                // Act
                var uploader = new DropboxApiUploader(httpClient, appKey, appSecret, refreshToken);
                bool uploaded = await uploader.UploadFileAsync(filePath, targetFileName);

                // Assert
                Assert.That(uploaded, Is.True);
                Assert.That(handler.WasTokenRequested, Is.True, "Token endpoint should have been called");
                Assert.That(handler.ReceivedBearerToken, Is.Not.Empty, "Bearer token should be present in upload request");
                Assert.That(handler.DropboxApiArgHeaderValue, Does.Contain(targetFileName), "Dropbox-API-Arg should contain target filename");
            }
            finally
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
            }
        }

        [Test]
        [Category("Dropbox")]
        public async Task UploadFileAsync_ThrowsWhenFileNotFound()
        {
            // Arrange
            const string missingFilePath = "C:\\nonexistent\\file.log";
            const string appKey = "test_app_key";
            const string appSecret = "test_app_secret";
            const string refreshToken = "test_refresh_token";

            var uploader = new DropboxApiUploader(new HttpClient(), appKey, appSecret, refreshToken);

            // Act & Assert
            var ex = Assert.ThrowsAsync<FileNotFoundException>(async () => 
                await uploader.UploadFileAsync(missingFilePath, "targetfile.log"));
            Assert.That(ex.Message, Does.Contain("File not found"));
        }

        [Test]
        [Category("Dropbox")]
        [Explicit("Integration test - requires valid Dropbox API credentials")]
        public async Task UploadFileAsync_WithRealDropboxApiCredentials()
        {
            if (!TryLoadDropboxCredentials(out string appKey, out string appSecret, out string refreshToken, out string configPath))
            {
                Assert.Ignore("Dropbox API credentials not configured in Network.cfg");
            }

            TestContext.WriteLine($"Using Network.cfg from: {configPath}");

            string testFilePath = Path.Combine(Path.GetTempPath(), "JokiAutomationTest_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log");
            string testFileContent = "JokiAutomation Test Log - " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            try
            {
                File.WriteAllText(testFilePath, testFileContent);
                TestContext.WriteLine($"Created test file: {testFilePath}");
                TestContext.WriteLine($"File size: {new FileInfo(testFilePath).Length} bytes");

                using (var httpClient = new HttpClient())
                {
                    httpClient.Timeout = TimeSpan.FromSeconds(30);
                    var uploader = new DropboxApiUploader(httpClient, appKey, appSecret, refreshToken);
                    bool uploadSuccess = await uploader.UploadFileAsync(testFilePath, Path.GetFileName(testFilePath));

                    Assert.That(uploadSuccess, Is.True, "Upload to Dropbox API failed");
                    TestContext.WriteLine("Upload successful!");
                }
            }
            finally
            {
                if (File.Exists(testFilePath))
                {
                    File.Delete(testFilePath);
                    TestContext.WriteLine($"Cleaned up test file: {testFilePath}");
                }
            }
        }

        private static bool TryLoadDropboxCredentials(out string appKey, out string appSecret, out string refreshToken, out string configPath)
        {
            appKey = null;
            appSecret = null;
            refreshToken = null;
            configPath = FindNetworkConfigPath();

            if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
            {
                return false;
            }

            foreach (string rawLine in File.ReadAllLines(configPath))
            {
                if (string.IsNullOrWhiteSpace(rawLine))
                {
                    continue;
                }

                string line = rawLine.Trim();
                if (line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                string[] parts = line.Split(new[] { ';' }, 2);
                if (parts.Length != 2)
                {
                    continue;
                }

                string key = parts[0].Trim();
                string value = parts[1].Trim();

                if (key.Equals("DROPBOX_APP_KEY", StringComparison.OrdinalIgnoreCase))
                {
                    appKey = value;
                    continue;
                }

                if (key.Equals("DROPBOX_APP_SECRET", StringComparison.OrdinalIgnoreCase))
                {
                    appSecret = value;
                    continue;
                }

                if (key.Equals("DROPBOX_REFRESH_TOKEN", StringComparison.OrdinalIgnoreCase))
                {
                    refreshToken = value;
                }
            }

            return !string.IsNullOrWhiteSpace(appKey)
                && !string.IsNullOrWhiteSpace(appSecret)
                && !string.IsNullOrWhiteSpace(refreshToken)
                && !appKey.StartsWith("your_", StringComparison.OrdinalIgnoreCase)
                && !appSecret.StartsWith("your_", StringComparison.OrdinalIgnoreCase)
                && !refreshToken.StartsWith("your_", StringComparison.OrdinalIgnoreCase);
        }

        private static string FindNetworkConfigPath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            string[] candidates =
            {
                Path.Combine(baseDir, "Network.cfg"),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "JokiAutomation", "Network.cfg")),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "JokiAutomation", "Network.cfg")),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "..", "JokiAutomation", "Network.cfg"))
            };

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        [Test]
        [Category("Dropbox")]
        public void Constructor_ThrowsWhenAppKeyIsNull()
        {
            var ex = Assert.Throws<ArgumentException>(() =>
                new DropboxApiUploader(new HttpClient(), null, "secret", "token"));
            Assert.That(ex.ParamName, Is.EqualTo("appKey"));
        }

        [Test]
        [Category("Dropbox")]
        public void Constructor_ThrowsWhenAppSecretIsNull()
        {
            var ex = Assert.Throws<ArgumentException>(() =>
                new DropboxApiUploader(new HttpClient(), "key", null, "token"));
            Assert.That(ex.ParamName, Is.EqualTo("appSecret"));
        }

        [Test]
        [Category("Dropbox")]
        public void Constructor_ThrowsWhenRefreshTokenIsNull()
        {
            var ex = Assert.Throws<ArgumentException>(() =>
                new DropboxApiUploader(new HttpClient(), "key", "secret", null));
            Assert.That(ex.ParamName, Is.EqualTo("refreshToken"));
        }

        /// <summary>
        /// Fake handler that handles both OAuth2 token and file upload endpoints.
        /// </summary>
        private sealed class FakeCombinedHandler : HttpMessageHandler
        {
            public bool WasTokenRequested { get; private set; }
            public string ReceivedBearerToken { get; private set; }
            public string DropboxApiArgHeaderValue { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                // Handle token endpoint
                if (request.RequestUri.Host == "api.dropboxapi.com" && 
                    request.RequestUri.PathAndQuery.Contains("oauth2/token"))
                {
                    WasTokenRequested = true;

                    // Return a valid token response
                    string responseContent = "{\"access_token\": \"test_access_token_12345\", \"expires_in\": 3600, \"token_type\": \"bearer\"}";

                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(responseContent, System.Text.Encoding.UTF8, "application/json")
                    };
                }

                // Handle upload endpoint
                if (request.RequestUri.Host == "content.dropboxapi.com" &&
                    request.RequestUri.PathAndQuery.Contains("2/files/upload"))
                {
                    // Capture Bearer token
                    if (request.Headers.Authorization != null)
                    {
                        ReceivedBearerToken = request.Headers.Authorization.Parameter;
                    }

                    // Capture Dropbox-API-Arg header
                    if (request.Headers.TryGetValues("Dropbox-API-Arg", out var headerValues))
                    {
                        DropboxApiArgHeaderValue = string.Join(",", headerValues);
                    }

                    // Return success
                    string json = "{\"name\": \"test.log\", \"path_display\": \"/JokiAutomation_Logs/test.log\", \"id\": \"id:test123\"}";

                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
                    };
                }

                return await Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
        }
    }
}
