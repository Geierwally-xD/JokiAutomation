using System;
using System.Runtime.InteropServices;

namespace BMDSwitcherAPI
{
    // Connect To Failure Enum
    public enum _BMDSwitcherConnectToFailure
    {
        bmdSwitcherConnectToFailureNoResponse = 1,
        bmdSwitcherConnectToFailureIncompatibleFirmware = 2,
        bmdSwitcherConnectToFailureCorruptData = 3,
        bmdSwitcherConnectToFailureStateSync = 4,
        bmdSwitcherConnectToFailureStateSyncTimedOut = 5
    }

    // Transition Style Enum
    public enum _BMDSwitcherTransitionStyle
    {
        bmdSwitcherTransitionStyleMix = 0,
        bmdSwitcherTransitionStyleDip = 1,
        bmdSwitcherTransitionStyleWipe = 2,
        bmdSwitcherTransitionStyleDVE = 3,
        bmdSwitcherTransitionStyleStinger = 4
    }

    // Dummy interfaces/enums that are referenced but not used in your code
    public enum _BMDSwitcherVideoMode { }
    public enum _BMDSwitcherDownConversionMethod { }
    public enum _BMDSwitcherPowerStatus { }
    public enum _BMDSwitcherInputAvailability { }
    public enum _BMDSwitcherTransitionSelection { }
    public interface IBMDSwitcherVideoModeIterator { }
    public interface IBMDSwitcherDownConvertedModeIterator { }
    public interface IBMDSwitcherInputIterator { }
    public interface IBMDSwitcherMixEffectBlockIterator { }
    public interface IBMDSwitcherSerialPort { }
    public interface IBMDSwitcherMediaPlayerIterator { }
    public interface IBMDSwitcherMediaPool { }
    public interface IBMDSwitcherDownstreamKeyIterator { }
    public interface IBMDSwitcherMacroPool { }
    public interface IBMDSwitcherMacroControl { }
    public interface IBMDSwitcherAudioMixer { }
    public interface IBMDSwitcherCallback { }
    public interface IBMDSwitcherMixEffectBlockCallback { }
    public interface IBMDSwitcherDownstreamKeyCallback { }

