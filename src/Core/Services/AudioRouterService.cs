using System.Diagnostics;
using System.Runtime.InteropServices;
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

    // -------------------------------------------------------------------------
    // IAudioRouterService — GetAvailableRenderDevices
    // -------------------------------------------------------------------------

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

    // -------------------------------------------------------------------------
    // IAudioRouterService — RouteApplicationAsync
    // -------------------------------------------------------------------------

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

    // -------------------------------------------------------------------------
    // Core routing logic
    // -------------------------------------------------------------------------

    /// <summary>
    /// The inner synchronous implementation of the routing operation.
    /// Runs on a thread-pool thread via <c>Task.Run</c> in the public methods.
    /// </summary>
    private static OperationResult<int> ApplyRouting(
        string executableName,
        string targetDeviceId,
        IProgress<ProgressReport>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            // Normalise the process name — strip extension so Process.GetProcessesByName
            // works correctly (it expects the name without .exe).
            var processName = Path.GetFileNameWithoutExtension(executableName);

            progress?.Report(new ProgressReport($"Searching for '{processName}' processes...", 10));

            var processes = Process.GetProcessesByName(processName);

            if (processes.Length == 0)
            {
                return OperationResult<int>.Failure(
                    $"No running processes found with the name '{processName}'. " +
                    "Ensure the application is launched before running the audio configurator.");
            }

            // Instantiate the policy config factory — version-aware.
            //
            // The COM class {870af99c-171d-4f9e-af0d-e63df40c2bc9} (CPolicyConfigClient)
            // exists on both Win10 and Win11 but exposes a DIFFERENT IID on each:
            //   Win10: {2a59116d-6c4f-45e0-a74f-707e3fef9258}  (IAudioPolicyConfigFactory)
            //   Win11: {ab3d4648-e242-459f-b02f-541c70306324}  (IAudioPolicyConfigFactoryWin11)
            //
            // We try CoCreateInstance + QI for the Win11 IID first (preferred path),
            // falling back to RoGetActivationFactory if that QI returns E_NOINTERFACE.
            bool isWin11 = Environment.OSVersion.Version.Build >= 21390;

            Func<uint, EDataFlow, ERole, IntPtr, int> setEndpoint;

            if (isWin11)
            {
                var comObj = new AudioPolicyConfigFactoryComObject();
                IAudioPolicyConfigFactoryWin11? win11Factory = null;

                try
                {
                    // Preferred: same CLSID, QI for the Win11 IID.
                    win11Factory = (IAudioPolicyConfigFactoryWin11)comObj;
                }
                catch (InvalidCastException)
                {
                    // QI returned E_NOINTERFACE — fall back to WinRT activation.
                    win11Factory = CreateWin11AudioPolicyFactory();
                }

                setEndpoint = win11Factory.SetPersistedDefaultAudioEndpoint;
            }
            else
            {
                setEndpoint = ((IAudioPolicyConfigFactory)new AudioPolicyConfigFactoryComObject())
                                  .SetPersistedDefaultAudioEndpoint;
            }

            int routedCount = 0;
            var routeErrors = new List<string>(); // Accumulate per-PID errors for diagnostics.

            for (int i = 0; i < processes.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var proc = processes[i];
                using (proc) // Dispose Process handle after use to release the OS handle.
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
                string iface      = isWin11 ? "Win11" : "Win10";
                return OperationResult<int>.Failure(
                    $"Found {processes.Length} '{processName}' process(es) but none could be routed " +
                    $"(interface: {iface}).{firstError}");
            }

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
            // Create the HSTRING from the managed device ID string.
            // The length is in characters, not bytes.
            int hr = NativeInterop.WindowsCreateString(
                deviceId, (uint)deviceId.Length, out hstring);

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
                // PROCESS_NO_AUDIO = preference stored; session not yet open.
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
    /// Obtains the <see cref="IAudioPolicyConfigFactoryWin11"/> instance via
    /// <c>RoGetActivationFactory("Windows.Media.Internal.AudioPolicyConfig")</c>.
    /// </summary>
    /// <remarks>
    /// The raw COM pointer returned by <c>RoGetActivationFactory</c> is wrapped in a
    /// managed RCW via <see cref="Marshal.GetObjectForIUnknown"/>, then the native
    /// reference is released with <see cref="Marshal.Release"/>. The RCW keeps the
    /// object alive for the lifetime of the returned interface reference.
    /// </remarks>
    /// <exception cref="COMException">
    /// Thrown if <c>RoGetActivationFactory</c> returns a failure HRESULT — for example
    /// if the Windows build does not recognise the activatable class name or IID.
    /// </exception>
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

            // Wrap the raw pointer in a managed RCW and cast to the typed interface.
            // GetObjectForIUnknown AddRefs; we release the native ref below.
            return (IAudioPolicyConfigFactoryWin11)Marshal.GetObjectForIUnknown(factoryPtr);
        }
        finally
        {
            if (hstring != IntPtr.Zero)
                NativeInterop.WindowsDeleteString(hstring);

            // Release the native ref from RoGetActivationFactory — the RCW holds its own.
            if (factoryPtr != IntPtr.Zero)
                Marshal.Release(factoryPtr);
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Instantiates <see cref="IMMDeviceEnumerator"/> via COM activation.
    /// </summary>
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
