namespace DragonOS.AudioConfigurator.Core.Interop;

// ============================================================================
// Windows Core Audio API — Enumerations
//
// These mirror the C++ enumerations defined in <mmdeviceapi.h> and <audiopolicy.h>.
// Values must match the Windows SDK exactly — do not renumber.
// Reference: https://learn.microsoft.com/en-us/windows/win32/coreaudio/core-audio-enumerations
// ============================================================================

/// <summary>
/// Indicates the direction of audio data flow for an endpoint device.
/// Mirrors the Win32 <c>EDataFlow</c> enumeration from <c>mmdeviceapi.h</c>.
/// </summary>
public enum EDataFlow
{
    /// <summary>Audio rendering (output) — speakers, headphones, virtual cables.</summary>
    eRender = 0,

    /// <summary>Audio capture (input) — microphones, line-in.</summary>
    eCapture = 1,

    /// <summary>Both rendering and capture.</summary>
    eAll = 2,
}

/// <summary>
/// Defines the role that the system has assigned to an audio endpoint device.
/// Mirrors the Win32 <c>ERole</c> enumeration from <c>mmdeviceapi.h</c>.
/// </summary>
/// <remarks>
/// When calling <c>IAudioPolicyConfigFactory.SetPersistedDefaultAudioEndpoint</c>
/// for general application audio (e.g. music playback), use <see cref="eMultimedia"/>.
/// <see cref="eCommunications"/> is reserved for VoIP / voice-chat applications.
/// </remarks>
public enum ERole
{
    /// <summary>
    /// Games, system notification sounds, and voice commands.
    /// Maps to the "Default" device in Sound Settings.
    /// </summary>
    eConsole = 0,

    /// <summary>
    /// Music, movies, and narration.
    /// Maps to the "Default" device in Sound Settings (same physical device as eConsole
    /// unless the user has explicitly separated them).
    /// </summary>
    eMultimedia = 1,

    /// <summary>
    /// Voice communications (VoIP, conferencing).
    /// Maps to the "Default Communications Device" in Sound Settings.
    /// </summary>
    eCommunications = 2,
}

/// <summary>
/// Bitmask flags that filter <c>IMMDeviceEnumerator.EnumAudioEndpoints</c> results
/// by the current operational state of each endpoint.
/// Mirrors the Win32 <c>DEVICE_STATE_XXX</c> constants from <c>mmdeviceapi.h</c>.
/// </summary>
[Flags]
public enum DeviceState : uint
{
    /// <summary>
    /// The device is active — the audio adapter is present and the driver is enabled.
    /// This is the only state relevant to audio routing operations.
    /// </summary>
    Active = 0x00000001,

    /// <summary>The device is disabled in Device Manager.</summary>
    Disabled = 0x00000002,

    /// <summary>The device is not present (e.g. USB headset unplugged).</summary>
    NotPresent = 0x00000004,

    /// <summary>The device is present but unplugged (e.g. jack sense detected no cable).</summary>
    Unplugged = 0x00000008,

    /// <summary>All states — use to enumerate every known endpoint regardless of state.</summary>
    All = 0x0000000F,
}
