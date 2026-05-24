using System.Runtime.InteropServices;

namespace DragonOS.AudioConfigurator.Core.Interop;

// ============================================================================
// Windows Core Audio API — COM Interface Declarations
//
// These are the DOCUMENTED Windows Core Audio interfaces required for device
// enumeration. GUIDs and vtable layouts are stable across all Windows 10/11 builds.
//
// SDK Reference: https://learn.microsoft.com/en-us/windows/win32/coreaudio/mmdevice-api
// Header:        mmdeviceapi.h (Windows SDK)
// ============================================================================

/// <summary>
/// COM interface wrapping a single Windows audio endpoint device.
/// Provides access to the device's unique ID and property store (friendly name, etc.).
/// </summary>
/// <remarks>
/// Instances are obtained exclusively from <see cref="IMMDeviceEnumerator"/> methods —
/// never instantiated directly. The interface is documented in the Windows SDK.
/// </remarks>
[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    /// <summary>
    /// Activates a COM interface on the audio endpoint (e.g. IAudioClient).
    /// Not used by the audio routing pipeline — present to preserve vtable alignment.
    /// </summary>
    [PreserveSig]
    int Activate(
        ref Guid iid,
        uint dwClsCtx,
        IntPtr pActivationParams,
        [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);

    /// <summary>
    /// Opens the property store for this device, enabling reads of properties
    /// such as <c>PKEY_Device_FriendlyName</c>.
    /// </summary>
    [PreserveSig]
    int OpenPropertyStore(
        StorageAccessMode stgmAccess,
        out IPropertyStore ppProperties);

    /// <summary>
    /// Returns the opaque Windows endpoint ID string for this device.
    /// This is the value required by <c>IAudioPolicyConfigFactory.SetPersistedDefaultAudioEndpoint</c>.
    /// </summary>
    /// <param name="ppstrId">
    /// Receives a pointer to a null-terminated wide-character string.
    /// The caller is responsible for freeing this with <c>CoTaskMemFree</c>.
    /// The .NET runtime handles this automatically when marshalled as <c>LPWStr</c>.
    /// </param>
    [PreserveSig]
    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);

    /// <summary>
    /// Returns the current operational state of the device (active, disabled, etc.).
    /// Not used by the audio routing pipeline — present to preserve vtable alignment.
    /// </summary>
    [PreserveSig]
    int GetState(out DeviceState pdwState);
}

/// <summary>
/// COM interface representing a snapshot collection of audio endpoint devices,
/// returned by <see cref="IMMDeviceEnumerator.EnumAudioEndpoints"/>.
/// </summary>
[ComImport]
[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    /// <summary>Returns the number of devices in this collection.</summary>
    [PreserveSig]
    int GetCount(out uint pcDevices);

    /// <summary>Returns the device at zero-based <paramref name="nDevice"/> index.</summary>
    [PreserveSig]
    int Item(uint nDevice, out IMMDevice ppDevice);
}

/// <summary>
/// The primary COM interface for enumerating Windows audio endpoint devices.
/// Instantiate via <see cref="MMDeviceEnumeratorComObject"/> and cast to this interface.
/// </summary>
/// <remarks>
/// This is a fully documented, stable Windows API. It is safe to call on any
/// Windows Vista or later build.
/// Reference: https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/nn-mmdeviceapi-immdeviceenumerator
/// </remarks>
[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    /// <summary>
    /// Returns a collection of all audio endpoint devices matching the given
    /// <paramref name="dataFlow"/> direction and <paramref name="dwStateMask"/> bitmask.
    /// </summary>
    /// <param name="dataFlow">
    /// <see cref="EDataFlow.eRender"/> to enumerate output devices (speakers, virtual cables).
    /// </param>
    /// <param name="dwStateMask">
    /// Use <see cref="DeviceState.Active"/> to include only ready endpoints.
    /// </param>
    /// <param name="ppDevices">Receives the populated collection on S_OK.</param>
    [PreserveSig]
    int EnumAudioEndpoints(
        EDataFlow dataFlow,
        DeviceState dwStateMask,
        out IMMDeviceCollection ppDevices);

    /// <summary>
    /// Returns the current system-wide default audio endpoint for the given
    /// <paramref name="dataFlow"/> and <paramref name="role"/>.
    /// </summary>
    [PreserveSig]
    int GetDefaultAudioEndpoint(
        EDataFlow dataFlow,
        ERole role,
        out IMMDevice ppEndpoint);

    /// <summary>
    /// Returns the device with the given <paramref name="pwstrId"/> endpoint ID string.
    /// Useful for round-tripping from a stored ID back to an <see cref="IMMDevice"/>.
    /// </summary>
    [PreserveSig]
    int GetDevice(
        [MarshalAs(UnmanagedType.LPWStr)] string pwstrId,
        out IMMDevice ppDevice);

    /// <summary>
    /// Registers a notification client for device state changes.
    /// Not used by this pipeline — present to preserve vtable alignment.
    /// </summary>
    [PreserveSig]
    int RegisterEndpointNotificationCallback(IntPtr pClient);

    /// <summary>
    /// Unregisters a previously registered notification client.
    /// Present to preserve vtable alignment.
    /// </summary>
    [PreserveSig]
    int UnregisterEndpointNotificationCallback(IntPtr pClient);
}

