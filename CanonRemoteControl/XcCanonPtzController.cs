using CanonPtzCommon;
using CanonRemoteControl.Services;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
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
        private System.Threading.Timer _sessionKeepAliveTimer;
        private const int SessionKeepAliveIntervalMs = 30000; // 30 Sekunden (Session timeout ist meist 60s)
        private bool _isRefreshingSession = false;
        private readonly System.Threading.SemaphoreSlim _sessionSemaphore = new System.Threading.SemaphoreSlim(1, 1);

        // Shutdown state machine:
        // 0 = verbunden / noch nicht getrennt
        // 1 = Disconnect läuft
        // 2 = Disconnect abgeschlossen
        private int _disconnectState;
        private int _disposeState;
        private int _disposeRequested;
        private int _resourcesDisposed;

        private static readonly string _debugLogPath = Path.Combine(
            Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "",
            "PtzDebugLog.txt");

        public bool IsConnected { get; private set; }

        private void DebugLog(string message)
        {
#if DEBUG
            string logMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [XcCanonPtzController] {message}";
            
            // Output to Debug window
            System.Diagnostics.Debug.WriteLine(logMessage);
            
            // Write to file
            try
            {
                File.AppendAllText(_debugLogPath, logMessage + Environment.NewLine);
            }
            catch
            {
                // Ignore file logging errors to prevent disrupting the application
            }
#endif
        }

        private static string SanitizeForLog(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            return Regex.Replace(value, @"([?&]s=)[^&]+", "$1***", RegexOptions.IgnoreCase);
        }

        private void StopKeepAliveTimerIfRunning()
        {
            var timer = Interlocked.Exchange(ref _sessionKeepAliveTimer, null);
            if (timer != null)
            {
                timer.Dispose();
                DebugLog("Keep-alive timer stopped");
            }
        }

        private void DisposeResourcesOnce()
        {
            if (Interlocked.CompareExchange(ref _resourcesDisposed, 1, 0) != 0)
            {
                return;
            }

            StopKeepAliveTimerIfRunning();

            // Give in-flight refresh callback a short window to leave its critical section
            bool refreshCompleted = SpinWait.SpinUntil(() => !_isRefreshingSession, 500);
            if (!refreshCompleted)
            {
                DebugLog("Dispose continuing although refresh is still marked active");
            }

            if (_autoTrackingService is IDisposable disposable)
            {
                disposable.Dispose();
            }

            _httpClient?.Dispose();
            _sessionSemaphore?.Dispose();
            DebugLog("Controller disposed");
        }

        public XcCanonPtzController(CameraConfig config)
        {
            DebugLog("Constructor called");
            
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
            DebugLog($"BaseUrl: {_baseUrl}");

            if (!string.IsNullOrEmpty(_config.Username))
            {
                var credentials = Convert.ToBase64String(
                    Encoding.ASCII.GetBytes($"{_config.Username}:{_config.Password}"));
                _httpClient.DefaultRequestHeaders.Authorization = 
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
                DebugLog("Basic Auth configured");
            }
            else
            {
                DebugLog("No authentication configured");
            }

            // Initialize RA-AT001 Auto Tracking service
            _autoTrackingService = new AutoTrackingService(_config);
        }

        public async Task<CommandResult> ConnectAsync()
        {
            DebugLog("ConnectAsync called");
            
            try
            {
                // Check if another instance already has a session
                string existingSessionId = SharedSessionState.GetSessionId();
                if (!string.IsNullOrEmpty(existingSessionId))
                {
                    DebugLog("Found existing shared session");
                    
                    // Try to claim the existing session
                    var claimResult = await SendCgiRequestAsync($"claim.cgi?s={existingSessionId}", "SessionClaimExisting");
                    if (claimResult.Success)
                    {
                        _sessionId = existingSessionId;
                        IsConnected = true;
                        _reconnectAttempts = 0;
                        Volatile.Write(ref _disconnectState, 0);
                        
                        // Start session keep-alive timer
                        StartSessionKeepAlive();
                        
                        DebugLog("Connected using existing shared session");
                        return CommandResult.Ok("Connect", "Verbindung hergestellt (Shared Session)");
                    }
                    else
                    {
                        DebugLog($"Could not claim existing session: {claimResult.Message}");
                        // Fall through to create new session
                    }
                }
                
                // Create new session
                var openResult = await SendCgiRequestAsync("open.cgi", "SessionOpen");

                if (!openResult.Success)
                {
                    IsConnected = false;
                    DebugLog("Connect failed, IsConnected=false");
                    return openResult;
                }

                _sessionId = ExtractSessionId(openResult.ResponseBody);
                DebugLog("SessionId extracted");

                // Share the session ID with other instances
                SharedSessionState.SetSessionId(_sessionId);
                DebugLog("Session ID stored in shared state");

                IsConnected = true;
                _reconnectAttempts = 0;
                Volatile.Write(ref _disconnectState, 0);
                
                // Start session keep-alive timer
                StartSessionKeepAlive();
                
                DebugLog("Connect successful, IsConnected=true");

                return CommandResult.Ok("Connect", "Verbindung hergestellt");
            }
            catch (Exception ex)
            {
                IsConnected = false;
                DebugLog($"Connect exception: {ex.Message}");
                return CommandResult.Fail("Connect", "Verbindungsfehler", exception: ex);
            }
        }

        private void StartSessionKeepAlive()
        {
            StopKeepAliveTimerIfRunning();
            
            var timer = new System.Threading.Timer(
                async _ => await RefreshSessionAsync(),
                null,
                SessionKeepAliveIntervalMs,
                SessionKeepAliveIntervalMs);

            Interlocked.Exchange(ref _sessionKeepAliveTimer, timer);
            DebugLog("Session keep-alive timer started (30s interval)");
        }

        private async Task RefreshSessionAsync()
        {
            // Prevent concurrent refresh attempts
            if (_isRefreshingSession)
            {
                DebugLog("Session refresh already in progress, skipping...");
                return;
            }

            if (!IsConnected || string.IsNullOrEmpty(_sessionId))
            {
                DebugLog("Session refresh skipped: not connected or no session ID");
                return;
            }

            bool semaphoreAcquired = false;
            try
            {
                // Try to acquire lock, but don't wait - if busy, skip this refresh
                semaphoreAcquired = await _sessionSemaphore.WaitAsync(0);
                if (!semaphoreAcquired)
                {
                    DebugLog("Session refresh skipped: session is busy");
                    return;
                }

                _isRefreshingSession = true;

                DebugLog("Checking session validity with info.cgi...");

                // Use info.cgi as keep-alive ping - lightweight and doesn't interfere
                var pingResult = await SendCgiRequestAsync($"info.cgi?s={_sessionId}&item=c.1.zoom", "SessionKeepAlive");

                // Avoid double recovery: request layer may already handle reconnect on transport failures.
                if (!pingResult.Success)
                {
                    DebugLog("Session keep-alive request failed; reconnect handled by request layer");
                    return;
                }

                // Recover only on explicit invalid-session response.
                if (pingResult.ResponseBody != null && pingResult.ResponseBody.Contains("Unknown Connection ID"))
                {
                    DebugLog("Session is invalid (Unknown Connection ID), attempting to recover...");

                    // Session expired or taken over by RA-AT001, rebuild it
                    await HandleSessionLostAsync();
                    return;
                }

                DebugLog("Session is valid and active");
            }
            catch (ObjectDisposedException)
            {
                DebugLog("Session refresh skipped: semaphore already disposed during shutdown");
            }
            catch (Exception ex)
            {
                DebugLog($"Session refresh error: {ex.Message}");
            }
            finally
            {
                _isRefreshingSession = false;

                if (semaphoreAcquired)
                {
                    try
                    {
                        _sessionSemaphore.Release();
                    }
                    catch (ObjectDisposedException)
                    {
                        DebugLog("Session refresh release skipped: semaphore already disposed");
                    }
                }
            }
        }

        private async Task HandleSessionLostAsync()
        {
            DebugLog("Session lost, attempting to rebuild...");
            
            // Stop keep-alive timer temporarily
            StopKeepAliveTimerIfRunning();
            
            // Mark as disconnected
            IsConnected = false;
            string oldSessionId = _sessionId;
            _sessionId = null;
            
            try
            {
                // Try to close old session (might fail, that's ok)
                if (!string.IsNullOrEmpty(oldSessionId))
                {
                    try
                    {
                        await SendCgiRequestAsync($"close.cgi?s={oldSessionId}", "SessionCloseOld");
                    }
                    catch
                    {
                        // Ignore errors when closing invalid session
                    }
                }
                
                // Wait a bit before reconnecting
                await Task.Delay(1000);
                
                // Rebuild session
                var reconnectResult = await ConnectAsync();
                
                if (reconnectResult.Success)
                {
                    DebugLog("Session rebuilt successfully");
                }
                else
                {
                    DebugLog($"Session rebuild failed: {reconnectResult.Message}");
                }
            }
            catch (Exception ex)
            {
                DebugLog($"HandleSessionLostAsync exception: {ex.Message}");
            }
        }

        public async Task<CommandResult> DisconnectAsync()
        {
            DebugLog("Disconnect requested");

            int previousState = Interlocked.CompareExchange(ref _disconnectState, 1, 0);
            if (previousState != 0)
            {
                DebugLog(previousState == 1
                    ? "Disconnect already running"
                    : "Disconnect already completed");

                return CommandResult.Ok("Disconnect", "Canon PTZ wurde bereits getrennt oder wird gerade getrennt.");
            }

            try
            {
                // Keep-alive idempotent stoppen
                StopKeepAliveTimerIfRunning();

                // Session-ID atomar entnehmen und sofort lokal invalidieren
                string sessionIdForYield = Interlocked.Exchange(ref _sessionId, null);

                if (!string.IsNullOrEmpty(sessionIdForYield))
                {
                    DebugLog("SessionYield started");
                    await SendCgiRequestAsync($"yield.cgi?s={sessionIdForYield}", "SessionYield");
                    DebugLog("SessionYield completed");
                }

                IsConnected = false;
                DebugLog("Session state cleared");
                return CommandResult.Ok("Disconnect", "Verbindung getrennt");
            }
            catch (Exception ex)
            {
                IsConnected = false;
                return CommandResult.Fail("Disconnect", "Fehler beim Trennen", exception: ex);
            }
            finally
            {
                Volatile.Write(ref _disconnectState, 2);

                if (Volatile.Read(ref _disposeRequested) == 1)
                {
                    DisposeResourcesOnce();
                }
            }
        }

        public Task<CommandResult> StartPanLeftAsync()
        {
            DebugLog("StartPanLeftAsync called");
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
            if (!IsConnected)
            {
                return CommandResult.Fail("RecallPreset", "Nicht verbunden");
            }

            // Acquire session lock
            await _sessionSemaphore.WaitAsync();
            
            try
            {
                // Try with session parameter
                if (!string.IsNullOrEmpty(_sessionId))
                {
                    string path = $"control.cgi?s={_sessionId}&p={presetNumber}";
                    var result = await SendCgiRequestAsync(path, $"RecallPreset{presetNumber}");
                    
                    // Check if session is invalid (Unknown Connection ID)
                    if (!result.Success && result.ResponseBody != null && 
                        result.ResponseBody.Contains("Unknown Connection ID"))
                    {
                        DebugLog($"Session invalid during RecallPreset, checking if Auto-Tracking is active...");
                        
                        // Check if tracking is the culprit
                        bool trackingActive = await _autoTrackingService.IsEnabledAsync();
                        if (trackingActive)
                        {
                            return CommandResult.Fail("RecallPreset", 
                                "Preset kann nicht angefahren werden!\n\n" +
                                "Auto-Tracking ist aktiv.\n" +
                                "Bitte zuerst Auto-Tracking deaktivieren.");
                        }
                        
                        // Tracking not active, just reconnect
                        DebugLog("Auto-Tracking not active, reconnecting...");
                        var reconnectResult = await ConnectAsync();
                        if (!reconnectResult.Success)
                        {
                            return CommandResult.Fail("RecallPreset", 
                                $"Session ungültig, Neuverbindung fehlgeschlagen: {reconnectResult.Message}");
                        }
                        
                        // Retry with new session
                        path = $"control.cgi?s={_sessionId}&p={presetNumber}";
                        result = await SendCgiRequestAsync(path, $"RecallPreset{presetNumber}");
                    }
                    
                    return result;
                }

                // Fallback ohne session
                return await SendControlParametersAsync($"p={presetNumber}", $"RecallPreset{presetNumber}");
            }
            finally
            {
                _sessionSemaphore.Release();
            }
        }

        public async Task<CommandResult> StorePresetAsync(int presetNumber)
        {
            if (!IsConnected)
            {
                return CommandResult.Fail("StorePreset", "Nicht verbunden");
            }

            // Acquire session lock
            await _sessionSemaphore.WaitAsync();
            
            try
            {
                // Check if session ID is valid before attempting
                if (string.IsNullOrEmpty(_sessionId))
                {
                    DebugLog("Session ID is empty, attempting to reconnect...");
                    
                    var reconnectResult = await ConnectAsync();
                    if (!reconnectResult.Success)
                    {
                        return CommandResult.Fail("StorePreset", 
                            $"Keine Session, Neuverbindung fehlgeschlagen: {reconnectResult.Message}");
                    }
                }

                // Try to store preset
                string path = $"preset/set?s={_sessionId}&p={presetNumber}&all=enabled";
                var result = await SendCgiRequestAsync(path, $"StorePreset{presetNumber}");
                
                // Check if session is invalid (Unknown Connection ID)
                if (!result.Success && result.ResponseBody != null && 
                    result.ResponseBody.Contains("Unknown Connection ID"))
                {
                    DebugLog($"Session invalid during StorePreset, reconnecting...");
                    
                    // Session expired, reconnect and retry
                    var reconnectResult = await ConnectAsync();
                    if (!reconnectResult.Success)
                    {
                        return CommandResult.Fail("StorePreset", 
                            $"Session ungültig, Neuverbindung fehlgeschlagen: {reconnectResult.Message}");
                    }
                    
                    // Retry with new session
                    path = $"preset/set?s={_sessionId}&p={presetNumber}&all=enabled";
                    result = await SendCgiRequestAsync(path, $"StorePreset{presetNumber}");
                }
                
                // If preset 1 was stored successfully, update Auto-Tracking home position
                if (result.Success && presetNumber == 1)
                {
                    DebugLog("Preset 1 stored - updating Auto-Tracking home position");
                    
                    // Read current position
                    var xcPosition = await GetPositionAsync();
                    if (xcPosition != null)
                    {
                        var trackInfo = new TrackInfo
                        {
                            Pan = xcPosition.Pan,
                            Tilt = xcPosition.Tilt,
                            Zoom = xcPosition.Zoom
                        };

                        string homePosition = trackInfo.ToPtzHomePosition();
                        DebugLog($"Setting Auto-Tracking home position to: {homePosition}");
                        
                        // Check if tracking is enabled first
                        bool trackingEnabled = await _autoTrackingService.IsEnabledAsync();
                        if (trackingEnabled)
                        {
                            // Set home position (only works when tracking is enabled)
                            var homeResult = await _autoTrackingService.SetHomePositionAsync(homePosition);
                            if (homeResult.Success)
                            {
                                DebugLog("Auto-Tracking home position updated successfully");
                            }
                            else
                            {
                                DebugLog($"Warning: Could not update home position: {homeResult.Message}");
                            }
                        }
                        else
                        {
                            DebugLog("Auto-Tracking not enabled - home position not updated");
                        }
                    }
                }
                
                return result;
            }
            finally
            {
                _sessionSemaphore.Release();
            }
        }

        public Task<CommandResult> EnableTrackingSingleAsync()
        {
            // Verwende die komplette Altar-Position-Setup-Methode
            return EnableAutoTrackingAtAltarAsync();
        }

        public Task<CommandResult> EnableTrackingGroupAsync()
        {
            // RA-AT001 unterscheidet nicht zwischen Single und Group
            // Beide verwenden die gleiche Altar-Position-Setup-Methode
            return EnableAutoTrackingAtAltarAsync();
        }

        public async Task<CommandResult> DisableTrackingAsync()
        {
            try
            {
                DebugLog("DisableTrackingAsync - Start");
                
                // Disable Auto Tracking via RA-AT001 API
                var disableResult = await _autoTrackingService.DisableAsync();
                
                if (!disableResult.Success)
                {
                    return disableResult;
                }
                
                DebugLog("Auto-Tracking disabled, rebuilding XC session...");
                
                // WICHTIG: Nach Auto-Tracking deaktivieren MUSS die Session neu aufgebaut werden
                // Die RA-AT001 App hat die alte Session übernommen/ungültig gemacht
                
                // 1. Alte Session beenden (falls noch vorhanden)
                if (!string.IsNullOrEmpty(_sessionId))
                {
                    try
                    {
                        await SendCgiRequestAsync($"close.cgi?s={_sessionId}", "SessionCloseAfterTracking");
                    }
                    catch
                    {
                        // Ignorieren - Session ist eh ungültig
                    }
                }
                
                // 2. Disconnect komplett
                IsConnected = false;
                _sessionId = null;
                
                // Stop keep-alive timer
                StopKeepAliveTimerIfRunning();
                
                // 3. Kurz warten
                await Task.Delay(1000);
                
                // 4. Komplett neu verbinden
                var reconnectResult = await ConnectAsync();
                
                if (!reconnectResult.Success)
                {
                    return CommandResult.Fail(
                        "DisableTracking",
                        $"Auto-Tracking deaktiviert, aber XC Session konnte nicht aufgebaut werden: {reconnectResult.Message}");
                }
                
                DebugLog("DisableTrackingAsync - Success, XC session re-established");
                
                return CommandResult.Ok(
                    "DisableTracking",
                    "Auto-Tracking deaktiviert, PTZ-Steuerung wieder verfügbar");
            }
            catch (Exception ex)
            {
                DebugLog($"DisableTrackingAsync - Exception: {ex.Message}");
                return CommandResult.Fail(
                    "DisableTracking",
                    "Fehler beim Deaktivieren",
                    exception: ex);
            }
        }

        /// <summary>
        /// Enable Auto Tracking at Altar position - just moves to preset, does NOT set home position
        /// </summary>
        /// <returns>CommandResult indicating success or failure</returns>
        public async Task<CommandResult> EnableAutoTrackingAtAltarAsync()
        {
            try
            {
                DebugLog("EnableAutoTrackingAtAltarAsync - Start");

                // 1. Sicher stellen, dass Tracking ausgeschaltet ist
                bool isEnabled = await _autoTrackingService.IsEnabledAsync();
                if (isEnabled)
                {
                    DebugLog("Disabling tracking first...");
                    var disableResult = await _autoTrackingService.DisableAsync();
                    if (!disableResult.Success)
                    {
                        return CommandResult.Fail(
                            "EnableAutoTrackingAtAltar",
                            "Tracking konnte nicht deaktiviert werden: " + disableResult.Message);
                    }
                    await Task.Delay(500);
                }

                // 2. Altar-Preset anfahren (Home Position wird NICHT gesetzt, nur angefahren!)
                DebugLog($"Moving to altar preset {_config.AutoTrackingHomePreset}...");
                var presetResult = await RecallPresetAsync(_config.AutoTrackingHomePreset);
                if (!presetResult.Success)
                {
                    return CommandResult.Fail(
                        "EnableAutoTrackingAtAltar",
                        $"Altarposition (Preset {_config.AutoTrackingHomePreset}) konnte nicht angefahren werden: {presetResult.Message}");
                }

                // 3. Warten bis Kamera Preset erreicht hat
                DebugLog($"Waiting {_config.AutoTrackingStartupDelayMs}ms for camera to reach position...");
                await Task.Delay(_config.AutoTrackingStartupDelayMs);
                
                // 4. WICHTIG: Session YIELDEN bevor Auto-Tracking aktiviert wird
                // RA-AT001 App braucht die Session-Kontrolle
                if (!string.IsNullOrEmpty(_sessionId))
                {
                    DebugLog("Yielding session before enabling Auto-Tracking...");
                    await SendCgiRequestAsync($"yield.cgi?s={_sessionId}", "SessionYieldForTracking");
                    await Task.Delay(200);
                }
                
                // 5. Tracking einschalten
                DebugLog("Enabling auto tracking...");
                var enableResult = await _autoTrackingService.EnableAsync();
                if (!enableResult.Success)
                {
                    // Check for "application must be started" error
                    if (enableResult.ResponseBody != null && enableResult.ResponseBody.Contains("E4_003"))
                    {
                        return CommandResult.Fail(
                            "EnableAutoTrackingAtAltar",
                            "FEHLER: Auto-Tracking Applikation ist nicht gestartet!\n\n" +
                            "Bitte in der Kamera-Weboberfläche:\n" +
                            "1. 'Remote Webcam & Automation' öffnen\n" +
                            "2. 'RA-AT001 Auto Tracking' App starten\n" +
                            "3. Dann erneut versuchen");
                    }
                    
                    // Claim session back if tracking enable failed
                    if (!string.IsNullOrEmpty(_sessionId))
                    {
                        await SendCgiRequestAsync($"claim.cgi?s={_sessionId}", "SessionClaimAfterTrackingFail");
                    }
                    
                    return CommandResult.Fail(
                        "EnableAutoTrackingAtAltar",
                        "Auto Tracking konnte nicht aktiviert werden: " + enableResult.Message);
                }

                // 6. Recovery Control aktivieren (optional, wenn Tracking läuft)
                DebugLog($"Enabling recovery control ({_config.AutoTrackingRecoveryTimeSeconds}s)...");
                var recoveryResult = await _autoTrackingService.EnableRecoveryControlAsync(_config.AutoTrackingRecoveryTimeSeconds);
                if (!recoveryResult.Success)
                {
                    // Warning only, not critical
                    DebugLog($"Recovery control warning: {recoveryResult.Message}");
                }

                // 7. Session zurückholt für manuelle PTZ-Steuerung
                if (!string.IsNullOrEmpty(_sessionId))
                {
                    DebugLog("Reclaiming session after Auto-Tracking enable...");
                    var claimResult = await SendCgiRequestAsync($"claim.cgi?s={_sessionId}", "SessionClaimAfterTracking");
                    if (!claimResult.Success)
                    {
                        // Session ist ungültig, neu verbinden
                        DebugLog("Session invalid, reconnecting...");
                        await DisconnectAsync();
                        await Task.Delay(500);
                        var reconnectResult = await ConnectAsync();
                        if (!reconnectResult.Success)
                        {
                            DebugLog($"Warning: Could not re-establish session: {reconnectResult.Message}");
                        }
                    }
                }

                DebugLog("EnableAutoTrackingAtAltarAsync - Success");

                return CommandResult.Ok(
                    "EnableAutoTrackingAtAltar",
                    $"Auto Tracking aktiviert (Preset {_config.AutoTrackingHomePreset} angefahren)");
            }
            catch (Exception ex)
            {
                DebugLog($"EnableAutoTrackingAtAltarAsync - Exception: {ex.Message}");
                return CommandResult.Fail(
                    "EnableAutoTrackingAtAltar",
                    "Fehler beim Aktivieren",
                    exception: ex);
            }
        }

        private async Task<CommandResult> EnableAutoTrackingAtStoredHomeAsync()
        {
            try
            {
                DebugLog("EnableAutoTrackingAtStoredHomeAsync - Start");
                
                // 1. Disable tracking if enabled
                bool isEnabled = await _autoTrackingService.IsEnabledAsync();
                if (isEnabled)
                {
                    DebugLog("Disabling tracking first...");
                    var disableResult = await _autoTrackingService.DisableAsync();
                    if (!disableResult.Success)
                    {
                        return CommandResult.Fail(
                            "EnableAutoTrackingAtStoredHome",
                            "Tracking konnte nicht deaktiviert werden: " + disableResult.Message);
                    }
                    await Task.Delay(500);
                }

                // 2. Recall stored home preset
                DebugLog($"Moving to stored home preset {_config.AutoTrackingHomePosition}...");
                var presetResult = await RecallPresetAsync(int.Parse(_config.AutoTrackingHomePosition));
                if (!presetResult.Success)
                {
                    return CommandResult.Fail(
                        "EnableAutoTrackingAtStoredHome",
                        $"Home-Position (Preset {_config.AutoTrackingHomePosition}) konnte nicht angefahren werden: {presetResult.Message}");
                }

                // 3. Wait for camera to reach position
                DebugLog($"Waiting {_config.AutoTrackingStartupDelayMs}ms for camera to reach position...");
                await Task.Delay(_config.AutoTrackingStartupDelayMs);

                // 4. Enable recovery control
                DebugLog($"Enabling recovery control ({_config.AutoTrackingRecoveryTimeSeconds}s)...");
                var recoveryResult = await _autoTrackingService.EnableRecoveryControlAsync(_config.AutoTrackingRecoveryTimeSeconds);
                if (!recoveryResult.Success)
                {
                    // Warning only, not critical
                    DebugLog($"Recovery control warning: {recoveryResult.Message}");
                }

                // 5. Enable tracking
                DebugLog("Enabling auto tracking...");
                var enableResult = await _autoTrackingService.EnableAsync();
                if (!enableResult.Success)
                {
                    return CommandResult.Fail(
                        "EnableAutoTrackingAtStoredHome",
                        "Auto Tracking konnte nicht aktiviert werden: " + enableResult.Message);
                }

                DebugLog("EnableAutoTrackingAtStoredHomeAsync - Success");

                return CommandResult.Ok(
                    "EnableAutoTrackingAtStoredHome",
                    $"Auto Tracking aktiviert (Home Preset: {_config.AutoTrackingHomePosition})");
            }
            catch (Exception ex)
            {
                DebugLog($"EnableAutoTrackingAtStoredHomeAsync - Exception: {ex.Message}");
                return CommandResult.Fail(
                    "EnableAutoTrackingAtStoredHome",
                    "Fehler beim Aktivieren",
                    exception: ex);
            }
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
            if (!IsConnected)
            {
                return CommandResult.Fail("SetStandby", "Nicht verbunden");
            }

            string command = standby ? "standby" : "idle";
            string commandName = standby ? "EnterStandby" : "ExitStandby";
            
            // Include session ID if available
            string path = !string.IsNullOrEmpty(_sessionId) 
                ? $"standby.cgi?s={_sessionId}&cmd={command}"
                : $"standby.cgi?cmd={command}";
            
            return await SendCgiRequestAsync(path, commandName);
        }

        private async Task<CommandResult> SendControlParametersAsync(string parameterQuery, string commandName)
        {
            DebugLog($"SendControlParametersAsync called: {commandName}, IsConnected={IsConnected}");

            if (!IsConnected)
            {
                DebugLog($"{commandName} - Not connected, returning failure");
                return CommandResult.Fail(commandName, "Nicht verbunden");
            }

            // Acquire session lock to prevent interference with keep-alive
            await _sessionSemaphore.WaitAsync();
            
            try
            {
                // Build path with session ID if available
                string path;
                if (!string.IsNullOrEmpty(_sessionId))
                {
                    path = $"control.cgi?s={_sessionId}&{parameterQuery}";
                }
                else
                {
                    path = $"control.cgi?{parameterQuery}";
                }
                
                DebugLog($"Calling SendCgiRequestAsync with path: {path}");
                var result = await SendCgiRequestAsync(path, commandName);
                
                // Check if session is invalid (Unknown Connection ID)
                if (!result.Success && result.ResponseBody != null && 
                    result.ResponseBody.Contains("Unknown Connection ID"))
                {
                    DebugLog($"Session invalid during {commandName}, checking if Auto-Tracking is active...");
                    
                    // Check if tracking is the culprit
                    bool trackingActive = await _autoTrackingService.IsEnabledAsync();
                    if (trackingActive)
                    {
                        return CommandResult.Fail(commandName, 
                            "PTZ-Steuerung nicht möglich!\n\n" +
                            "Auto-Tracking ist aktiv.\n" +
                            "Bitte zuerst Auto-Tracking deaktivieren.");
                    }
                    
                    // Tracking not active, reconnect and retry
                    DebugLog("Auto-Tracking not active, reconnecting...");
                    var reconnectResult = await ConnectAsync();
                    if (!reconnectResult.Success)
                    {
                        return CommandResult.Fail(commandName, 
                            $"Session ungültig, Neuverbindung fehlgeschlagen: {reconnectResult.Message}");
                    }
                    
                    // Retry with new session
                    path = $"control.cgi?s={_sessionId}&{parameterQuery}";
                    result = await SendCgiRequestAsync(path, commandName);
                }
                
                return result;
            }
            finally
            {
                _sessionSemaphore.Release();
            }
        }

        private async Task<CommandResult> SendCgiRequestAsync(string path, string commandName)
        {
            try
            {
                string url = $"{_baseUrl}/-wvhttp-01-/{path}";
                DebugLog($"{commandName}: GET {SanitizeForLog(url)}");
                
                if (_httpClient.DefaultRequestHeaders.Authorization != null)
                {
                    // Maskiere Authorization-Header für Sicherheit
                    string authScheme = _httpClient.DefaultRequestHeaders.Authorization.Scheme;
                    string maskedAuth = new string('*', Math.Min(8, _httpClient.DefaultRequestHeaders.Authorization.Parameter?.Length ?? 0));
                    DebugLog($"Authorization: {authScheme} {maskedAuth}");
                }

                var response = await _httpClient.GetAsync(url);
                string body = await response.Content.ReadAsStringAsync();

                DebugLog($"Response: HTTP {(int)response.StatusCode} - {body}");

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
                DebugLog($"{commandName} - HttpRequestException: {ex.Message}");
                await HandleReconnectAsync();
                return CommandResult.Fail(commandName, "Netzwerkfehler", exception: ex);
            }
            catch (TaskCanceledException ex)
            {
                DebugLog($"{commandName} - TaskCanceledException: {ex.Message}");
                await HandleReconnectAsync();
                return CommandResult.Fail(commandName, "Timeout", exception: ex);
            }
            catch (Exception ex)
            {
                DebugLog($"{commandName} - Exception: {ex.Message}");
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
            if (Interlocked.CompareExchange(ref _disposeState, 1, 0) != 0)
            {
                return;
            }

            Volatile.Write(ref _disposeRequested, 1);
            DebugLog("Controller dispose requested");

            int disconnectState = Volatile.Read(ref _disconnectState);
            if (disconnectState == 0)
            {
                // Kein blockierendes Warten auf dem UI-Thread
                _ = DisconnectAsync();
                return;
            }

            if (disconnectState == 1)
            {
                DebugLog("Disconnect already running");
                return;
            }

            DebugLog("Disconnect already completed");
            DisposeResourcesOnce();
        }
    }
}
