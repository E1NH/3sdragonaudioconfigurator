using DragonOS.AudioConfigurator.Core.Models;

namespace DragonOS.AudioConfigurator.Core.Services;

/// <summary>
/// Defines the contract for acquiring and installing the VB-Audio Virtual Cable (VB-Cable) driver.
/// </summary>
/// <remarks>
/// VB-Cable creates a virtual audio endpoint pair ("CABLE Input" / "CABLE Output") that acts
/// as a software loopback device. Routing an application's audio output to "CABLE Input" allows
/// a second application (e.g. OBS) to capture it via "CABLE Output".
///
/// The installation process requires:
/// <list type="number">
///   <item>Administrator privileges (enforced via the application manifest).</item>
///   <item>Downloading the official driver ZIP from vb-audio.com.</item>
///   <item>Extracting to a temporary directory.</item>
///   <item>Executing the vendor installer silently.</item>
///   <item>A system reboot for the driver to appear in the audio device list.</item>
/// </list>
/// </remarks>
public interface IDriverInstallService
{
    /// <summary>
    /// Checks whether the VB-Cable "CABLE Input" audio device is currently active
    /// and visible to the Windows Core Audio API.
    /// </summary>
    /// <returns>
    /// A result containing <see langword="true"/> if VB-Cable is installed and active;
    /// <see langword="false"/> if absent (not installed or driver not yet loaded after reboot).
    /// </returns>
    Task<OperationResult<bool>> IsVbCableInstalledAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads, extracts, and silently installs the VB-Cable driver if it is not already present.
    /// </summary>
    /// <param name="progress">
    /// Optional progress sink. Reports download percentage and install phase transitions.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the download. Installation itself cannot be cancelled mid-flight.</param>
    /// <returns>
    /// A result containing <see cref="Unit"/> on success. On failure, the error message
    /// describes the specific step that failed (download, extraction, or installation).
    /// </returns>
    /// <remarks>
    /// If this method succeeds but <see cref="IsVbCableInstalledAsync"/> subsequently returns
    /// <see langword="false"/>, a system reboot is required before the driver will appear.
    /// The caller is responsible for prompting the user accordingly.
    /// </remarks>
    Task<OperationResult<Unit>> EnsureVbCableInstalledAsync(
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default);
}