/// <summary>
/// The concrete COM class that implements <see cref="IMMDeviceEnumerator"/>.
/// Activate with <c>new MMDeviceEnumeratorComObject()</c> then cast to
/// <see cref="IMMDeviceEnumerator"/>.
/// </summary>
/// <remarks>
/// CLSID: <c>{BCDE0395-E52F-467C-8E3D-C4579291692E}</c> (MMDeviceEnumerator)
/// </remarks>
[ComImport]
[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
[ClassInterface(ClassInterfaceType.None)]
internal class MMDeviceEnumeratorComObject { }

/// <summary>
/// COM interface for reading key-value properties from a Windows Shell property store.
/// Used here exclusively to retrieve <c>PKEY_Device_FriendlyName</c>.
/// </summary>
[ComImport]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    /// <summary>Returns the number of properties in this store.</summary>
    [PreserveSig]
    int GetCount(out uint cProps);

    /// <summary>Returns the property key at the given <paramref name="iProp"/> index.</summary>
    [PreserveSig]
    int GetAt(uint iProp, out PROPERTYKEY pkey);

    /// <summary>Returns the value for the property identified by <paramref name="key"/>.</summary>
    [PreserveSig]
    int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);

    /// <summary>Sets a property value. Not used by this pipeline.</summary>
    [PreserveSig]
    int SetValue(ref PROPERTYKEY key, ref PROPVARIANT pv);

    /// <summary>Persists pending property changes. Not used by this pipeline.</summary>
    [PreserveSig]
    int Commit();
}

/// <summary>
/// Maps to the Win32 <c>PROPERTYKEY</c> struct: a GUID + property ID pair
/// that uniquely identifies a Shell property.
/// </summary>
/// <remarks>
/// The friendly name key is:
/// <c>fmtid = {a45c254e-df1c-4efd-8020-67d146a850e0}, pid = 14</c>
/// which corresponds to <c>PKEY_Device_FriendlyName</c> in <c>functiondiscoverykeys_devpkey.h</c>.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct PROPERTYKEY
{
    /// <summary>The format GUID of the property.</summary>
    public Guid fmtid;

    /// <summary>The property identifier within the format GUID namespace.</summary>
    public uint pid;

    /// <summary>
    /// The <c>PKEY_Device_FriendlyName</c> property key — returns the human-readable
    /// device name shown in Windows Sound Settings.
    /// </summary>
    public static readonly PROPERTYKEY DeviceFriendlyName = new()
    {
        fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
        pid   = 14,
    };
}

/// <summary>
/// A minimal, fixed-layout overlay on the Win32 <c>PROPVARIANT</c> union,
/// sufficient for reading string-valued device properties.
/// </summary>
/// <remarks>
/// The full <c>PROPVARIANT</c> union is 16 bytes on x64. This struct exposes
/// only the variant type (<see cref="vt"/>) and the pointer field that holds
/// the string data when <c>vt == VT_LPWSTR (31)</c>.
/// For robust production usage, replace with the full propidl.h layout or use
/// the PropVariant wrapper from the Windows API Code Pack.
/// </remarks>
[StructLayout(LayoutKind.Explicit, Size = 16)]
internal struct PROPVARIANT
{
    /// <summary>
    /// The VARTYPE discriminator.
    /// <c>VT_LPWSTR = 31</c> indicates a null-terminated wide string at <see cref="pwszVal"/>.
    /// </summary>
    [FieldOffset(0)]
    public ushort vt;

    /// <summary>Reserved — do not access directly.</summary>
    [FieldOffset(2)]
    private readonly ushort wReserved1;

    /// <summary>Reserved — do not access directly.</summary>
    [FieldOffset(4)]
    private readonly ushort wReserved2;

