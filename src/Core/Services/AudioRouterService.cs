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

            // Instantiate the policy config factory.
            // This is the COM class that backs Windows "App volume and device preferences".
            var factoryObj = new AudioPolicyConfigFactoryComObject();
            var factory    = (IAudioPolicyConfigFactory)factoryObj;

            int routedCount = 0;

            for (int i = 0; i < processes.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var proc = processes[i];
                using (proc) // Dispose Process handle after use to release the OS handle.
                {
                    double pct = 20 + ((double)i / processes.Length * 75);
                    progress?.Report(new ProgressReport(
                        $"Routing PID {proc.Id} ({processName}) → CABLE Input...", pct));

                    var routeResult = RouteProcess(factory, (uint)proc.Id, targetDeviceId);

                    if (routeResult.IsSuccess)
                    {
                        routedCount++;
                    }
                    else
                    {
                        // Non-fatal per-process failure: log and continue rather than aborting.
                        // Some Spotify child processes (e.g. crash-handler) may legitimately
                        // reject the routing call.
                        System.Diagnostics.Debug.WriteLine(
                            $"[AudioRouter] Skipped PID {proc.Id}: {routeResult.ErrorMessage}");
                    }
                }
            }

            if (routedCount == 0 && processes.Length > 0)
            {
                return OperationResult<int>.Failure(
                    $"Found {processes.Length} '{processName}' process(es) but failed to route any of them. " +
                    "This may indicate a Windows build incompatibility with IAudioPolicyConfigFactory. " +
                    "Consult the EarTrumpet project for the latest vtable definition.");
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
    /// Calls <c>IAudioPolicyConfigFactory.SetPersistedDefaultAudioEndpoint</c> for a
    /// single process ID.
    /// </summary>
    /// <remarks>
    /// HSTRING lifetime is managed with a try/finally to guarantee that
    /// <see cref="NativeInterop.WindowsDeleteString"/> is called even if the COM method
    /// throws, preventing a Windows Runtime string handle leak.
    /// </remarks>
    private static OperationResult<Unit> RouteProcess(
        IAudioPolicyConfigFactory factory,
        uint processId,
        string deviceId)
    {
        IntPtr hstring = IntPtr.Zero;

        try
        {
            // Create the HSTRING from the managed device ID string.
            // The length is in characters, not bytes.
            int hr = NativeInterop.WindowsCreateString(
                deviceId, (uint)deviceId.Length, out hstring);

            NativeInterop.ThrowIfFailed(hr, "WindowsCreateString");

            // This is the call that does the actual per-process audio routing.
            hr = factory.SetPersistedDefaultAudioEndpoint(
                processId,
                EDataFlow.eRender,
                ERole.eMultimedia,
                hstring);

            NativeInterop.ThrowIfFailed(
                hr, $"IAudioPolicyConfigFactory.SetPersistedDefaultAudioEndpoint (PID {processId})");

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
