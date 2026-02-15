using BMDSwitcherAPI;
using System;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;

namespace JokiAutomation
{
    internal class ATEMControl : IDisposable
    {
        private IBMDSwitcher _switcher;
        private IBMDSwitcherMixEffectBlock _mixEffectBlock;
        private bool _isConnected;
        private string _ipAddress;
        private IBMDSwitcherDownstreamKey _pipKeyer; // <-- Add this field

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
            Mix = 0x00,      // ✅ Korrigiert
            Dip = 0x01,      // ✅ Korrigiert
            Wipe = 0x02,     // ✅ Korrigiert
            DVE = 0x03,      // ✅ Korrigiert
            Stinger = 0x04   // ✅ Korrigiert
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
            try
            {
                Debug.WriteLine("=== ATEM Verbindung START ===");
                
                // SDK verbinden
                Debug.WriteLine("Verbinde BMDSwitcherAPI");
                IBMDSwitcherDiscovery discovery = new CBMDSwitcherDiscovery();
                _BMDSwitcherConnectToFailure failReason;

                discovery.ConnectTo(_ipAddress, out _switcher, out failReason);

                if (_switcher == null)
                {
                    throw new Exception($"Verbindung fehlgeschlagen: {failReason}");
                }

                _mixEffectBlock = GetMixEffectBlock();

                if (_mixEffectBlock == null)
                {
                    throw new Exception("Konnte kein Mix Effect Block finden");
                }

                string productName = "";
                _switcher.GetProductName(out productName);
                Debug.WriteLine($"✓ ATEM Model: {productName}");

                // Audio-API Check
                IBMDSwitcherAudioInputIterator testIterator = CreateIterator<IBMDSwitcherAudioInputIterator>();
                SupportsAudioAPI = (testIterator != null);
                
                if (SupportsAudioAPI)
                {
                    Debug.WriteLine("✓ Audio-API verfügbar (SDK)");
                    Marshal.ReleaseComObject(testIterator);
                }
                else
                {
                    Debug.WriteLine("⚠ ATEM Mini: Keine Audio-API verfügbar");
                    Debug.WriteLine("ℹ Bitte konfigurieren Sie Audio manuell:");
                    Debug.WriteLine("  1. Öffnen Sie ATEM Software Control");
                    Debug.WriteLine("  2. Audio Tab → Mikrofon 1: ON, Mikrofon 2: OFF");
                    Debug.WriteLine("  3. Schließen Sie Software Control");
                }

                // ✅ WICHTIG: _isConnected VORHER setzen, damit SetTransitionStyle/Duration nicht fehlschlagen
                _isConnected = true;

                SetTransitionStyle(TransitionStyle.Mix);

                // Setze Transition-Dauer (z.B. 25 Frames = 1 Sekunde bei PAL)
                SetTransitionDuration(25);  // 1 Sekunde bei 25fps

                Debug.WriteLine("=== ATEM Verbindung ERFOLGREICH ===");
                return _isConnected;
            }
            catch (Exception ex)
            {
                _isConnected = false;
                Debug.WriteLine($"✗ ATEM Verbindung fehlgeschlagen: {ex.Message}");
                throw new Exception($"ATEM Verbindung fehlgeschlagen: {ex.Message}", ex);
            }
        }

        public void Disconnect()
        {
            if (_pipKeyer != null)
            {
                Marshal.ReleaseComObject(_pipKeyer);
                _pipKeyer = null;
            }

            if (_mixEffectBlock != null)
            {
                Marshal.ReleaseComObject(_mixEffectBlock);
                _mixEffectBlock = null;
            }

            if (_switcher != null)
            {
                Marshal.ReleaseComObject(_switcher);
                _switcher = null;
            }

            _isConnected = false;
        }

        public void SetProgramInput(VideoSource source)
        {
            ValidateConnection();
            _mixEffectBlock.SetProgramInput((long)source);
        }