    /// <summary>Reserved — do not access directly.</summary>
    [FieldOffset(6)]
    private readonly ushort wReserved3;

    /// <summary>
    /// Valid only when <see cref="vt"/> equals <c>31</c> (VT_LPWSTR).
    /// Points to a null-terminated Unicode string allocated with CoTaskMemAlloc.
    /// </summary>
    [FieldOffset(8)]
    public IntPtr pwszVal;

    /// <summary>
    /// Extracts the string value from this PROPVARIANT.
    /// Returns <see langword="null"/> if the variant type is not <c>VT_LPWSTR</c>.
    /// </summary>
    public readonly string? ToStringValue()
        => vt == 31 // VT_LPWSTR
            ? Marshal.PtrToStringUni(pwszVal)
            : null;
}

// ----------------------------------------------------------------------------
// WASAPI Audio Session Management
// Used to find the specific Spotify process that owns an active audio session.
// Reference: https://learn.microsoft.com/en-us/windows/win32/coreaudio/wasapi
// ----------------------------------------------------------------------------

/// <summary>The operational state of an audio session.</summary>
internal enum AudioSessionState
{
    Inactive = 0,
    Active   = 1,
    Expired  = 2,
}

/// <summary>
/// Manages and enumerates audio sessions on an audio endpoint device.
/// Obtained by calling <see cref="IMMDevice.Activate"/> with this interface's GUID.
/// </summary>
[ComImport]
[Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionManager2
{
    // IAudioSessionManager base (vtable 3-4) — not needed, padded.
    [PreserveSig] int __pad_GetAudioSessionControl(IntPtr a, uint b, IntPtr c);
    [PreserveSig] int __pad_GetSimpleAudioVolume(IntPtr a, uint b, IntPtr c);

    /// <summary>Returns an enumerator over all current audio sessions on this device.</summary>
    [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessionEnum);

    [PreserveSig] int __pad_RegisterSessionNotification(IntPtr a);
    [PreserveSig] int __pad_UnregisterSessionNotification(IntPtr a);
    [PreserveSig] int __pad_RegisterDuckNotification(IntPtr a, IntPtr b);
    [PreserveSig] int __pad_UnregisterDuckNotification(IntPtr a);
}

/// <summary>Enumerates audio sessions on an audio endpoint device.</summary>
[ComImport]
[Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEnumerator
{
    [PreserveSig] int GetCount(out int sessionCount);

    /// <summary>
    /// Returns the session control at <paramref name="index"/>.
    /// The CLR automatically QIs the returned pointer for <see cref="IAudioSessionControl2"/>.
    /// </summary>
    [PreserveSig] int GetSession(int index, out IAudioSessionControl2 session);
}

/// <summary>
/// Extended audio session control. Inherits IAudioSessionControl's vtable (offsets 3-11)
/// then adds its own methods (offsets 12-16). All base methods are redeclared here with
/// vtable padding so the CLR sees the correct slot layout without COM interface inheritance.
/// </summary>
[ComImport]
[Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl2
{
    // IAudioSessionControl base (vtable 3-11).
    [PreserveSig] int GetState(out AudioSessionState pRetVal);
    [PreserveSig] int __pad_GetDisplayName(IntPtr a);
    [PreserveSig] int __pad_SetDisplayName(IntPtr a, IntPtr b);
    [PreserveSig] int __pad_GetIconPath(IntPtr a);
    [PreserveSig] int __pad_SetIconPath(IntPtr a, IntPtr b);
    [PreserveSig] int __pad_GetGroupingParam(IntPtr a);
    [PreserveSig] int __pad_SetGroupingParam(IntPtr a, IntPtr b);
    [PreserveSig] int __pad_RegisterNotification(IntPtr a);
    [PreserveSig] int __pad_UnregisterNotification(IntPtr a);

    // IAudioSessionControl2 extended (vtable 12-16).
    [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
    [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

    /// <summary>Returns the PID of the process that owns this audio session.</summary>
    [PreserveSig] int GetProcessId(out uint pRetVal);

    [PreserveSig] int IsSystemSoundsSession();
    [PreserveSig] int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
}

/// <summary>
/// Controls the access mode when opening a Shell property store via
/// <see cref="IMMDevice.OpenPropertyStore"/>.
/// </summary>
internal enum StorageAccessMode : uint
{
    /// <summary>Read-only access. Sufficient for querying friendly names.</summary>
    Read = 0,

    /// <summary>Write access. Not required for the audio routing pipeline.</summary>
    Write = 1,

    /// <summary>Read-write access.</summary>
    ReadWrite = 2,
}
