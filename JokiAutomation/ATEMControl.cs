using BMDSwitcherAPI;
using System;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace JokiAutomation
{
    internal class ATEMControl : IDisposable
    {
        // ── Transition serialisation ──────────────────────────────────────────
        /// <summary>
        /// Ensures only one ATEM transition runs at a time.
        /// Threading note: all COM calls stay on the calling (UI) thread via
        /// ConfigureAwait(false) is intentionally NOT used here so callers from
        /// a non-UI context still serialise through this semaphore.
        /// </summary>
        private readonly SemaphoreSlim _transitionSemaphore = new SemaphoreSlim(1, 1);

        private const int MAKRO_INIT = 0;
        private const int MAKRO_SHUT_DOWN = 1;
        private IBMDSwitcher _switcher;
        private IBMDSwitcherMixEffectBlock _mixEffectBlock;
        private bool _isConnected;
        private string _ipAddress;
        private IBMDSwitcherDownstreamKey _pipKeyer;

        // ── COM threading diagnostics ─────────────────────────────────────────
        private int _comOwnerThreadId;
        private ApartmentState _comApartmentState;

        // ── Timing constants ─────────────────────────────────────────────────
        private static readonly TimeSpan DefaultTransitionTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan PreviewConfirmTimeout   = TimeSpan.FromSeconds(2);
        private const int PollingIntervalMs = 30;

        // ── PiP (Picture-in-Picture) Konfiguration ──────────────────────────
        private const long   PiP_Source    = 2;      // Input2 (Cam 2)
        private const double PiP_SizeX     = 0.35;
        private const double PiP_SizeY     = 0.35;
        private const double PiP_PositionX = 10.4;
        private const double PiP_PositionY = -5.7;
        public enum VideoSource
        {
            Black = 0,
            Input1 = 1,
            Input2 = 2,
            Input3 = 3,
            Input4 = 4,
            ColorBars = 1000,
            Color1 = 2001,
            Color2 = 2002,
            MediaPlayer1 = 3010,
            MediaPlayer2 = 3020
        }

        public enum TransitionStyle
        {
            Mix = 0x00,
            Dip = 0x01,
            Wipe = 0x02,
            DVE = 0x03,
            Stinger = 0x04
        }

        public enum PiPSize
        {
            Small = 0,
            Medium = 1,
            Large = 2,
            Full = 3
        }

        public enum PiPPosition
        {
            TopLeft = 0,
            TopRight = 1,
            BottomLeft = 2,
            BottomRight = 3,
            Center = 4
        }

        public bool IsConnected => _isConnected;
        public bool SupportsAudioAPI { get; private set; }

        public ATEMControl(string ipAddress = "192.168.178.48")
        {
            _ipAddress = ipAddress;
            _isConnected = false;
            SupportsAudioAPI = false;
        }

        public bool Connect()
        {
            IBMDSwitcherDiscovery discovery = null;
            try
            {
                Debug.WriteLine("=== ATEM Verbindung START ===");
                discovery = new CBMDSwitcherDiscovery();
                _BMDSwitcherConnectToFailure failReason;
                discovery.ConnectTo(_ipAddress, out _switcher, out failReason);

                if (_switcher == null)
                    throw new Exception($"Verbindung fehlgeschlagen: {failReason}");

                // ────────────────────────────────────────────────────────────
                // 1. PFLICHT-TEST: MixEffectBlock mit GetProgramInput/GetPreviewInput
                // ────────────────────────────────────────────────────────────
                _mixEffectBlock = GetMixEffectBlock();
                if (_mixEffectBlock == null)
                    throw new Exception("Konnte kein Mix Effect Block finden");

                // Erfasse COM-Owner-Thread-ID und Apartment-State unmittelbar
                _comOwnerThreadId = Thread.CurrentThread.ManagedThreadId;
                _comApartmentState = Thread.CurrentThread.GetApartmentState();
                Debug.WriteLine($"📋 COM-Owner: ThreadId={_comOwnerThreadId}, ApartmentState={_comApartmentState}");

                // Test ob GetProgramInput und GetPreviewInput funktionieren
                try
                {
                    long testProgram = 0;
                    long testPreview = 0;
                    _mixEffectBlock.GetProgramInput(out testProgram);
                    _mixEffectBlock.GetPreviewInput(out testPreview);
                    Debug.WriteLine($"✓ MixEffectBlock Interface kompatibel — Program={testProgram}, Preview={testPreview}");
                }
                catch (COMException comEx) when (comEx.ErrorCode == unchecked((int)0x80004002)) // E_NOINTERFACE
                {
                    throw new Exception("MixEffectBlock nicht kompatibel — Pflicht-Interface nicht verfügbar (E_NOINTERFACE)", comEx);
                }
                catch (Exception ex)
                {
                    throw new Exception($"MixEffectBlock-Test fehlgeschlagen: {ex.Message}", ex);
                }

                // ────────────────────────────────────────────────────────────
                // 2. Produktname auslesen
                // ────────────────────────────────────────────────────────────
                string productName = "";
                _switcher.GetProductName(out productName);
                Debug.WriteLine($"✓ ATEM Model: {productName}");

                // ────────────────────────────────────────────────────────────
                // 3. Legacy Audio-API nicht verwenden — nur Kennzeichen setzen
                // ────────────────────────────────────────────────────────────
                SupportsAudioAPI = false;
                Debug.WriteLine("ℹ Legacy Audio-API bewusst deaktiviert (ATEM Mini Pro)");

                _isConnected = true;
                SetTransitionStyle(TransitionStyle.Mix);
                SetTransitionDuration(25);
                Debug.WriteLine("=== ATEM Verbindung ERFOLGREICH ===");
                return _isConnected;
            }
            catch (Exception ex)
            {
                _isConnected = false;
                SupportsAudioAPI = false;
                Debug.WriteLine($"✗ ATEM Verbindung fehlgeschlagen: {ex.Message}");
                throw new Exception($"ATEM Verbindung fehlgeschlagen: {ex.Message}", ex);
            }
            finally
            {
                if (discovery != null && Marshal.IsComObject(discovery))
                {
                    Marshal.ReleaseComObject(discovery);
                }
            }
        }

        public void Disconnect()
        {
            if (_pipKeyer != null) { Marshal.ReleaseComObject(_pipKeyer); _pipKeyer = null; }
            if (_mixEffectBlock != null) { Marshal.ReleaseComObject(_mixEffectBlock); _mixEffectBlock = null; }
            if (_switcher != null) { Marshal.ReleaseComObject(_switcher); _switcher = null; }
            _isConnected = false;
        }

        public void SetProgramInput(VideoSource source)
        {
            ValidateConnection();
            _mixEffectBlock.SetProgramInput((long)source);
        }

        // ── Legacy synchronous wrapper (kept for backwards compatibility) ─────
        /// <summary>
        /// Synchronous wrapper. Prefer <see cref="TransitionToProgramInputAsync"/> in new code.
        /// </summary>
        public Task TransitionToProgramInput(VideoSource source)
        {
            return TransitionToProgramInputAsync(source);
        }

        // ── New asynchronous, serialised, verified transition ─────────────────
        /// <summary>
        /// Switches the ATEM program bus to <paramref name="source"/> using an
        /// Auto-Transition.  The method returns only after
        /// <c>GetProgramInput()</c> confirms the target is on-air.
        ///
        /// Threading: COM calls remain on UI thread. Async polling with Task.Delay
        /// (no Thread.Sleep, no ConfigureAwait(false), no Task.Run).
        /// </summary>
        public async Task TransitionToProgramInputAsync(
            VideoSource source,
            CancellationToken cancellationToken = default,
            TimeSpan? timeout = null)
        {
            ValidateConnection();

            TimeSpan effectiveTimeout = timeout ?? DefaultTransitionTimeout;
            string correlationId = Guid.NewGuid().ToString("N").Substring(0, 8);
            long targetInput = (long)source;

            await _transitionSemaphore.WaitAsync(cancellationToken);
            try
            {
                LogTransition(correlationId, $"Async-Sequenz gestartet — target={source}({targetInput})");
                EnsureComOwnerThread("TransitionToProgramInputAsync-Entry");

                // ── Step 1: Read current Program and Preview ─────────────────
                long currentProgram = 0;
                long currentPreview = 0;
                _mixEffectBlock.GetProgramInput(out currentProgram);
                _mixEffectBlock.GetPreviewInput(out currentPreview);
                LogTransition(correlationId,
                    $"Aktuelle Zustände — Program={currentProgram}, Preview={currentPreview}");

                // ── Step 2: Early exit if target already on Program ──────────
                if (currentProgram == targetInput)
                {
                    LogTransition(correlationId, $"Target {source} bereits auf Program — kein Transition nötig");
                    return;
                }

                // ── Step 3: Set Preview to target ──────────────────────────
                _mixEffectBlock.SetPreviewInput(targetInput);
                LogTransition(correlationId, $"SetPreviewInput({targetInput}) aufgerufen");

                // ── Step 4: Async polling - wait for Preview confirmation ──
                EnsureComOwnerThread("Before WaitForPreviewInputAsync");
                bool previewConfirmed = await WaitForPreviewInputAsync(
                    targetInput, PreviewConfirmTimeout, cancellationToken, correlationId);

                EnsureComOwnerThread("After WaitForPreviewInputAsync");

                if (!previewConfirmed)
                {
                    long pgm = 0, prv = 0;
                    _mixEffectBlock.GetProgramInput(out pgm);
                    _mixEffectBlock.GetPreviewInput(out prv);
                    throw new TimeoutException(
                        $"[Sequence {correlationId}] Preview-Bestätigungszeitüberschreitung. " +
                        $"Erwartet Preview={targetInput}, aktuell Program={pgm}, Preview={prv}");
                }

                LogTransition(correlationId, $"Preview bestätigt: {targetInput}");

                // ── Step 5: Perform Auto-Transition exactly once ───────────
                cancellationToken.ThrowIfCancellationRequested();
                _mixEffectBlock.PerformAutoTransition();
                LogTransition(correlationId, "Auto-Transition gestartet");

                // ── Step 6: Async polling - wait for Program confirmation ──
                EnsureComOwnerThread("Before WaitForProgramInputAsync");
                await WaitForProgramInputAsync(
                    targetInput, effectiveTimeout, cancellationToken, correlationId);

                EnsureComOwnerThread("After WaitForProgramInputAsync");

                LogTransition(correlationId, $"Program bestätigt: {targetInput}");

                // ── Step 7: Log final state ──────────────────────────────────
                long finalProgram = 0;
                long finalPreview = 0;
                _mixEffectBlock.GetProgramInput(out finalProgram);
                _mixEffectBlock.GetPreviewInput(out finalPreview);
                LogTransition(correlationId,
                    $"Sequenz abgeschlossen — Program={finalProgram}, Preview={finalPreview}");
            }
            catch (OperationCanceledException)
            {
                LogTransition(correlationId, "Sequenz durch CancellationToken abgebrochen");
                throw;
            }
            catch (TimeoutException tex)
            {
                LogTransition(correlationId, $"Sequenz fehlgeschlagen (Zeitüberschreitung): {tex.Message}");
                throw;
            }
            catch (Exception ex)
            {
                LogTransition(correlationId, $"Sequenz fehlgeschlagen: {ex.GetType().Name} — {ex.Message}");
                throw;
            }
            finally
            {
                _transitionSemaphore.Release();
            }
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private static void LogTransition(string correlationId, string message)
        {
            string text = $"[Sequence {correlationId}] {message}";
            Debug.WriteLine(text);
        }

        /// <summary>
        /// Prüft den COM-Owner-Thread und bricht bei Thread-/Apartment-Mismatch ab.
        /// </summary>
        private void EnsureComOwnerThread(string operation)
        {
            int currentThreadId = Thread.CurrentThread.ManagedThreadId;
            ApartmentState currentApartment = Thread.CurrentThread.GetApartmentState();

            if (currentThreadId != _comOwnerThreadId || currentApartment != _comApartmentState)
            {
                throw new InvalidOperationException(
                    $"ATEM COM-Aufruf '{operation}' auf falschem Thread. " +
                    $"Owner={_comOwnerThreadId}/{_comApartmentState}, " +
                    $"Current={currentThreadId}/{currentApartment}");
            }
        }

        /// <summary>
        /// Polls GetPreviewInput until it equals <paramref name="expectedInput"/>
        /// or the timeout expires. Async with Task.Delay.
        /// </summary>
        private async Task<bool> WaitForPreviewInputAsync(
            long expectedInput,
            TimeSpan timeout,
            CancellationToken cancellationToken,
            string correlationId)
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long prv = 0;
                _mixEffectBlock.GetPreviewInput(out prv);
                if (prv == expectedInput)
                {
                    LogTransition(correlationId, $"Preview-Poll erfolgreich: {prv}");
                    return true;
                }
                await Task.Delay(PollingIntervalMs, cancellationToken);
            }
            LogTransition(correlationId, $"WaitForPreviewInput timeout nach {timeout.TotalMilliseconds}ms");
            return false;
        }

        /// <summary>
        /// Polls GetProgramInput until it equals <paramref name="expectedProgram"/>
        /// or the timeout expires. Async with Task.Delay.
        /// </summary>
        private async Task WaitForProgramInputAsync(
            long expectedProgram,
            TimeSpan timeout,
            CancellationToken cancellationToken,
            string correlationId)
        {
            var sw = Stopwatch.StartNew();

            while (sw.Elapsed < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();

                long pgm = 0;
                _mixEffectBlock.GetProgramInput(out pgm);
                
                if (pgm == expectedProgram)
                {
                    LogTransition(correlationId, $"Program-Poll erfolgreich: {pgm}");
                    return;
                }

                await Task.Delay(PollingIntervalMs, cancellationToken);
            }

            // Timeout
            long finalPgm = 0, finalPrv = 0;
            _mixEffectBlock.GetProgramInput(out finalPgm);
            _mixEffectBlock.GetPreviewInput(out finalPrv);
            
            throw new TimeoutException(
                $"[Sequence {correlationId}] Program-Poll timeout nach {timeout.TotalSeconds:F1}s. " +
                $"Program={finalPgm} (erwartet {expectedProgram}), Preview={finalPrv}");
        }

        /// <summary>
        /// Logs the on-air state of Upstream Key 0 for diagnostic purposes only.
        /// Does not change any keyer state.
        /// </summary>
        private void DiagnoseKeyerState(string correlationId)
        {
            try
            {
                IBMDSwitcherKey key = GetUpstreamKey(0);
                if (key == null) return;
                int onAir = 0;
                long fillInput = 0;
                key.GetOnAir(out onAir);
                key.GetInputFill(out fillInput);
                if (onAir != 0)
                {
                    LogTransition(correlationId,
                        $"Diagnostic: Upstream Key 0 is ON-AIR (fill input={fillInput}) — PiP active");
                }
                else
                {
                    LogTransition(correlationId, "Diagnostic: Upstream Key 0 is off-air");
                }
            }
            catch (Exception ex)
            {
                LogTransition(correlationId, $"Diagnostic: keyer check failed: {ex.Message}");
            }
        }

        public void SetDownstreamKeyerOnAir(byte keyerIndex, bool enable)
        {
            ValidateConnection();
            IBMDSwitcherDownstreamKeyIterator dskIterator = CreateIterator<IBMDSwitcherDownstreamKeyIterator>();
            try
            {
                IBMDSwitcherDownstreamKey dsk = GetDownstreamKeyByIndex(dskIterator, keyerIndex);
                dsk.SetOnAir(enable ? 1 : 0);
            }
            finally
            {
                if (dskIterator != null) Marshal.ReleaseComObject(dskIterator);
            }
        }

        public void SetTransitionStyle(TransitionStyle style)
        {
            ValidateConnection();
            try
            {
                IBMDSwitcherTransitionParameters transParams = _mixEffectBlock as IBMDSwitcherTransitionParameters;
                if (transParams != null)
                {
                    _BMDSwitcherTransitionStyle bmdStyle;
                    switch (style)
                    {
                        case TransitionStyle.Mix:     bmdStyle = _BMDSwitcherTransitionStyle.bmdSwitcherTransitionStyleMix;     break;
                        case TransitionStyle.Dip:     bmdStyle = _BMDSwitcherTransitionStyle.bmdSwitcherTransitionStyleDip;     break;
                        case TransitionStyle.Wipe:    bmdStyle = _BMDSwitcherTransitionStyle.bmdSwitcherTransitionStyleWipe;    break;
                        case TransitionStyle.DVE:     bmdStyle = _BMDSwitcherTransitionStyle.bmdSwitcherTransitionStyleDVE;     break;
                        case TransitionStyle.Stinger: bmdStyle = _BMDSwitcherTransitionStyle.bmdSwitcherTransitionStyleStinger; break;
                        default:                      bmdStyle = _BMDSwitcherTransitionStyle.bmdSwitcherTransitionStyleMix;     break;
                    }
                    transParams.SetNextTransitionStyle(bmdStyle);
                    Debug.WriteLine($"✓ Transition-Stil gesetzt: {style}");
                }
                else Debug.WriteLine("⚠ Transition-Parameter-Interface nicht verfügbar");
            }
            catch (Exception ex) { Debug.WriteLine($"⚠ Transition-Stil konnte nicht gesetzt werden: {ex.Message}"); }
        }

        public void SetTransitionDuration(byte frames)
        {
            ValidateConnection();
            try
            {
                // IBMDSwitcherTransitionMixParameters ist kein Iterator,
                // sondern ein zusätzliches COM-Interface des MixEffectBlock.
                // Direkt per Cast anfordern.
                IBMDSwitcherTransitionMixParameters mixParams =
                    _mixEffectBlock as IBMDSwitcherTransitionMixParameters;

                if (mixParams == null)
                {
                    Debug.WriteLine("⚠ Mix-Transition-Parameter nicht verfügbar (optional) — Verwende vorhandene Switcher-Dauer");
                    return;
                }

                // Bisherigen Wert lesen
                uint currentRate = 0;
                mixParams.GetRate(out currentRate);
                Debug.WriteLine($"📖 Transition-Dauer aktuell: {currentRate} Frames");

                // Neuen Wert setzen
                mixParams.SetRate((uint)frames);
                Debug.WriteLine($"⚙ Transition-Dauer gesetzt auf: {frames} Frames");

                // Wert erneut lesen und bestätigen
                uint confirmedRate = 0;
                mixParams.GetRate(out confirmedRate);
                if (confirmedRate == frames)
                {
                    Debug.WriteLine($"✓ Transition-Dauer bestätigt: {confirmedRate} Frames");
                }
                else
                {
                    Debug.WriteLine($"⚠ Transition-Dauer nicht bestätigt: Erwartet {frames}, erhalten {confirmedRate}");
                }
            }
            catch (InvalidCastException icEx)
            {
                Debug.WriteLine($"⚠ Mix-Transition-Parameter Cast fehlgeschlagen (optional): {icEx.Message}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"⚠ Transition-Dauer konnte nicht gesetzt werden: {ex.Message}");
            }
        }

        public void InitializeToDefaultState()
        {
            ValidateConnection();
            Debug.WriteLine("=== ATEM Initialisierung START ===");
            SetDownstreamKeyerOnAir(0, false);
            Thread.Sleep(1000);
            RunMacro(_switcher, MAKRO_INIT);
            Console.WriteLine($"Makro {MAKRO_INIT} gestartet.");
            Debug.WriteLine("=== ATEM Initialisierung ENDE ===");
        }

        public static uint GetMacroCount(IBMDSwitcher switcher)
        {
            var macroPool = (IBMDSwitcherMacroPool)switcher;
            macroPool.GetMaxCount(out uint maxCount);
            return maxCount;
        }

        public static string GetMacroDescription(IBMDSwitcher switcher, uint index)
        {
            var macroPool = (IBMDSwitcherMacroPool)switcher;
            macroPool.GetDescription(index, out string description);
            return description;
        }

        public static void RunMacro(IBMDSwitcher switcher, uint index)
        {
            var macroControl = (IBMDSwitcherMacroControl)switcher;
            macroControl.Run(index);
        }

        public void SetAudioMixerInput(ushort audioSource, bool enable)
        {
            ValidateConnection();
            
            // Wenn Audio-API nicht verfügbar → Befehl protokollieren und überspringen
            if (!SupportsAudioAPI)
            {
                Debug.WriteLine($"⚠ Audio-Befehl ignoriert (Audio-API nicht verfügbar): MIC {audioSource} → {(enable ? "ON" : "OFF")}");
                return;
            }

            IBMDSwitcherAudioInput audioInput = null;
            IBMDSwitcherAudioInputIterator audioInputIterator = null;
            try
            {
                audioInputIterator = CreateIterator<IBMDSwitcherAudioInputIterator>();
                if (audioInputIterator == null) throw new Exception("Audio mixer iterator failed");
                audioInput = FindAudioInputById(audioInputIterator, audioSource);
                if (audioInput == null) throw new Exception($"Audio input {audioSource} not found");
                audioInput.SetMixOption(enable
                    ? _BMDSwitcherAudioMixOption.bmdSwitcherAudioMixOptionOn
                    : _BMDSwitcherAudioMixOption.bmdSwitcherAudioMixOptionOff);
                Debug.WriteLine($"✓ Audio Input {audioSource} → {(enable ? "ON" : "OFF")}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"✗ Audio-Fehler: {ex.Message}");
                throw new Exception($"Failed to set audio mixer input {audioSource}: {ex.Message}", ex);
            }
            finally
            {
                if (audioInput != null) Marshal.ReleaseComObject(audioInput);
                if (audioInputIterator != null) Marshal.ReleaseComObject(audioInputIterator);
            }
        }


        public void EnablePictureInPicture()
        {
            ValidateConnection();
            try
            {
                IBMDSwitcherKey key = GetUpstreamKey(0);
                if (key == null) throw new Exception("Upstream Key 0 nicht verfügbar");
                key.SetType(_BMDSwitcherKeyType.bmdSwitcherKeyTypeDVE);
                key.SetInputFill(PiP_Source);
                ConfigureDVEForPiP(key);
                key.SetOnAir(1);
                Debug.WriteLine("✓ PiP aktiviert (Upstream Key 0 mit DVE)");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"✗ PiP-Fehler: {ex.Message}");
                throw new Exception($"Failed to enable Picture-in-Picture: {ex.Message}", ex);
            }
        }

        public async Task DisablePictureInPictureAsync(
            CancellationToken cancellationToken = default,
            TimeSpan? timeout = null)
        {
            ValidateConnection();
            TimeSpan effectiveTimeout = timeout ?? TimeSpan.FromSeconds(2);
            Stopwatch stopwatch = Stopwatch.StartNew();
            Exception lastError = null;
            int attempt = 0;

            while (stopwatch.Elapsed < effectiveTimeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                attempt++;

                try
                {
                    IBMDSwitcherKey key = GetUpstreamKey(0);
                    if (key == null)
                    {
                        throw new Exception("Upstream Key 0 nicht verfügbar");
                    }

                    key.SetOnAir(0);

                    int onAir = 1;
                    key.GetOnAir(out onAir);
                    if (onAir == 0)
                    {
                        Debug.WriteLine($"✓ PiP deaktiviert (async, Versuch {attempt})");
                        return;
                    }

                    Debug.WriteLine($"⚠ PiP noch aktiv (async, Versuch {attempt}), erneuter Poll...");
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    Debug.WriteLine($"⚠ PiP async disable Versuch {attempt} fehlgeschlagen: {ex.Message}");
                }

                await Task.Delay(PollingIntervalMs, cancellationToken);
            }

            string baseMessage = $"Failed to disable Picture-in-Picture within {effectiveTimeout.TotalMilliseconds:0}ms";
            if (lastError != null)
            {
                throw new TimeoutException(baseMessage + $". Last error: {lastError.Message}", lastError);
            }

            throw new TimeoutException(baseMessage + ". PiP remained on-air.");
        }

        public void DisablePictureInPicture()
        {
            ValidateConnection();
            try
            {
                const int maxAttempts = 4;
                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    IBMDSwitcherKey key = GetUpstreamKey(0);
                    if (key == null)
                    {
                        Debug.WriteLine("✗ PiP-Fehler: Upstream Key 0 nicht verfügbar");
                        return;
                    }

                    key.SetOnAir(0);

                    int onAir = 1;
                    key.GetOnAir(out onAir);
                    if (onAir == 0)
                    {
                        Debug.WriteLine($"✓ PiP deaktiviert (Versuch {attempt})");
                        return;
                    }

                    Debug.WriteLine($"⚠ PiP noch aktiv nach Versuch {attempt}, erneuter Versuch...");
                    Thread.Sleep(75);
                }

                Debug.WriteLine("✗ PiP konnte nach mehreren Versuchen nicht deaktiviert werden");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"✗ PiP-Fehler: {ex.Message}");
                throw new Exception($"Failed to disable Picture-in-Picture: {ex.Message}", ex);
            }
        }

        public bool IsPiPActive()
        {
            ValidateConnection();
            try
            {
                IBMDSwitcherKey key = GetUpstreamKey(0);
                if (key == null) return false;
                int onAir;
                key.GetOnAir(out onAir);
                return onAir == 1;
            }
            catch { return false; }
        }

        private IBMDSwitcherKey GetUpstreamKey(int index)
        {
            if (_mixEffectBlock == null) throw new Exception("Mix Effect Block nicht verfügbar");
            IBMDSwitcherKeyIterator keyIterator = null;
            try
            {
                IntPtr iteratorPtr;
                Guid keyIteratorGuid = typeof(IBMDSwitcherKeyIterator).GUID;
                _mixEffectBlock.CreateIterator(ref keyIteratorGuid, out iteratorPtr);
                if (iteratorPtr == IntPtr.Zero) throw new Exception("Key Iterator konnte nicht erstellt werden");
                keyIterator = (IBMDSwitcherKeyIterator)Marshal.GetObjectForIUnknown(iteratorPtr);
                Marshal.Release(iteratorPtr);
                IBMDSwitcherKey key = null;
                for (int i = 0; i <= index; i++)
                {
                    keyIterator.Next(out key);
                    if (key == null) throw new Exception($"Upstream Key {index} nicht gefunden");
                    if (i == index) return key;
                }
                return key;
            }
            finally { if (keyIterator != null) Marshal.ReleaseComObject(keyIterator); }
        }

        /*   private void ConfigureDVEForPiP(IBMDSwitcherKey key)
           {
               if (key == null) throw new ArgumentNullException(nameof(key));

               // HINWEIS: Das ATEM Mini Pro SDK stellt keine API-Methoden für DVE-Größe
               // und -Position bereit (SetSizeX/SetPositionX existieren nicht im Interface).
               // Größe (0.35 x 0.35) und Position (X: 10.4 / Y: -5.7) müssen einmalig
               // im ATEM Software Control konfiguriert werden – der ATEM speichert diese
               // Einstellungen dauerhaft. Die API setzt hier nur Border und Maske.

               try
               {
                   IBMDSwitcherKeyDVEParameters dveParams = key as IBMDSwitcherKeyDVEParameters;
                   if (dveParams == null)
                       throw new NotSupportedException("Upstream Key stellt keine DVE-Parameter bereit.");

                   dveParams.SetBorderEnabled(0);
                   dveParams.SetMasked(0);

                   Debug.WriteLine($"✓ DVE konfiguriert: Border=aus, Maske=aus (Größe/Position via ATEM Software Control)");
               }
               catch (Exception ex)
               {
                   throw new InvalidOperationException(
                       $"DVE-Konfiguration fehlgeschlagen: {ex.Message}", ex);
               }
           }*/
        private void ConfigureDVEForPiP(IBMDSwitcherKey key)
        {
            var fly = key as IBMDSwitcherKeyFlyParameters;

            if (fly == null)
            {
                Debug.WriteLine("Fly Interface nicht verfügbar");
                return;
            }

            int canFly;
            fly.GetCanFly(out canFly);

            Debug.WriteLine($"CanFly = {canFly}");

            double sx, sy;
            fly.GetSizeX(out sx);
            fly.GetSizeY(out sy);

            Debug.WriteLine($"Aktuelle Größe X={sx} Y={sy}");

            // Test
            fly.SetSizeX(0.35);
            fly.SetSizeY(0.35);

            fly.SetPositionX(10.40);
            fly.SetPositionY(-5.85);
        }

        public void Dispose() { Disconnect(); GC.SuppressFinalize(this); }

        private void ValidateConnection()
        {
            if (!IsConnected) throw new InvalidOperationException("Not connected");
        }

        private IBMDSwitcherMixEffectBlock GetMixEffectBlock()
        {
            IBMDSwitcherMixEffectBlockIterator meIterator = CreateIterator<IBMDSwitcherMixEffectBlockIterator>();
            if (meIterator == null) return null;
            IBMDSwitcherMixEffectBlock mixEffectBlock;
            meIterator.Next(out mixEffectBlock);
            Marshal.ReleaseComObject(meIterator);
            return mixEffectBlock;
        }

        private IBMDSwitcherDownstreamKey GetDownstreamKeyByIndex(IBMDSwitcherDownstreamKeyIterator iterator, byte index)
        {
            IBMDSwitcherDownstreamKey dsk = null;
            for (int i = 0; i <= index; i++)
            {
                iterator.Next(out dsk);
                if (dsk == null) throw new Exception($"Downstream keyer {index} not found");
                if (i == index) return dsk;
            }
            return dsk;
        }

        private IBMDSwitcherAudioInput FindAudioInputById(IBMDSwitcherAudioInputIterator iterator, long targetId)
        {
            IBMDSwitcherAudioInput currentInput = null;
            while (true)
            {
                iterator.Next(out currentInput);
                if (currentInput == null) break;
                long inputId;
                currentInput.GetAudioInputId(out inputId);
                if (inputId == targetId) return currentInput;
            }
            return null;
        }

        private T CreateIteratorOn<T>(IBMDSwitcher owner) where T : class
        {
            if (owner == null) return null;
            IntPtr iteratorPtr = IntPtr.Zero;
            Guid iteratorGuid = typeof(T).GUID;
            try
            {
                owner.CreateIterator(ref iteratorGuid, out iteratorPtr);
                if (iteratorPtr == IntPtr.Zero) return null;
                return Marshal.GetObjectForIUnknown(iteratorPtr) as T;
            }
            finally
            {
                if (iteratorPtr != IntPtr.Zero) Marshal.Release(iteratorPtr);
            }
        }

        private T CreateIteratorOn<T>(IBMDSwitcherMixEffectBlock owner) where T : class
        {
            if (owner == null) return null;
            IntPtr iteratorPtr = IntPtr.Zero;
            Guid iteratorGuid = typeof(T).GUID;
            try
            {
                owner.CreateIterator(ref iteratorGuid, out iteratorPtr);
                if (iteratorPtr == IntPtr.Zero) return null;
                return Marshal.GetObjectForIUnknown(iteratorPtr) as T;
            }
            finally
            {
                if (iteratorPtr != IntPtr.Zero) Marshal.Release(iteratorPtr);
            }
        }

        private T CreateIterator<T>() where T : class
        {
            IntPtr iteratorPtr;
            Guid iteratorGuid = typeof(T).GUID;
            try
            {
                _switcher.CreateIterator(ref iteratorGuid, out iteratorPtr);
                if (iteratorPtr == IntPtr.Zero) return null;
                T iterator = (T)Marshal.GetObjectForIUnknown(iteratorPtr);
                Marshal.Release(iteratorPtr);
                return iterator;
            }
            catch { return null; }
        }

        private IBMDSwitcherDownstreamKey GetDownstreamKeyer(int index)
        {
            IBMDSwitcherDownstreamKeyIterator dskIterator = CreateIterator<IBMDSwitcherDownstreamKeyIterator>();
            if (dskIterator == null) throw new Exception("Could not create downstream key iterator");
            try
            {
                IBMDSwitcherDownstreamKey dsk = null;
                for (int i = 0; i <= index; i++)
                {
                    dskIterator.Next(out dsk);
                    if (dsk == null) throw new Exception($"Downstream keyer {index} not found");
                    if (i == index) return dsk;
                }
                return dsk;
            }
            finally { Marshal.ReleaseComObject(dskIterator); }
        }
    }
}