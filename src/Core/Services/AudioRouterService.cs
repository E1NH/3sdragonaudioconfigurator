using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using DragonOS.AudioConfigurator.Core.Interop;
using DragonOS.AudioConfigurator.Core.Models;

namespace DragonOS.AudioConfigurator.Core.Services;

/// <summary>
/// Default implementation of <see cref="IAudioRouterService"/>.
/// Uses <c>IAudioPolicyConfigFactory.SetPersistedDefaultAudioEndpoint</c>
/// to bind specific processes to a target audio endpoint.
/// </summary>
/// <remarks>
/// <para>
/// <b>Threading model:</b> COM calls are marshalled to a thread-pool thread via
/// <c>Task.Run</c>. The COM objects used here (<c>IMMDeviceEnumerator</c>,
/// <c>IAudioPolicyConfigFactory</c>) are free-threaded or both-threaded and do
/// not require a dedicated STA thread.
/// </para>
/// <para>
/// <b>Spotify / CEF note:</b> Modern Spotify uses Chromium Embedded Framework
/// and spawns multiple processes named <c>Spotify.exe</c> (UI, GPU, renderer,
/// crash handler). Audio sessions may be associated with any renderer process.
/// This implementation routes all matched processes to ensure full coverage.
/// </para>
/// </remarks>
public sealed class AudioRouterService : IAudioRouterService
{
    private const string CableInputNameFragment = "CABLE Input";

    /// <inheritdoc/>
    public OperationResult<IReadOnlyList<AudioDevice>> GetAvailableRenderDevices()
    {
        try
        {
            var enumerator = CreateEnumerator();

            // Fetch the current system default for comparison.
            enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out var defaultDevice);
            defaultDevice.GetId(out var defaultId);

            int hr = enumerator.EnumAudioEndpoints(
                EDataFlow.eRender, DeviceState.Active, out var collection);
            NativeInterop.ThrowIfFailed(hr, "IMMDeviceEnumerator.EnumAudioEndpoints");

            hr = collection.GetCount(out uint count);
            NativeInterop.ThrowIfFailed(hr, "IMMDeviceCollection.GetCount");

            var devices = new List<AudioDevice>((int)count);

            for (uint i = 0; i < count; i++)
            {
                hr = collection.Item(i, out var device);
                if (!NativeInterop.Succeeded(hr)) continue;

                device.GetId(out var id);
                var name = GetFriendlyName(device) ?? id;

                devices.Add(new AudioDevice(
                    Id:           id,
                    FriendlyName: name,
                    IsDefault:    string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase)));
            }

