using DragonOS.AudioConfigurator.Core.Models;

namespace DragonOS.AudioConfigurator.Core.Services;

/// <summary>
/// Defines the contract for checking and satisfying external software dependencies
/// required before the audio routing pipeline can proceed.
/// </summary>
/// <remarks>
/// Current responsibilities:
/// <list type="bullet">
///   <item>Verify that <c>winget</c> (Windows Package Manager) is available on the host.</item>
///   <item>Check whether Spotify is installed and, if not, install it silently.</item>
/// </list>
/// Implementations must be safe to call from a background thread.
/// </remarks>
public interface IDependencyCheckService
{
    /// <summary>
    /// Determines whether <c>winget</c> is available on this machine.
    /// </summary>
    /// <remarks>
    /// <c>winget</c> ships with Windows 10 1809 and later via the App Installer package.
    /// Older machines or stripped LTSC/Enterprise builds may lack it.
    /// </remarks>
    /// <returns>
    /// A result containing <see langword="true"/> if <c>winget</c> is present;
    /// <see langword="false"/> (with an error message) if it cannot be found.
    /// </returns>
    Task<OperationResult<bool>> IsWingetAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether Spotify is currently installed on the system.
    /// </summary>
    /// <returns>
    /// A result containing <see langword="true"/> if Spotify is installed,
    /// <see langword="false"/> if it is absent.
    /// </returns>
    Task<OperationResult<bool>> IsSpotifyInstalledAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Installs Spotify silently using <c>winget</c> if it is not already present.
    /// </summary>
    /// <param name="progress">
    /// Optional progress sink. Reports download/install status updates.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the installation.</param>
    /// <returns>
    /// A result containing <see cref="Unit"/> on success, or a failure with a
    /// human-readable description of what went wrong.
    /// </returns>
    Task<OperationResult<Unit>> EnsureSpotifyInstalledAsync(
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default);
}