    // IBMDSwitcherMixEffectBlock Interface - MUSS VOR IBMDSwitcher definiert werden!
    [ComImport]
    [Guid("74397703-2675-4b9e-9c1c-1f6e9c3c5c5f")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IBMDSwitcherMixEffectBlock
    {
        void GetInputAvailabilityMask(out _BMDSwitcherInputAvailability availabilityMask);
        void GetProgramInput(out long input);
        void SetProgramInput([In] long input);
        void GetPreviewInput(out long input);
        void SetPreviewInput([In] long input);
        void GetPreviewLive([MarshalAs(UnmanagedType.Bool)] out bool live);
        void GetPreviewTransition([MarshalAs(UnmanagedType.Bool)] out bool previewTransition);
        void SetPreviewTransition([In, MarshalAs(UnmanagedType.Bool)] bool previewTransition);
        void PerformAutoTransition();
        void PerformCut();
        void PerformFadeToBlack();
        void GetFadeToBlackRate(out uint frames);
        void SetFadeToBlackRate([In] uint frames);
        void GetFadeToBlackFullyBlack([MarshalAs(UnmanagedType.Bool)] out bool fullyBlack);
        void GetFadeToBlackFramesRemaining(out uint frames);
        void GetInTransition([MarshalAs(UnmanagedType.Bool)] out bool inTransition);
        void GetTransitionPosition(out double position);
        void SetTransitionPosition([In] double position);
        void GetTransitionFramesRemaining(out uint frames);
        void GetTransitionStyle(out _BMDSwitcherTransitionStyle style);
        void SetTransitionStyle([In] _BMDSwitcherTransitionStyle style);
        void GetNextTransitionStyle(out _BMDSwitcherTransitionStyle style);
        void SetNextTransitionStyle([In] _BMDSwitcherTransitionStyle style);
        void GetNextTransitionSelection(out _BMDSwitcherTransitionSelection selection);
        void SetNextTransitionSelection([In] _BMDSwitcherTransitionSelection selection);
        void GetInputCut(out long input);
        void SetInputCut([In] long input);
        void GetInputPreview(out long input);
        void SetInputPreview([In] long input);
        void AddCallback([In, MarshalAs(UnmanagedType.Interface)] IBMDSwitcherMixEffectBlockCallback callback);
        void RemoveCallback([In, MarshalAs(UnmanagedType.Interface)] IBMDSwitcherMixEffectBlockCallback callback);
    }

    // IBMDSwitcherDownstreamKey Interface - MUSS VOR IBMDSwitcher definiert werden!
    [ComImport]
    [Guid("4bbbddb5-730a-4e6e-a5f4-9c6e5b8f5e4a")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IBMDSwitcherDownstreamKey
    {
        void GetInputAvailabilityMask(out _BMDSwitcherInputAvailability availabilityMask);
        void GetInputCut(out long input);
        void SetInputCut([In] long input);
        void GetInputFill(out long input);
        void SetInputFill([In] long input);
        void GetOnAir(out int onAir);
        void SetOnAir([In] int onAir);
        void PerformAutoTransition();
        void GetRate(out uint frames);
        void SetRate([In] uint frames);
        void GetPreMultiplied([MarshalAs(UnmanagedType.Bool)] out bool preMultiplied);
        void SetPreMultiplied([In, MarshalAs(UnmanagedType.Bool)] bool preMultiplied);
        void GetClip(out double clip);
        void SetClip([In] double clip);
        void GetGain(out double gain);
        void SetGain([In] double gain);
        void GetInverse([MarshalAs(UnmanagedType.Bool)] out bool inverse);
        void SetInverse([In, MarshalAs(UnmanagedType.Bool)] bool inverse);
        void GetTie([MarshalAs(UnmanagedType.Bool)] out bool tie);
        void SetTie([In, MarshalAs(UnmanagedType.Bool)] bool tie);
        void AddCallback([In, MarshalAs(UnmanagedType.Interface)] IBMDSwitcherDownstreamKeyCallback callback);
        void RemoveCallback([In, MarshalAs(UnmanagedType.Interface)] IBMDSwitcherDownstreamKeyCallback callback);
    }

    // IBMDSwitcher Interface - NACH den anderen Interfaces!
    [ComImport]
    [Guid("8f8c9eae-1a58-4f13-969e-1b4c7c666c91")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IBMDSwitcher
    {
        void DoesSupportVideoModes([MarshalAs(UnmanagedType.Bool)] out bool supportsVideoModes);
        void GetVideoMode(out _BMDSwitcherVideoMode videoMode);
        void SetVideoMode([In] _BMDSwitcherVideoMode videoMode);
        void DoesSupportDownConvertedHDVideoMode([In] _BMDSwitcherVideoMode coreVideoMode, [In] _BMDSwitcherDownConversionMethod conversionMethod, [MarshalAs(UnmanagedType.Bool)] out bool supportsVideoMode);
        void GetDownConvertedHDVideoMode([In] _BMDSwitcherVideoMode coreVideoMode, out _BMDSwitcherDownConversionMethod conversionMethod);
        void SetDownConvertedHDVideoMode([In] _BMDSwitcherVideoMode coreVideoMode, [In] _BMDSwitcherDownConversionMethod conversionMethod);
        void Does3GSDIOutputLevelAFollowInput([MarshalAs(UnmanagedType.Bool)] out bool follows);
        void GetPowerStatus(out _BMDSwitcherPowerStatus powerStatus);
        void GetMethodForSupportedVideoModes([MarshalAs(UnmanagedType.Interface)] out IBMDSwitcherVideoModeIterator iterator);
        void GetMethodForSupportedDownConvertedHDVideoModes([In] _BMDSwitcherVideoMode coreVideoMode, [MarshalAs(UnmanagedType.Interface)] out IBMDSwitcherDownConvertedModeIterator iterator);
        void GetProductName([MarshalAs(UnmanagedType.BStr)] out string productName);
        void GetInputs([MarshalAs(UnmanagedType.Interface)] out IBMDSwitcherInputIterator iterator);
        void GetMixEffectBlocks([MarshalAs(UnmanagedType.Interface)] out IBMDSwitcherMixEffectBlockIterator iterator);
        void GetMixEffectBlock([In] uint index, [MarshalAs(UnmanagedType.Interface)] out IBMDSwitcherMixEffectBlock mixEffectBlock);
        void GetSerialPort([MarshalAs(UnmanagedType.Interface)] out IBMDSwitcherSerialPort serialPort);
        void GetMediaPlayers([MarshalAs(UnmanagedType.Interface)] out IBMDSwitcherMediaPlayerIterator iterator);
        void GetMediaPool([MarshalAs(UnmanagedType.Interface)] out IBMDSwitcherMediaPool mediaPool);
        void GetDownstreamKeys([MarshalAs(UnmanagedType.Interface)] out IBMDSwitcherDownstreamKeyIterator iterator);
        void GetDownstreamKey([In] uint index, [MarshalAs(UnmanagedType.Interface)] out IBMDSwitcherDownstreamKey downstreamKey);
        void GetMacroPool([MarshalAs(UnmanagedType.Interface)] out IBMDSwitcherMacroPool macroPool);
        void GetMacroControl([MarshalAs(UnmanagedType.Interface)] out IBMDSwitcherMacroControl macroControl);
        void GetAudioMixer([MarshalAs(UnmanagedType.Interface)] out IBMDSwitcherAudioMixer audioMixer);
        void CreateIterator([In, MarshalAs(UnmanagedType.LPStruct)] Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object iterator);
        void AddCallback([In, MarshalAs(UnmanagedType.Interface)] IBMDSwitcherCallback callback);
        void RemoveCallback([In, MarshalAs(UnmanagedType.Interface)] IBMDSwitcherCallback callback);
    }

    // IBMDSwitcherDiscovery Interface
    [ComImport]
    [Guid("5be80c8e-34ff-4815-9754-b3e85a000df0")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IBMDSwitcherDiscovery
    {
        void ConnectTo(
            [In, MarshalAs(UnmanagedType.BStr)] string deviceAddress,
            [MarshalAs(UnmanagedType.Interface)] out IBMDSwitcher switcherDevice,
            out _BMDSwitcherConnectToFailure failReason);
    }

    // CoClass for Discovery
    [ComImport]
    [Guid("1c18b3a0-d9c4-4c3d-9e5d-4e3d5c6d7e8f")]
    [ClassInterface(ClassInterfaceType.None)]
    public class CBMDSwitcherDiscovery
    {
    }
}