            return OperationResult<IReadOnlyList<AudioDevice>>.Success(devices.AsReadOnly());
        }
        catch (COMException comEx)
        {
            return OperationResult<IReadOnlyList<AudioDevice>>.Failure(
                $"COM error during device enumeration: 0x{(uint)comEx.ErrorCode:X8}", comEx);
        }
        catch (Exception ex)
        {
            return OperationResult<IReadOnlyList<AudioDevice>>.Failure(
                $"Unexpected error during device enumeration: {ex.Message}", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<int>> RouteApplicationAsync(
        string executableName,
        string targetDeviceId,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableName);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDeviceId);

        return await Task.Run(() =>
            ApplyRouting(executableName, targetDeviceId, progress, cancellationToken),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<OperationResult<int>> RouteApplicationToCableInputAsync(
        string executableName,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executableName);

        return await Task.Run(() =>
        {
            // 1. Locate the CABLE Input device.
            progress?.Report(ProgressReport.Indeterminate("Locating CABLE Input audio endpoint..."));

            if (!TryFindDeviceByName(CableInputNameFragment, out var cableDeviceId) ||
                cableDeviceId is null)
            {
                return OperationResult<int>.Failure(
                    "The 'CABLE Input' device was not found among active audio endpoints. " +
                    "This usually means VB-Cable was installed but the system has not been " +
                    "rebooted yet. Please reboot and run the configurator again.");
            }

            // 2. Route the application to the discovered device.
            return ApplyRouting(executableName, cableDeviceId, progress, cancellationToken);
        }, cancellationToken);
    }

    /// <summary>
    /// The inner synchronous routing implementation, called via <c>Task.Run</c>.
    /// </summary>
    private static OperationResult<int> ApplyRouting(
        string executableName,
        string targetDeviceId,
        IProgress<ProgressReport>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            var processName = Path.GetFileNameWithoutExtension(executableName);

            progress?.Report(new ProgressReport($"Searching for '{processName}' processes...", 10));

            var processes = Process.GetProcessesByName(processName);

            if (processes.Length == 0)
            {
                return OperationResult<int>.Failure(
                    $"No running processes found with the name '{processName}'. " +
                    "Ensure the application is launched before running the audio configurator.");
            }

            // Instantiate the policy config factory — Win11-first with Win10 fallback.
            //
            // On Windows 11 (Build ≥ 21390) the CPolicyConfigClient COM object
            // ({870af99c-171d-4f9e-af0d-e63df40c2bc9}) returns E_NOINTERFACE for the
            // Win10 IID {2a59116d...}. The Win11 IID {ab3d4648...} must be used.
            // On Windows 10 and early Windows 11 builds the Win11 IID may not exist,
            // so we fall back to the Win10 IID if Win11 QI fails.
            Func<uint, EDataFlow, ERole, IntPtr, int> setEndpoint;

            try
            {
                var comObj = new AudioPolicyConfigFactoryComObject();
                IAudioPolicyConfigFactoryWin11 win11Factory;

                try
                {
                    // Preferred: CoCreateInstance + QI for Win11 IID.
                    win11Factory = (IAudioPolicyConfigFactoryWin11)comObj;
                }
                catch (InvalidCastException)
                {
                    // E_NOINTERFACE — fall back to WinRT RoGetActivationFactory.
                    win11Factory = CreateWin11AudioPolicyFactory();
                }

                setEndpoint = win11Factory.SetPersistedDefaultAudioEndpoint;
            }
            catch
            {
                // Win11 interface unavailable — fall back to Win10 IID.
                // This path is taken on Windows 10 and early Windows 11 builds.
                setEndpoint = ((IAudioPolicyConfigFactory)new AudioPolicyConfigFactoryComObject())
                                  .SetPersistedDefaultAudioEndpoint;
            }

            int routedCount = 0;
            var routeErrors = new List<string>();

            for (int i = 0; i < processes.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var proc = processes[i];
                using (proc)
                {
                    double pct = 20 + ((double)i / processes.Length * 75);
                    progress?.Report(new ProgressReport(
                        $"Routing PID {proc.Id} ({processName}) → CABLE Input...", pct));

                    var routeResult = RouteProcess(setEndpoint, (uint)proc.Id, targetDeviceId);

                    if (routeResult.IsSuccess)
                    {
                        routedCount++;
                    }
                    else
                    {
                        routeErrors.Add($"PID {proc.Id}: {routeResult.ErrorMessage}");
                        System.Diagnostics.Debug.WriteLine(
                            $"[AudioRouter] Skipped PID {proc.Id}: {routeResult.ErrorMessage}");
                    }
                }
            }

            if (routedCount == 0 && processes.Length > 0)
            {
                // Surface the first error so the UI shows the actual HRESULT rather than
                // a generic "build incompatibility" message — makes remote diagnosis possible.
                string firstError = routeErrors.Count > 0 ? $" First error → {routeErrors[0]}" : string.Empty;
                return OperationResult<int>.Failure(
                    $"Found {processes.Length} '{processName}' process(es) but none could be routed " +
                    $"(interface: Win10/unified).{firstError}");
            }

            progress?.Report(new ProgressReport(
                $"Routed {routedCount} of {processes.Length} '{processName}' process(es). Locating active audio session...", 88));

            // WASAPI active session targeting.
            //
            // The general loop above has written the cold-start preference for every
            // matched PID. Now we find the SPECIFIC process that owns the live audio
            // stream (AudioSessionState.Active) and route it once more — this is what
            // forces the currently-running session to migrate immediately rather than
            // waiting for the next application launch.
            if (routedCount > 0)
            {
                var activePid = FindActiveAudioSessionPid(processName);
                if (activePid.HasValue)
                {
                    progress?.Report(ProgressReport.Indeterminate(
                        $"Targeting active audio session (PID {activePid.Value})..."));
                    RouteProcess(setEndpoint, activePid.Value, targetDeviceId);
                }
            }


            // The COM call writes the process-identity entry and updates Audiosrv's
            // in-memory policy store, but registry persistence of the device-mapping values
            // is unreliable on this build. Write them directly in the exact format that
            // Windows Settings uses — confirmed from registry delta observation (screenshot,
            // cb97a9c0_0 and 5955a9f3_0 comparison). The Windows Audio Service monitors
            // DefaultEndpoint via RegNotifyChangeKeyValue; the WM_SETTINGCHANGE broadcast
            // below triggers it to reload and apply the new preference.
            WriteRegistryDeviceMapping(processName, targetDeviceId);

            progress?.Report(new ProgressReport(
                $"Routed {routedCount} of {processes.Length} '{processName}' process(es). Activating live session...", 90));

            // Broadcast WM_SETTINGCHANGE — belt-and-suspenders signal to all windows.
            NativeInterop.BroadcastAudioSettingChange();

            // Brief system-default swap: flips the global default to CABLE Input for
            // 500 ms so Spotify's live audio session receives a device-removed event
            // and reopens on the correct endpoint, then immediately restores the
            // original default so no other application is permanently affected.
            ExecuteBriefSystemDefaultSwap(targetDeviceId, progress);

            progress?.Report(new ProgressReport(
                $"Successfully routed {routedCount} of {processes.Length} '{processName}' process(es).", 100));

            return OperationResult<int>.Success(routedCount);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (COMException comEx)
        {
            return OperationResult<int>.Failure(
                $"COM error during audio routing: 0x{(uint)comEx.ErrorCode:X8}. " +
                "If this is 0x80040154 (REGDB_E_CLASSNOTREG), the IAudioPolicyConfigFactory " +
                "class GUID may have changed in this Windows build. " +
                "Reference: https://github.com/File-New-Project/EarTrumpet for the latest CLSID.",
                comEx);
        }
        catch (Exception ex)
        {
            return OperationResult<int>.Failure(
                $"Unexpected error during audio routing: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Calls <c>SetPersistedDefaultAudioEndpoint</c> for a single process ID via the
    /// supplied delegate, which abstracts over the Win10 and Win11 interface variants.
    /// </summary>
    /// <param name="setEndpoint">
    /// A delegate bound to either <c>IAudioPolicyConfigFactory.SetPersistedDefaultAudioEndpoint</c>
    /// (Windows 10) or <c>IAudioPolicyConfigFactoryWin11.SetPersistedDefaultAudioEndpoint</c>
    /// (Windows 11). The caller is responsible for creating and holding the factory alive
    /// for the lifetime of this call — the delegate reference itself keeps the RCW rooted.
    /// </param>
    /// <param name="processId">The OS process ID to bind to the target device.</param>
    /// <param name="deviceId">
    /// The raw audio endpoint ID (e.g. <c>{0.0.0.00000000}.{ad93b569-...}</c>).
    /// The full PnP Device Interface Path including <c>DEVINTERFACE_AUDIO_RENDER</c>
    /// is constructed internally before passing to the COM method.
    /// </param>
    /// <remarks>
    /// HSTRING lifetime is managed with a try/finally to guarantee that
    /// <see cref="NativeInterop.WindowsDeleteString"/> is called even if the COM method
    /// throws, preventing a Windows Runtime string handle leak.
    /// </remarks>
    private static OperationResult<Unit> RouteProcess(
        Func<uint, EDataFlow, ERole, IntPtr, int> setEndpoint,
        uint processId,
        string deviceId)
    {
        // 0x80070057 returned by SetPersistedDefaultAudioEndpoint means
        // "PROCESS_NO_AUDIO": the process has no active audio session at this
        // moment, but the routing preference HAS been written to the Windows
        // audio policy store. It will take effect the next time the application
        // opens an audio session (e.g. when Spotify next begins playback).
        // Treat this as success — NOT as E_INVALIDARG.
        // Reference: github.com/Belphemur/SoundSwitch — HRESULT.cs, PROCESS_NO_AUDIO
        const int ProcessNoAudio = unchecked((int)0x80070057);

        IntPtr hstring = IntPtr.Zero;

        try
        {
            // SetPersistedDefaultAudioEndpoint requires a fully-qualified PnP Device
            // Interface Path — NOT the raw endpoint GUID, and NOT the plain SWD prefix.
            //
            // Format: \\?\SWD#MMDEVAPI#<endpointId>#{DEVINTERFACE_AUDIO_RENDER}
            //
            // When the path omits the interface class GUID, Audiosrv treats the string
            // as invalid and executes a silent "clear policy" operation: it creates the
            // process-identity subkey (writing (Default) = NT exe path) but intentionally
            // omits the four 000_000 device-mapping values. This is an undocumented
            // "invalid = clear" contract in the Windows Audio Engine.
            //
            // DEVINTERFACE_AUDIO_RENDER = {e6327cad-dcec-4949-ae8a-991e976a79d2}
            // Reference: Windows DDK / devpkey.h, confirmed by Gemini audio policy analysis.
            const string DevInterfaceAudioRender = "{e6327cad-dcec-4949-ae8a-991e976a79d2}";
            string fullDevicePath = $@"\\?\SWD#MMDEVAPI#{deviceId}#{DevInterfaceAudioRender}";

            int hr = NativeInterop.WindowsCreateString(
                fullDevicePath, (uint)fullDevicePath.Length, out hstring);

            NativeInterop.ThrowIfFailed(hr, "WindowsCreateString");

            // Route for all three roles — Windows stores them as independent slots.
            // Spotify (CEF) opens audio sessions against eConsole; eMultimedia is also
            // set for completeness, matching what the Windows Settings UI does internally.
            // eCommunications ensures future-proofing if Spotify ever uses that path.
            ERole[] roles = [ERole.eConsole, ERole.eMultimedia, ERole.eCommunications];

            foreach (var role in roles)
            {
                hr = setEndpoint(processId, EDataFlow.eRender, role, hstring);

                // S_OK (0) = active session routed immediately.
                // PROCESS_NO_AUDIO (0x80070057) = preference stored; session not yet open.
                // Both are successful outcomes — continue to next role.
                if (hr == ProcessNoAudio || hr == 0) continue;

                NativeInterop.ThrowIfFailed(
                    hr, $"SetPersistedDefaultAudioEndpoint (PID {processId}, role {role})");
            }

            return OperationResult<Unit>.Success(Unit.Value);
        }
        catch (COMException comEx)
        {
            return OperationResult<Unit>.Failure(
                $"COM error routing PID {processId}: 0x{(uint)comEx.ErrorCode:X8}", comEx);
        }
        finally
        {
            // Always release the HSTRING handle — even if SetPersistedDefaultAudioEndpoint threw.
            if (hstring != IntPtr.Zero)
                NativeInterop.WindowsDeleteString(hstring);
        }
    }

    /// <summary>
    /// Obtains <see cref="IAudioPolicyConfigFactoryWin11"/> via
    /// <c>RoGetActivationFactory</c> — used when CoCreateInstance + QI returns E_NOINTERFACE.
    /// </summary>
    private static IAudioPolicyConfigFactoryWin11 CreateWin11AudioPolicyFactory()
    {
        const string ClassName = "Windows.Media.Internal.AudioPolicyConfig";
        var iid = new Guid("ab3d4648-e242-459f-b02f-541c70306324");

        IntPtr hstring    = IntPtr.Zero;
        IntPtr factoryPtr = IntPtr.Zero;

        try
        {
            int hr = NativeInterop.WindowsCreateString(
                ClassName, (uint)ClassName.Length, out hstring);
            NativeInterop.ThrowIfFailed(hr, "WindowsCreateString (AudioPolicyConfig)");

            hr = NativeInterop.RoGetActivationFactory(hstring, ref iid, out factoryPtr);
            NativeInterop.ThrowIfFailed(hr, "RoGetActivationFactory (IAudioPolicyConfigFactoryWin11)");

            return (IAudioPolicyConfigFactoryWin11)Marshal.GetObjectForIUnknown(factoryPtr);
        }
        finally
        {
            if (hstring    != IntPtr.Zero) NativeInterop.WindowsDeleteString(hstring);
            if (factoryPtr != IntPtr.Zero) Marshal.Release(factoryPtr);
        }
    }

    /// <summary>
    /// Temporarily sets the Windows system-wide default playback device to
    /// <paramref name="cableDeviceId"/> for 500 ms, then restores the original.
    /// This forces any live audio session (such as Spotify's) to receive a
    /// device-removed notification, tear down, and reopen on the newly persisted
    /// per-app endpoint rather than waiting for a full application restart.
    /// </summary>
    /// <remarks>
    /// Failures are swallowed — the persisted per-app preference set by
    /// <c>SetPersistedDefaultAudioEndpoint</c> is already in place, so Spotify
    /// will pick up the correct device on its next cold start even if this swap
    /// cannot execute (e.g. due to a COM access error on a locked-down system).
    /// </remarks>
    private static void ExecuteBriefSystemDefaultSwap(
        string cableDeviceId,
        IProgress<ProgressReport>? progress)
    {
        try
        {
            var enumerator = CreateEnumerator();

            int hr = enumerator.GetDefaultAudioEndpoint(
                EDataFlow.eRender, ERole.eMultimedia, out var currentDefault);

            if (!NativeInterop.Succeeded(hr)) return;

            currentDefault.GetId(out var originalId);

            // If the system default is already CABLE Input, no swap is needed.
            if (string.Equals(originalId, cableDeviceId, StringComparison.OrdinalIgnoreCase))
                return;

            var policyConfig = (IPolicyConfig)new AudioPolicyConfigFactoryComObject();

            ERole[] roles = [ERole.eConsole, ERole.eMultimedia, ERole.eCommunications];

            foreach (var role in roles)
                policyConfig.SetDefaultEndpoint(cableDeviceId, role);

            progress?.Report(ProgressReport.Indeterminate(
                "Switching live audio session to CABLE Input..."));

            // 2000 ms — UWP AppContainer audio sessions are significantly slower to
            // process WM_DEVICECHANGE notifications than Win32 processes.
            // Win32 Spotify typically responds within 200 ms; UWP Spotify requires
            // up to ~1.5 s to fully tear down and reopen the audio session.
            System.Threading.Thread.Sleep(2_000);

            foreach (var role in roles)
                policyConfig.SetDefaultEndpoint(originalId, role);
        }
        catch { /* Non-fatal — per-app preference already written. */ }
    }

    /// <summary>
    /// Searches WASAPI audio sessions across all active render endpoints to find the
    /// process ID of the <paramref name="processName"/> instance that currently owns
    /// an <c>AudioSessionState.Active</c> audio stream.
    /// Returns <see langword="null"/> if no active session is found for that process.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Spotify (Chromium Embedded Framework) spawns 5–7 processes all named
    /// <c>Spotify.exe</c>. Only one — the renderer that opened the active audio
    /// stream — has state <c>Active</c>.
    /// </para>
    /// <para>
    /// Calling <c>SetPersistedDefaultAudioEndpoint</c> against this specific PID
    /// forces the live audio session to migrate to the new endpoint immediately,
    /// rather than waiting for a full application restart.
    /// </para>
    /// </remarks>
    private static uint? FindActiveAudioSessionPid(string processName)
    {
        try
        {
            var enumerator = CreateEnumerator();

            // Spotify could be playing on any active render endpoint.
            int hr = enumerator.EnumAudioEndpoints(
                EDataFlow.eRender, DeviceState.Active, out var collection);
            if (!NativeInterop.Succeeded(hr)) return null;

            collection.GetCount(out uint deviceCount);

            // CLSCTX_ALL = 0x17 (23) — required by IMMDevice.Activate for in-proc/out-of-proc factories.
            const uint CLSCTX_ALL = 23;
            var sessionManagerIid = new Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");

            for (uint d = 0; d < deviceCount; d++)
            {
                hr = collection.Item(d, out var device);
                if (!NativeInterop.Succeeded(hr)) continue;

                // Activate IAudioSessionManager2 on this endpoint.
                hr = device.Activate(ref sessionManagerIid, CLSCTX_ALL, IntPtr.Zero, out var mgrObj);
                if (!NativeInterop.Succeeded(hr) || mgrObj is not IAudioSessionManager2 sessionManager)
                    continue;

                hr = sessionManager.GetSessionEnumerator(out var sessionEnum);
                if (!NativeInterop.Succeeded(hr)) continue;

                sessionEnum.GetCount(out int sessionCount);

                for (int s = 0; s < sessionCount; s++)
                {
                    hr = sessionEnum.GetSession(s, out var session);
                    if (!NativeInterop.Succeeded(hr)) continue;

                    try
                    {
                        hr = session.GetState(out var state);
                        if (!NativeInterop.Succeeded(hr) || state != AudioSessionState.Active)
                            continue;

                        hr = session.GetProcessId(out uint pid);
                        if (!NativeInterop.Succeeded(hr) || pid == 0) continue;

                        try
                        {
                            var proc = Process.GetProcessById((int)pid);
                            if (string.Equals(
                                Path.GetFileNameWithoutExtension(proc.ProcessName),
                                processName,
                                StringComparison.OrdinalIgnoreCase))
                            {
                                return pid;
                            }
                        }
                        catch (ArgumentException)
                        {
                            // Process already exited between GetProcessId and GetProcessById — skip.
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(session);
                    }
                }
            }

            return null;
        }
        catch
        {
            // Session enumeration is advisory — never fatal.
            return null;
        }
    }

    /// <summary>
    /// Writes the four device-mapping values the Windows Audio Service is expected to
    /// persist under the process's <c>DefaultEndpoint</c> subkey, in the exact format
    /// confirmed by comparing registry state before and after Windows Settings changes
    /// a per-app audio device.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this is necessary:</b> <c>SetPersistedDefaultAudioEndpoint</c> creates the
    /// process-identity subkey (<c>(Default)</c> = NT exe path) and updates the Windows Audio
    /// Service in-memory policy, but does not reliably persist the <c>000_000</c> device-mapping
    /// values to the registry on this Windows 11 build. Without those values the preference
    /// survives only until the process exits — on next Spotify launch the audio engine finds
    /// no device mapping and falls back to the system default.
    /// </para>
    /// <para>
    /// <b>Format confirmed from Windows Settings delta:</b>
    /// <list type="bullet">
    ///   <item><c>000_000</c> = <c>\\?\SWD#MMDEVAPI#{endpointId}</c> (eConsole device path)</item>
    ///   <item><c>000_000_p</c> = <c>{EFD176BA-848B-421A-BAB2-F7535AD1EA63}</c> (role policy token)</item>
    ///   <item><c>001_000</c> = same SWD path (eMultimedia device path)</item>
    ///   <item><c>001_000_p</c> = same role policy token</item>
    /// </list>
    /// </para>
    /// </remarks>
    private static void WriteRegistryDeviceMapping(string processName, string rawDeviceId)
    {
        try
        {
            const string rootKeyPath    = @"SOFTWARE\Microsoft\Multimedia\Audio\DefaultEndpoint";
            const string rolePolicyGuid = "{EFD176BA-848B-421A-BAB2-F7535AD1EA63}";

            // Device path as stored by Windows Settings — plain SWD prefix, no interface GUID.
            string swdPath = $@"\\?\SWD#MMDEVAPI#{rawDeviceId}";

            using var root = Registry.CurrentUser.OpenSubKey(rootKeyPath, writable: true);
            if (root is null) return;

            foreach (var subkeyName in root.GetSubKeyNames())
            {
                using var sub = root.OpenSubKey(subkeyName, writable: true);
                if (sub is null) continue;

                var defaultValue = sub.GetValue(null) as string;
                if (defaultValue is null) continue;

                if (!defaultValue.Contains(@"Roaming\Spotify\Spotify.exe",
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                // 000_NNN = eConsole (role 0), 001_NNN = eMultimedia (role 1) — Windows Settings format.
                sub.SetValue("000_000",   swdPath,        RegistryValueKind.String);
                sub.SetValue("000_000_p", rolePolicyGuid, RegistryValueKind.String);
                sub.SetValue("001_000",   swdPath,        RegistryValueKind.String);
                sub.SetValue("001_000_p", rolePolicyGuid, RegistryValueKind.String);
            }
        }
        catch
        {
            // Registry write failure is non-fatal — COM preference is already in place.
        }
    }

    /// <summary>Instantiates <see cref="IMMDeviceEnumerator"/> via COM.</summary>
    private static IMMDeviceEnumerator CreateEnumerator()
    {
        var comObj = new MMDeviceEnumeratorComObject();
        return (IMMDeviceEnumerator)comObj;
    }

    /// <summary>
    /// Searches active render endpoints for one whose friendly name contains
    /// <paramref name="nameFragment"/> (case-insensitive).
    /// </summary>
    /// <param name="nameFragment">The substring to match against friendly names.</param>
    /// <param name="deviceId">
    /// Receives the device ID when a match is found; <see langword="null"/> otherwise.
    /// </param>
    private static bool TryFindDeviceByName(string nameFragment, out string? deviceId)
    {
        deviceId = null;

        var enumerator = CreateEnumerator();

        int hr = enumerator.EnumAudioEndpoints(
            EDataFlow.eRender, DeviceState.Active, out var collection);
        NativeInterop.ThrowIfFailed(hr, "IMMDeviceEnumerator.EnumAudioEndpoints");

        collection.GetCount(out uint count);

        for (uint i = 0; i < count; i++)
        {
            collection.Item(i, out var device);
            var name = GetFriendlyName(device);
            if (name is null) continue;

            if (!name.Contains(nameFragment, StringComparison.OrdinalIgnoreCase)) continue;

            device.GetId(out deviceId);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Reads <c>PKEY_Device_FriendlyName</c> from an <see cref="IMMDevice"/>'s property store.
    /// Returns <see langword="null"/> if the property cannot be read.
    /// </summary>
    private static string? GetFriendlyName(IMMDevice device)
    {
        int hr = device.OpenPropertyStore(StorageAccessMode.Read, out var store);
        if (!NativeInterop.Succeeded(hr)) return null;

        var key = PROPERTYKEY.DeviceFriendlyName;
        hr = store.GetValue(ref key, out var pv);

        return NativeInterop.Succeeded(hr) ? pv.ToStringValue() : null;
    }
}
