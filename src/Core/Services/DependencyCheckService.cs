using System.Diagnostics;
using DragonOS.AudioConfigurator.Core.Models;

namespace DragonOS.AudioConfigurator.Core.Services;

/// <summary>
/// Default implementation of <see cref="IDependencyCheckService"/>.
/// Uses <c>winget</c> (Windows Package Manager) for application management.
/// </summary>
public sealed class DependencyCheckService : IDependencyCheckService
{
    // The stable winget package ID for the MSIX-distributed Spotify client.
    private const string SpotifyWingetId = "Spotify.Spotify";

    // -------------------------------------------------------------------------
    // Public interface implementation
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> IsWingetAvailableAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 'winget --version' is the least destructive probe; it prints the version
            // string and exits 0 on success. On machines where winget is absent, the
            // process launch itself will throw FileNotFoundException.
            var (exitCode, _, _) = await RunProcessAsync(
                "winget", "--version", cancellationToken);

            return OperationResult<bool>.Success(exitCode == 0);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
                                        or FileNotFoundException)
        {
            return OperationResult<bool>.Success(false); // winget absent — not an error per se.
        }
        catch (Exception ex)
        {
            return OperationResult<bool>.Failure(
                $"Unexpected error while probing winget availability: {ex.Message}", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<bool>> IsSpotifyInstalledAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Method 1: file-system paths — covers direct-download and winget installs.
            // Reliable under UAC elevation: the elevated process inherits the original
            // user's environment block so LOCALAPPDATA / APPDATA are correct.
            if (IsSpotifyInstalledByPath())
                return OperationResult<bool>.Success(true);

            // Method 2: registry — covers installs that write the standard uninstall key.
            if (IsSpotifyInstalledByRegistry())
                return OperationResult<bool>.Success(true);

            // Method 3: running process — if Spotify is open, it is obviously installed
            // and we can route to it immediately without touching the filesystem.
            if (IsSpotifyProcessRunning())
                return OperationResult<bool>.Success(true);

            // Method 4: winget as last resort. Running as admin, winget may not see
            // user-scoped packages, so this is intentionally the slowest path.
            var (exitCode, output, _) = await RunProcessAsync(
                "winget",
                $"list --id {SpotifyWingetId} --accept-source-agreements",
                cancellationToken);

            bool isInstalled = exitCode == 0
                && output.Contains(SpotifyWingetId, StringComparison.OrdinalIgnoreCase);

            return OperationResult<bool>.Success(isInstalled);
        }
        catch (Exception ex)
        {
            return OperationResult<bool>.Failure(
                $"Failed to query Spotify installation status: {ex.Message}", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<Unit>> EnsureSpotifyInstalledAsync(
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // This step is a presence check only — the Dragon OS Audio Configurator is a
        // routing tool for users who already have Spotify. Installing Spotify
        // automatically is out of scope; if it is absent the user is directed to
        // install it from the official source before re-running.
        try
        {
            progress?.Report(ProgressReport.Indeterminate("Checking for Spotify..."));

            var check = await IsSpotifyInstalledAsync(cancellationToken);

            if (!check.IsSuccess)
                return OperationResult<Unit>.Failure(check.ErrorMessage!);

            if (!check.Value)
                return OperationResult<Unit>.Failure(
                    "Spotify was not found on this system. " +
                    "Please install Spotify from https://www.spotify.com/download, " +
                    "then re-run the configurator.");

            progress?.Report(new ProgressReport("Spotify is installed and ready.", 100));
            return OperationResult<Unit>.Success(Unit.Value);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return OperationResult<Unit>.Failure(
                $"Unexpected error while checking for Spotify: {ex.Message}", ex);
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns <see langword="true"/> if Spotify's executable is found at either
    /// of its two known user-profile installation paths.
    /// <list type="bullet">
    ///   <item><description><c>%LOCALAPPDATA%\Spotify\Spotify.exe</c> — current default.</description></item>
    ///   <item><description><c>%APPDATA%\Spotify\Spotify.exe</c> — used by older installers.</description></item>
    /// </list>
    /// </summary>
    private static bool IsSpotifyInstalledByPath()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appData      = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        return File.Exists(Path.Combine(localAppData, "Spotify", "Spotify.exe"))
            || File.Exists(Path.Combine(appData,      "Spotify", "Spotify.exe"));
    }

    /// <summary>
    /// Returns <see langword="true"/> if Spotify has written its standard uninstall
    /// registry key under <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\Spotify</c>.
    /// This key is present after both direct-download and winget installs.
    /// </summary>
    private static bool IsSpotifyInstalledByRegistry()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Spotify");
        return key is not null;
    }

    /// <summary>
    /// Returns <see langword="true"/> if at least one <c>Spotify.exe</c> process is
    /// currently running. If Spotify is open, it is installed — no path lookup required.
    /// </summary>
    private static bool IsSpotifyProcessRunning()
        => Process.GetProcessesByName("Spotify").Length > 0;

    /// <summary>
    /// Launches a process and waits for it to exit, capturing stdout and stderr.
    /// </summary>
    /// <param name="fileName">The executable name or full path.</param>
    /// <param name="arguments">Command-line arguments string.</param>
    /// <param name="cancellationToken">
    /// Token that kills the process if cancelled. Note that the underlying process
    /// is not guaranteed to honour the kill immediately on all Windows builds.
    /// </param>
    /// <returns>
    /// A tuple of (exit code, stdout, stderr).
    /// </returns>
    private static async Task<(int ExitCode, string Output, string Error)> RunProcessAsync(
        string fileName,
        string arguments,
        CancellationToken cancellationToken = default)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName               = fileName,
                Arguments              = arguments,
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
            },
            EnableRaisingEvents = true,
        };

        process.Start();

        // Read stdout and stderr concurrently to avoid deadlocks on processes that
        // write large amounts to one stream while the other buffer fills.
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask  = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        return (process.ExitCode,
                await outputTask,
                await errorTask);
    }
}
