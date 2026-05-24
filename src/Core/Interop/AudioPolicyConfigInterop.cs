using System.Runtime.InteropServices;

namespace DragonOS.AudioConfigurator.Core.Interop;

// ============================================================================
// IAudioPolicyConfigFactory — Undocumented Windows COM Interface
//
// WARNING: This interface is NOT part of the public Windows SDK. It was
// reverse-engineered from AudioSes.dll and is subject to change in future
// Windows builds without notice. It has remained stable from Windows 10 1607
// through Windows 11 24H2 (as of the project's knowledge cutoff).
//
// CANONICAL OPEN-SOURCE REFERENCE:
//   EarTrumpet by File-New-Project
//   https://github.com/File-New-Project/EarTrumpet
//   Specifically: EarTrumpet/DataModel/Audio/Internals/PolicyConfig.cs
//
// If you encounter access violations or E_NOINTERFACE on a newer Windows build,
// validate the vtable layout against the current EarTrumpet source before filing
// a bug against this project.
//
// HOW IT WORKS:
//   Windows 10 Anniversary Update (1607) introduced the "App volume and device
//   preferences" panel (ms-settings:apps-volume). The backing COM interface,
//   IAudioPolicyConfigFactory, exposes SetPersistedDefaultAudioEndpoint — which
//   writes a per-process audio endpoint preference to an internal Windows Audio
//   policy store. This is what Windows itself uses when you change an app's
//   output device in the Settings UI.
//
// WHY NOT IPolicyConfig?
//   IPolicyConfig (a different, older undocumented interface) changes the
//   SYSTEM-WIDE default audio device — affecting every running application.
//   IAudioPolicyConfigFactory targets a specific process by PID.
//
// ============================================================================

