using DragonOS.AudioConfigurator.Core.Models;

namespace DragonOS.AudioConfigurator.Core.Services;

/// <summary>
/// Defines the contract for per-application audio endpoint routing using the
/// undocumented <c>IAudioPolicyConfigFactory</c> COM interface.
/// </summary>
/// <remarks>
/// <para>
/// This service implements the mechanism that backs Windows 10/11's
/// "App volume and device preferences" feature (<c>ms-settings:apps-volume</c>).
/// It does NOT change the system-wide default audio device.
/// </para>
/// <para>
/// All methods in this interface must be called from a thread that has initialised
/// the COM apartment. The service implementation handles this internally by
/// running COM calls on a dedicated STA thread or thread-pool thread with
/// <c>Task.Run</c>.
/// </para>
/// </remarks>
public interface IAudioRouterService
{
    /// <summary>
    /// Returns all currently active audio render (output) endpoints visible to
    /// the Windows Core Audio API.
    /// </summary>
    /// <returns>
    /// A result containing a read-only list of <see cref="AudioDevice"/> records.
    /// </returns>
    OperationResult<IReadOnlyList<AudioDevice>> GetAvailableRenderDevices();

    /// <summary>
    /// Routes all running processes matching <paramref name="executableName"/> to
    /// the audio endpoint identified by <paramref name="targetDeviceId"/>.
    /// </summary>
    /// <param name="executableName">
    /// The process image name, with or without the <c>.exe</c> extension
    /// (e.g. <c>"Spotify"</c> or <c>"Spotify.exe"</c>).
    /// For Electron/CEF-based applications like Spotify, all child processes sharing
    /// this name are routed to ensure full audio capture.
    /// </param>
    /// <param name="targetDeviceId">
    /// The Windows endpoint ID of the target audio device, as returned by
    /// <see cref="GetAvailableRenderDevices"/>. This value is obtained from
    /// <c>IMMDevice::GetId</c> and is opaque — do not construct it by hand.
    /// </param>
    /// <param name="progress">
    /// Optional progress sink for reporting routing sub-steps.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// A result containing the number of process IDs that were successfully routed.
    /// A value of zero means no running processes matched <paramref name="executableName"/>.
    /// </returns>
    Task<OperationResult<int>> RouteApplicationAsync(
        string executableName,
        string targetDeviceId,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Convenience overload that automatically locates the VB-Cable "CABLE Input"
    /// device and routes all processes matching <paramref name="executableName"/> to it.
    /// </summary>
    /// <param name="executableName">
    /// The target process image name (e.g. <c>"Spotify"</c>).
    /// </param>
    /// <param name="progress">Optional progress sink.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// A result containing the number of processes routed.
    /// Returns a failure if no "CABLE Input" device is found (driver not installed or
    /// reboot required).
    /// </returns>
    Task<OperationResult<int>> RouteApplicationToCableInputAsync(
        string executableName,
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default);
}