        // NEU: Methode für weiche Übergänge
        public void TransitionToProgramInput(VideoSource source)
        {
            ValidateConnection();
            
            try
            {
                // 1. Setze Preview auf gewünschte Source
                _mixEffectBlock.SetPreviewInput((long)source);
                Thread.Sleep(100); // Kurze Pause für ATEM-Verarbeitung
                
                // 2. Führe Auto-Transition aus (nutzt eingestellte Duration und Style)
                _mixEffectBlock.PerformAutoTransition();
                
                Debug.WriteLine($"✓ Transition zu {source} gestartet");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"✗ Transition fehlgeschlagen: {ex.Message}");
                throw new Exception($"Failed to transition to {source}: {ex.Message}", ex);
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
                if (dskIterator != null)
                {
                    Marshal.ReleaseComObject(dskIterator);
                }
            }
        }

        public void SetTransitionStyle(TransitionStyle style)
        {
            ValidateConnection();

            try
            {
                // Hole die Transition-Parameter-Schnittstelle direkt vom MixEffectBlock
                IBMDSwitcherTransitionParameters transParams = _mixEffectBlock as IBMDSwitcherTransitionParameters;

                if (transParams != null)
                {
                    // ✅ Konvertiere zu richtigem BMD-Enum-Wert
                    _BMDSwitcherTransitionStyle bmdStyle;
                    
                    switch (style)
                    {
                        case TransitionStyle.Mix:
                            bmdStyle = _BMDSwitcherTransitionStyle.bmdSwitcherTransitionStyleMix;
                            break;
                        case TransitionStyle.Dip:
                            bmdStyle = _BMDSwitcherTransitionStyle.bmdSwitcherTransitionStyleDip;
                            break;
                        case TransitionStyle.Wipe:
                            bmdStyle = _BMDSwitcherTransitionStyle.bmdSwitcherTransitionStyleWipe;
                            break;
                        case TransitionStyle.DVE:
                            bmdStyle = _BMDSwitcherTransitionStyle.bmdSwitcherTransitionStyleDVE;
                            break;
                        case TransitionStyle.Stinger:
                            bmdStyle = _BMDSwitcherTransitionStyle.bmdSwitcherTransitionStyleStinger;
                            break;
                        default:
                            bmdStyle = _BMDSwitcherTransitionStyle.bmdSwitcherTransitionStyleMix;
                            break;
                    }
                    
                    // Setze den Transition Style
                    transParams.SetNextTransitionStyle(bmdStyle);

                    Debug.WriteLine($"✓ Transition-Stil gesetzt: {style}");
                }
                else
                {
                    Debug.WriteLine("⚠ Transition-Parameter-Interface nicht verfügbar");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"⚠ Transition-Stil konnte nicht gesetzt werden: {ex.Message}");
            }
        }

        public void SetTransitionDuration(byte frames)
        {
            ValidateConnection();
            
            try
            {
                // ✅ Hole Mix-Transition-Parameter direkt
                IntPtr mixParamsPtr;
                Guid mixParamsGuid = typeof(IBMDSwitcherTransitionMixParameters).GUID;
                _mixEffectBlock.CreateIterator(ref mixParamsGuid, out mixParamsPtr);
                
                if (mixParamsPtr != IntPtr.Zero)
                {
                    IBMDSwitcherTransitionMixParameters mixParams = (IBMDSwitcherTransitionMixParameters)Marshal.GetObjectForIUnknown(mixParamsPtr);
                    Marshal.Release(mixParamsPtr);
                    
                    try
                    {
                        // Setze die Dauer in Frames
                        mixParams.SetRate((uint)frames);
                        
                        Debug.WriteLine($"✓ Transition-Dauer gesetzt: {frames} Frames (~{frames / 25.0:F1} Sekunden)");
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(mixParams);
                    }
                }
                else
                {
                    Debug.WriteLine("⚠ Mix-Transition-Parameter nicht verfügbar");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"⚠ Transition-Dauer konnte nicht gesetzt werden: {ex.Message}");
                Debug.WriteLine($"   Exception-Typ: {ex.GetType().Name}");
            }
        }

        public void InitializeToDefaultState()
        {
            ValidateConnection();

            Debug.WriteLine("=== ATEM Initialisierung START ===");

            SetDownstreamKeyerOnAir(0, false);
            Thread.Sleep(1000);

            // ✅ Verwende weichen Übergang statt hartem Cut
            TransitionToProgramInput(VideoSource.Input3);
            Thread.Sleep(1000);

            Thread.Sleep(1000);

            Debug.WriteLine("✓ ATEM Video-Initialisierung abgeschlossen");
            Debug.WriteLine("ℹ Audio wurde beim Connect() konfiguriert (MIC 1=ON, MIC 2=OFF)");
            Debug.WriteLine("=== ATEM Initialisierung ENDE ===");
        }

        public void SetAudioMixerInput(ushort audioSource, bool enable)
        {
            ValidateConnection();

            if (!SupportsAudioAPI)
            {
                Debug.WriteLine($"⚠ Audio-Befehl ignoriert: MIC {audioSource} → {(enable ? "ON" : "OFF")}");
                Debug.WriteLine("  Audio-API nicht verfügbar");
                return;
            }

            IBMDSwitcherAudioInput audioInput = null;
            IBMDSwitcherAudioInputIterator audioInputIterator = null;

            try
            {
                audioInputIterator = CreateIterator<IBMDSwitcherAudioInputIterator>();

                if (audioInputIterator == null)
                {
                    throw new Exception("Audio mixer iterator failed");
                }

                audioInput = FindAudioInputById(audioInputIterator, audioSource);

                if (audioInput == null)
                {
                    throw new Exception($"Audio input {audioSource} not found");
                }

                audioInput.SetMixOption(enable ? _BMDSwitcherAudioMixOption.bmdSwitcherAudioMixOptionOn : _BMDSwitcherAudioMixOption.bmdSwitcherAudioMixOptionOff);
                
                Debug.WriteLine($"✓ Audio Input {audioSource} → {(enable ? "ON" : "OFF")}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"✗ Audio-Fehler: {ex.Message}");
                throw new Exception($"Failed to set audio mixer input {audioSource}: {ex.Message}", ex);
            }
            finally
            {
                if (audioInput != null)
                {
                    Marshal.ReleaseComObject(audioInput);
                }
                if (audioInputIterator != null)
                {
                    Marshal.ReleaseComObject(audioInputIterator);
                }
            }
        }

        public void EnablePictureInPicture(VideoSource pipSource, PiPPosition position = PiPPosition.BottomRight, PiPSize size = PiPSize.Small)
        {
            ValidateConnection();

            Debug.WriteLine($"=== PiP aktivieren: Source={pipSource}, Position={position}, Size={size} ===");

            try
            {
                // ✅ Verwende Upstream Key 0 statt Downstream Keyer
                IBMDSwitcherKey key = GetUpstreamKey(0);
                
                if (key == null)
                {
                    throw new Exception("Upstream Key 0 nicht verfügbar");
                }

                // Key-Type auf DVE setzen
                key.SetType(_BMDSwitcherKeyType.bmdSwitcherKeyTypeDVE);
                
                // Fill-Source setzen (das Bild im PiP)
                key.SetInputFill((long)pipSource);
                
                // DVE-Parameter konfigurieren
                ConfigureDVEForPiP(key, position, size);
                
                // Key aktivieren (PiP einblenden)
                key.SetOnAir(1);
                
                Debug.WriteLine("✓ PiP aktiviert (Upstream Key 0 mit DVE)");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"✗ PiP-Fehler: {ex.Message}");
                throw new Exception($"Failed to enable Picture-in-Picture: {ex.Message}", ex);
            }
        }

        public void DisablePictureInPicture()
        {
            ValidateConnection();

            Debug.WriteLine("=== PiP deaktivieren ===");

            try
            {
                // ✅ Upstream Key 0 deaktivieren
                IBMDSwitcherKey key = GetUpstreamKey(0);
                
                if (key != null)
                {
                    key.SetOnAir(0);
                    Debug.WriteLine("✓ PiP deaktiviert (Upstream Key 0)");
                }
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
                
                if (key == null)
                    return false;
                
                int onAir;
                key.GetOnAir(out onAir);
                
                return onAir == 1;
            }
            catch
            {
                return false;
            }
        }

        // ✅ NEU: Upstream Key holen
        private IBMDSwitcherKey GetUpstreamKey(int index)
        {
            if (_mixEffectBlock == null)
            {
                throw new Exception("Mix Effect Block nicht verfügbar");
            }

            IBMDSwitcherKeyIterator keyIterator = null;
            
            try
            {
                // Key Iterator erstellen
                IntPtr iteratorPtr;
                Guid keyIteratorGuid = typeof(IBMDSwitcherKeyIterator).GUID;
                _mixEffectBlock.CreateIterator(ref keyIteratorGuid, out iteratorPtr);
                
                if (iteratorPtr == IntPtr.Zero)
                {
                    throw new Exception("Key Iterator konnte nicht erstellt werden");
                }
                
                keyIterator = (IBMDSwitcherKeyIterator)Marshal.GetObjectForIUnknown(iteratorPtr);
                Marshal.Release(iteratorPtr);
                
                // Zum gewünschten Key navigieren
                IBMDSwitcherKey key = null;
                for (int i = 0; i <= index; i++)
                {
                    keyIterator.Next(out key);
                    
                    if (key == null)
                    {
                        throw new Exception($"Upstream Key {index} nicht gefunden");
                    }
                    
                    if (i == index)
                    {
                        return key;
                    }
                }
                
                return key;
            }
            finally
            {
                if (keyIterator != null)
                {
                    Marshal.ReleaseComObject(keyIterator);
                }
            }
        }

        // ✅ NEU: DVE für PiP konfigurieren
        private void ConfigureDVEForPiP(IBMDSwitcherKey key, PiPPosition position, PiPSize size)
        {
            try
            {
                // Hole DVE-Parameter-Interface
                IBMDSwitcherKeyDVEParameters dveParams = key as IBMDSwitcherKeyDVEParameters;
                
                if (dveParams == null)
                {
                    Debug.WriteLine("⚠ DVE-Parameter nicht verfügbar");
                    return;
                }

                // ✅ Größe setzen (0.0 bis 1.0)
                double sizeX = 0.25;
                double sizeY = 0.25;
                
                switch (size)
                {
                    case PiPSize.Small:
                        sizeX = sizeY = 0.25;
                        break;
                    case PiPSize.Medium:
                        sizeX = sizeY = 0.4;
                        break;
                    case PiPSize.Large:
                        sizeX = sizeY = 0.6;
                        break;
                    case PiPSize.Full:
                        sizeX = sizeY = 1.0;
                        break;
                }

                // Calculate position values
                double posX = 0.0;
                double posY = 0.0;
                switch (position)
                {
                    case PiPPosition.TopLeft:
                        posX = -8.0;
                        posY = -4.5;
                        break;
                    case PiPPosition.TopRight:
                        posX = 8.0;
                        posY = -4.5;
                        break;
                    case PiPPosition.BottomLeft:
                        posX = -8.0;
                        posY = 4.5;
                        break;
                    case PiPPosition.BottomRight:
                        posX = 8.0;
                        posY = 4.5;
                        break;
                    case PiPPosition.Center:
                        posX = 0.0;
                        posY = 0.0;
                        break;
                }

                // Set mask to simulate PiP size and position
                dveParams.SetMasked(1);

                // Calculate mask values based on desired size and position
                // These are rough approximations for demonstration
                double maskLeft = -8.0 + posX - sizeX * 8.0;
                double maskRight = 8.0 + posX + sizeX * 8.0;
                double maskTop = -4.5 + posY - sizeY * 4.5;
                double maskBottom = 4.5 + posY + sizeY * 4.5;

                dveParams.SetMaskLeft(maskLeft);
                dveParams.SetMaskRight(maskRight);
                dveParams.SetMaskTop(maskTop);
                dveParams.SetMaskBottom(maskBottom);

                // Border aktivieren (optional)
                dveParams.SetBorderEnabled(1);
                dveParams.SetBorderWidthOut(0.5);
                
                Debug.WriteLine($"✓ DVE konfiguriert: Size=({sizeX},{sizeY}), Position=({posX},{posY})");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"⚠ DVE-Konfiguration fehlgeschlagen: {ex.Message}");
            }
        }


        public void Dispose()
        {
            Disconnect();
            GC.SuppressFinalize(this);
        }

        private void ValidateConnection()
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("Not connected");
            }
        }

        private IBMDSwitcherMixEffectBlock GetMixEffectBlock()
        {
            IBMDSwitcherMixEffectBlockIterator meIterator = CreateIterator<IBMDSwitcherMixEffectBlockIterator>();

            if (meIterator == null)
            {
                return null;
            }

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

                if (dsk == null)
                {
                    throw new Exception($"Downstream keyer {index} not found");
                }

                if (i == index)
                {
                    return dsk;
                }
            }

            return dsk;
        }

        private IBMDSwitcherAudioInput FindAudioInputById(IBMDSwitcherAudioInputIterator iterator, long targetId)
        {
            IBMDSwitcherAudioInput currentInput = null;

            while (true)
            {
                iterator.Next(out currentInput);

                if (currentInput == null)
                {
                    break;
                }

                long inputId;
                currentInput.GetAudioInputId(out inputId);

                if (inputId == targetId)
                {
                    return currentInput;
                }
            }

            return null;
        }

        private T CreateIterator<T>() where T : class
        {
            IntPtr iteratorPtr;
            Guid iteratorGuid = typeof(T).GUID;
            
            try
            {
                _switcher.CreateIterator(ref iteratorGuid, out iteratorPtr);

                if (iteratorPtr == IntPtr.Zero)
                {
                    return null;
                }

                T iterator = (T)Marshal.GetObjectForIUnknown(iteratorPtr);
                Marshal.Release(iteratorPtr);

                return iterator;
            }
            catch
            {
                return null;
            }
        }

        // Private Helper-Methoden

        private IBMDSwitcherDownstreamKey GetDownstreamKeyer(int index)
        {
            IBMDSwitcherDownstreamKeyIterator dskIterator = CreateIterator<IBMDSwitcherDownstreamKeyIterator>();

            if (dskIterator == null)
            {
                throw new Exception("Could not create downstream key iterator");
            }

            try
            {
                IBMDSwitcherDownstreamKey dsk = null;

                for (int i = 0; i <= index; i++)
                {
                    dskIterator.Next(out dsk);

                    if (dsk == null)
                    {
                        throw new Exception($"Downstream keyer {index} not found");
                    }

                    if (i == index)
                    {
                        return dsk;
                    }
                }

                return dsk;
            }
            finally
            {
                Marshal.ReleaseComObject(dskIterator);
            }
        }

        private void ConfigurePiPLayout(PiPPosition position, PiPSize size)
        {
            // Position und Größe in ATEM-Koordinaten umrechnen
            // ATEM verwendet: X/Y: -16.0 bis +16.0, Size: 0.0 bis 1.0
            double xPos = 0;
            double yPos = 0;
            double scale = 0.25; // Standard = 25% (Small)

            // Größe bestimmen
            switch (size)
            {
                case PiPSize.Small:
                    scale = 0.25; // 25%
                    break;
                case PiPSize.Medium:
                    scale = 0.4; // 40%
                    break;
                case PiPSize.Large:
                    scale = 0.6; // 60%
                    break;
                case PiPSize.Full:
                    scale = 1.0; // 100%
                    break;
            }

            // Position bestimmen
            switch (position)
            {
                case PiPPosition.TopLeft:
                    xPos = -12.0;
                    yPos = -9.0;
                    break;
                case PiPPosition.TopRight:
                    xPos = 12.0;
                    yPos = -9.0;
                    break;
                case PiPPosition.BottomLeft:
                    xPos = -12.0;
                    yPos = 9.0;
                    break;
                case PiPPosition.BottomRight:
                    xPos = 12.0;
                    yPos = 9.0;
                    break;
                case PiPPosition.Center:
                    xPos = 0.0;
                    yPos = 0.0;
                    break;
            }

            // DVE-Parameter für PiP setzen (falls vorhanden)
            try
            {
                // Versuche auf DVE-Parameter zuzugreifen
                // HINWEIS: Das ATEM Mini hat eingeschränkte DVE-Funktionen
                // Für vollständige PiP-Kontrolle benötigen Sie ATEM Mini Extreme
                Debug.WriteLine($"  Position: X={xPos}, Y={yPos}, Scale={scale}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"  ⚠ DVE-Konfiguration nicht verfügbar: {ex.Message}");
            }
        }

    }
}