/// <summary>
/// The undocumented COM interface that backs Windows 10/11 per-application audio
/// endpoint routing ("App volume and device preferences" in Windows Settings).
/// </summary>
/// <remarks>
/// <para>
/// <b>Interface GUID:</b> <c>{2a59116d-6c4f-45e0-a74f-707e3fef9258}</c>
/// </para>
/// <para>
/// <b>Class GUID:</b> <c>{870af99c-171d-4f9e-af0d-e63df40c2bc9}</c> (<c>CPolicyConfigClient</c>)
/// </para>
/// <para>
/// <b>Vtable layout:</b> Offsets 0–9 are internal Windows audio policy methods that
/// are not useful to callers. They are declared here as <c>__vtbl_pad_N</c> stubs
/// because the COM vtable is positional — every slot must be accounted for, or
/// subsequent method calls will invoke the wrong function pointer and crash the process.
/// </para>
/// <para>
/// <b>String parameter type:</b> Device IDs are passed as HSTRING (Windows Runtime
/// string handles) rather than raw LPWSTR. Lifetime is managed explicitly via
/// <see cref="NativeInterop.WindowsCreateString"/> and <see cref="NativeInterop.WindowsDeleteString"/>.
/// </para>
/// </remarks>
[ComImport]
[Guid("2a59116d-6c4f-45e0-a74f-707e3fef9258")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioPolicyConfigFactory
{
    // -------------------------------------------------------------------------
    // Vtable offsets 0–9: Internal Windows audio policy event registrations
    // and container management methods. These are not useful to callers of
    // the per-app routing API. Each stub must be declared to preserve the
    // vtable offset of GetPersistedDefaultAudioEndpoint (offset 10) and
    // SetPersistedDefaultAudioEndpoint (offset 11).
    // -------------------------------------------------------------------------
#pragma warning disable IDE1006 // Naming convention — intentional vtable padding names.

    [PreserveSig] int __vtbl_pad_0();  // add_CtxVolumeChange
    [PreserveSig] int __vtbl_pad_1();  // remove_CtxVolumeChange
    [PreserveSig] int __vtbl_pad_2();  // add_RingerVibrateStateChanged
    [PreserveSig] int __vtbl_pad_3();  // remove_RingerVibrateStateChanged
    [PreserveSig] int __vtbl_pad_4();  // GetTelemetryId
    [PreserveSig] int __vtbl_pad_5();  // SetTelemetryId
    [PreserveSig] int __vtbl_pad_6();  // GetContainerCount
    [PreserveSig] int __vtbl_pad_7();  // GetContainer
    [PreserveSig] int __vtbl_pad_8();  // GetContainerAudioDeviceId
    [PreserveSig] int __vtbl_pad_9();  // SetContainerAudioDeviceId

#pragma warning restore IDE1006

    // Vtable offset 10.
    /// <summary>
    /// Reads the persisted per-process audio endpoint preference for the given process.
    /// </summary>
    /// <param name="processId">
    /// The Win32 process ID of the target application. Obtain from
    /// <see cref="System.Diagnostics.Process.Id"/>.
    /// </param>
    /// <param name="flow">
    /// The data-flow direction. Use <see cref="EDataFlow.eRender"/> for audio output.
    /// </param>
    /// <param name="role">
    /// The endpoint role. Use <see cref="ERole.eMultimedia"/> for music/media applications.
    /// </param>
    /// <param name="hstringDeviceId">
    /// Receives an HSTRING handle containing the endpoint ID string.
    /// The caller must release this with <see cref="NativeInterop.WindowsDeleteString"/>.
    /// Returns <see cref="IntPtr.Zero"/> if no preference has been set for this process.
    /// </param>
    /// <returns>
    /// S_OK (0) on success. E_NOTIMPL or a negative HRESULT if the Windows build does
    /// not support per-process routing (pre-1607) or if the vtable is misaligned.
    /// </returns>
    [PreserveSig]
    int GetPersistedDefaultAudioEndpoint(
        uint processId,
        EDataFlow flow,
        ERole role,
        out IntPtr hstringDeviceId);

    // Vtable offset 11 — THE KEY METHOD.
    /// <summary>
    /// Writes a per-process audio endpoint preference for the given process.
    /// This is the exact mechanism Windows Settings uses internally when the user
    /// selects a different output device in "App volume and device preferences".
    /// </summary>
    /// <param name="processId">
    /// The Win32 process ID of the target application.
    /// For applications that spawn multiple processes (Electron, CEF), call this
    /// method once per process ID to ensure all audio sessions are routed.
    /// </param>
    /// <param name="flow">
    /// Use <see cref="EDataFlow.eRender"/> to route the application's audio output.
    /// </param>
    /// <param name="role">
    /// Use <see cref="ERole.eMultimedia"/> for music/media playback applications.
    /// Use <see cref="ERole.eCommunications"/> additionally for VoIP applications.
    /// </param>
    /// <param name="hstringDeviceId">
    /// An HSTRING handle wrapping the target endpoint's ID string.
    /// Create this with <see cref="NativeInterop.WindowsCreateString"/> and release
    /// with <see cref="NativeInterop.WindowsDeleteString"/> after this call returns.
    /// The endpoint ID must be obtained from <c>IMMDevice::GetId</c> — never fabricated.
    /// </param>
    /// <returns>
    /// S_OK (0) on success.
    /// E_INVALIDARG if the device ID is invalid or the process does not exist.
    /// A negative HRESULT if the vtable is misaligned (indicates a Windows build change).
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>Effect timing:</b> The preference is written immediately but the running
    /// application may not switch its audio stream until it opens a new audio session.
    /// Spotify, for example, honours the new endpoint on the next track transition
    /// or after a restart.
    /// </para>
    /// <para>
    /// <b>Persistence:</b> The preference survives process restarts and is stored per
    /// application executable path in the Windows audio policy store.
    /// </para>
    /// </remarks>
    [PreserveSig]
    int SetPersistedDefaultAudioEndpoint(
        uint processId,
        EDataFlow flow,
        ERole role,
        IntPtr hstringDeviceId);
}

/// <summary>
/// The concrete COM class that implements <see cref="IAudioPolicyConfigFactory"/>.
/// Activate with <c>new AudioPolicyConfigFactoryComObject()</c> then cast to
/// <see cref="IAudioPolicyConfigFactory"/>.
/// </summary>
/// <remarks>
/// CLSID: <c>{870af99c-171d-4f9e-af0d-e63df40c2bc9}</c> (<c>CPolicyConfigClient</c> in AudioSes.dll)
/// </remarks>
[ComImport]
[Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
[ClassInterface(ClassInterfaceType.None)]
internal class AudioPolicyConfigFactoryComObject { }

// ============================================================================
// IPolicyConfig — System-wide default audio endpoint control
//
// This is the OLDER, separate undocumented interface used to change the
// Windows SYSTEM default playback device (the global setting, not per-app).
// It lives on the same COM class (CPolicyConfigClient) as IAudioPolicyConfigFactory
// but exposes a completely different vtable via a different IID.
//
// We use it exclusively for the brief system-default swap: set default to
// CABLE Input for ~500 ms so Spotify's live audio session receives a
// WM_DEVICECHANGE / device-removed notification and reopens on CABLE Input,
// then immediately restore the original default so no other app is affected.
//
// CANONICAL REFERENCE:
//   SoundSwitch: https://github.com/Belphemur/SoundSwitch (AudioPolicyConfig.cs)
//   EarTrumpet:  https://github.com/File-New-Project/EarTrumpet (PolicyConfig.cs)
//
// VTABLE LAYOUT (offsets 3–14 after IUnknown):
//   3:  GetMixFormat          4:  GetDeviceFormat
//   5:  ResetDeviceFormat     6:  SetDeviceFormat
//   7:  GetProcessingPeriod   8:  SetProcessingPeriod
//   9:  GetShareMode          10: SetShareMode
//   11: GetPropertyValue      12: SetPropertyValue
//   13: SetDefaultEndpoint    ← THE KEY METHOD
//   14: SetEndpointVisibility
// ============================================================================

/// <summary>
/// Undocumented COM interface for changing the Windows system-wide default
/// audio endpoint. Activate via <see cref="AudioPolicyConfigFactoryComObject"/>.
/// </summary>
[ComImport]
[Guid("f8679f50-850a-41cf-9c72-430f290290c8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
#pragma warning disable IDE1006
    [PreserveSig] int __pad_GetMixFormat(IntPtr a, IntPtr b);
    [PreserveSig] int __pad_GetDeviceFormat(IntPtr a, bool b, IntPtr c);
    [PreserveSig] int __pad_ResetDeviceFormat(IntPtr a);
    [PreserveSig] int __pad_SetDeviceFormat(IntPtr a, IntPtr b, IntPtr c);
    [PreserveSig] int __pad_GetProcessingPeriod(IntPtr a, bool b, IntPtr c, IntPtr d);
    [PreserveSig] int __pad_SetProcessingPeriod(IntPtr a, IntPtr b);
    [PreserveSig] int __pad_GetShareMode(IntPtr a, IntPtr b);
    [PreserveSig] int __pad_SetShareMode(IntPtr a, IntPtr b);
    [PreserveSig] int __pad_GetPropertyValue(IntPtr a, bool b, IntPtr c, IntPtr d);
    [PreserveSig] int __pad_SetPropertyValue(IntPtr a, bool b, IntPtr c, IntPtr d);
#pragma warning restore IDE1006

    /// <summary>
    /// Sets the system-wide default audio endpoint for the given role.
    /// Use sparingly — this changes the global playback device for all applications.
    /// </summary>
    [PreserveSig]
    int SetDefaultEndpoint(
        [MarshalAs(UnmanagedType.LPWStr)] string pszDeviceId,
        ERole role);
}

// ============================================================================
// IAudioPolicyConfigFactoryWin11 — Windows 11 21H2+ (build >= 21390)
//
// Windows 11 ships a revised interface with additional volume-group and chat-
// application methods inserted before SetPersistedDefaultAudioEndpoint, pushing
// it from vtable offset 11 (Win10) to offset 25 (Win11, after IInspectable base).
//
// ACTIVATION: RoGetActivationFactory("Windows.Media.Internal.AudioPolicyConfig")
//   instead of CoCreateInstance(CLSID_CPolicyConfigClient).
//
// INTERFACE TYPE: InterfaceIsIInspectable
//   The CLR implicitly handles the six IUnknown + IInspectable slots
//   (QueryInterface, AddRef, Release, GetIids, GetRuntimeClassName, GetTrustLevel).
//   The methods declared below begin at the next vtable slot after those six.
//
// VTABLE (relative to IInspectable base, i.e. after the 6 implicit slots):
//   Offset  0: add_CtxVolumeChange
//   Offset  1: remove_CtxVolumeChange
//   Offset  2: add_RingerVibrateStateChanged
//   Offset  3: remove_RingerVibrateStateChanged
//   Offset  4: SetVolumeGroupGainForId         (Win11 new)
//   Offset  5: GetVolumeGroupGainForId         (Win11 new)
//   Offset  6: GetActiveVolumeGroupForEndpointId (Win11 new)
//   Offset  7: GetVolumeGroupsForEndpoint       (Win11 new)
//   Offset  8: GetCurrentVolumeContext          (Win11 new)
//   Offset  9: SetVolumeGroupMuteForId          (Win11 new)
//   Offset 10: GetVolumeGroupMuteForId          (Win11 new)
//   Offset 11: SetRingerVibrateState            (Win11 new)
//   Offset 12: GetRingerVibrateState            (Win11 new)
//   Offset 13: SetPreferredChatApplication      (Win11 new)
//   Offset 14: ResetPreferredChatApplication    (Win11 new)
//   Offset 15: GetPreferredChatApplication      (Win11 new)
//   Offset 16: GetCurrentChatApplications       (Win11 new)
//   Offset 17: add_ChatContextChanged           (Win11 new)
//   Offset 18: remove_ChatContextChanged        (Win11 new)
//   Offset 19: SetPersistedDefaultAudioEndpoint ← KEY METHOD (NOTE: Set before Get, reversed from Win10)
//   Offset 20: GetPersistedDefaultAudioEndpoint
//   Offset 21: ClearAllPersistedApplicationDefaultEndpoints
//
// SOURCE REFERENCE:
//   SoundSwitch: https://github.com/Belphemur/SoundSwitch (AudioPolicyConfig.cs)
//   EarTrumpet:  https://github.com/File-New-Project/EarTrumpet (PolicyConfig.cs)
// ============================================================================

/// <summary>
/// The Windows 11 (build 21390+) revision of the per-application audio policy
/// factory interface. Obtained via WinRT activation — see
/// <c>AudioRouterService</c> for usage.
/// </summary>
/// <remarks>
/// <para>
/// <b>Interface GUID:</b> <c>{ab3d4648-e242-459f-b02f-541c70306324}</c>
/// </para>
/// <para>
/// <b>InterfaceType:</b> Declared as <c>InterfaceIsIUnknown</c> (not IInspectable)
/// because <c>ComInterfaceType.InterfaceIsIInspectable</c> is unsupported in .NET Core /
/// .NET 5+. The three IInspectable slots (GetIids, GetRuntimeClassName, GetTrustLevel)
/// are therefore declared explicitly as vtable padding at offsets 3–5.
/// </para>
/// <para>
/// <b>Key difference from Win10:</b> <c>SetPersistedDefaultAudioEndpoint</c> and
/// <c>GetPersistedDefaultAudioEndpoint</c> have their order swapped compared to the
/// Windows 10 interface; Set now precedes Get.
/// </para>
/// </remarks>
[ComImport]
[Guid("ab3d4648-e242-459f-b02f-541c70306324")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioPolicyConfigFactoryWin11
{
    // 22 vtable padding slots total — must all be declared to preserve positional correctness.
    //
    // Absolute vtable layout (CLR owns slots 0–2 for IUnknown):
    //   Slot  3: GetIids             (IInspectable)
    //   Slot  4: GetRuntimeClassName (IInspectable)
    //   Slot  5: GetTrustLevel       (IInspectable)
    //   Slots 6–24: Win11 audio policy application methods (see comment block above)
    //   Slot 25: SetPersistedDefaultAudioEndpoint  ← KEY METHOD
    //   Slot 26: GetPersistedDefaultAudioEndpoint
    //   Slot 27: ClearAllPersistedApplicationDefaultEndpoints
#pragma warning disable IDE1006

    // IInspectable slots (3–5) — not handled by the CLR when InterfaceIsIUnknown is used.
    [PreserveSig] int __pad_GetIids();
    [PreserveSig] int __pad_GetRuntimeClassName();
    [PreserveSig] int __pad_GetTrustLevel();

    // Win11 audio policy application methods (slots 6–24).
    [PreserveSig] int __pad_add_CtxVolumeChange();
    [PreserveSig] int __pad_remove_CtxVolumeChange();
    [PreserveSig] int __pad_add_RingerVibrateStateChanged();
    [PreserveSig] int __pad_remove_RingerVibrateStateChanged();
    [PreserveSig] int __pad_SetVolumeGroupGainForId();
    [PreserveSig] int __pad_GetVolumeGroupGainForId();
    [PreserveSig] int __pad_GetActiveVolumeGroupForEndpointId();
    [PreserveSig] int __pad_GetVolumeGroupsForEndpoint();
    [PreserveSig] int __pad_GetCurrentVolumeContext();
    [PreserveSig] int __pad_SetVolumeGroupMuteForId();
    [PreserveSig] int __pad_GetVolumeGroupMuteForId();
    [PreserveSig] int __pad_SetRingerVibrateState();
    [PreserveSig] int __pad_GetRingerVibrateState();
    [PreserveSig] int __pad_SetPreferredChatApplication();
    [PreserveSig] int __pad_ResetPreferredChatApplication();
    [PreserveSig] int __pad_GetPreferredChatApplication();
    [PreserveSig] int __pad_GetCurrentChatApplications();
    [PreserveSig] int __pad_add_ChatContextChanged();
    [PreserveSig] int __pad_remove_ChatContextChanged();

#pragma warning restore IDE1006

    // Vtable offset 19 — THE KEY METHOD. NOTE: Set precedes Get on Win11, reversed from Win10.
    /// <summary>
    /// Writes a per-process audio endpoint preference for the given process.
    /// Semantics are identical to the Win10 counterpart.
    /// </summary>
    [PreserveSig]
    int SetPersistedDefaultAudioEndpoint(
        uint processId,
        EDataFlow flow,
        ERole role,
        IntPtr hstringDeviceId);

    // Vtable offset 20.
    /// <summary>
    /// Reads the persisted per-process audio endpoint preference for the given process.
    /// </summary>
    [PreserveSig]
    int GetPersistedDefaultAudioEndpoint(
        uint processId,
        EDataFlow flow,
        ERole role,
        out IntPtr hstringDeviceId);

    // Vtable offset 21.
    /// <summary>
    /// Resets all per-application audio endpoint preferences to the system default.
    /// </summary>
    [PreserveSig]
    int ClearAllPersistedApplicationDefaultEndpoints();
}
