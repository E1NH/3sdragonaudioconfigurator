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
            // 'winget list' exits 0 and emits the package row when a match is found.
            // Exit code != 0 means not found or an error occurred.
            // --accept-source-agreements suppresses the interactive EULA prompt
            // that can appear on first winget use on a machine.
            var (exitCode, output, _) = await RunProcessAsync(
                "winget",
                $"list --id {SpotifyWingetId} --accept-source-agreements",
                cancellationToken);

            // A double-check: winget may return 0 but emit an empty match list.
            bool isInstalled = exitCode == 0
                && output.Contains(SpotifyWingetId, StringComparison.OrdinalIgnoreCase);

            return OperationResult<bool>.Success(isInstalled);
        }
        catch (Exception ex)
        {
            return OperationResult<bool>.Failure(
                $"Failed to query winget for Spotify installation status: {ex.Message}", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<Unit>> EnsureSpotifyInstalledAsync(
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Step 1: Confirm winget is present before attempting anything.
            var wingetCheck = await IsWingetAvailableAsync(cancellationToken);
            if (!wingetCheck.IsSuccess || !wingetCheck.Value)
            {
                return OperationResult<Unit>.Failure(
                    "winget (Windows Package Manager) is not available on this machine. " +
                    "Install it from the Microsoft Store (search for 'App Installer') and retry.");
            }

            // Step 2: Skip if Spotify is already installed.
            var spotifyCheck = await IsSpotifyInstalledAsync(cancellationToken);
            if (spotifyCheck.IsSuccess && spotifyCheck.Value)
            {
                progress?.Report(new ProgressReport("Spotify is already installed.", 100));
                return OperationResult<Unit>.Success(Unit.Value);
            }

            // Step 3: Install Spotify silently.
            // -h        = hidden (suppresses the installer UI window)
            // --accept-source-agreements = suppresses EULA prompts
            // --accept-package-agreements = suppresses per-package licence prompts
            progress?.Report(ProgressReport.Indeterminate("Installing Spotify via winget..."));

            var (exitCode, _, errorOutput) = await RunProcessAsync(
                "winget",
                $"install {SpotifyWingetId} -h " +
                "--accept-source-agreements --accept-package-agreements",
                cancellationToken);

            if (exitCode != 0)
            {
                return OperationResult<Unit>.Failure(
                    $"winget failed to install Spotify (exit code {exitCode}). " +
                    $"Details: {errorOutput.Trim()}");
            }

            progress?.Report(new ProgressReport("Spotify installed successfully.", 100));
            return OperationResult<Unit>.Success(Unit.Value);
        }
        catch (OperationCanceledException)
        {
            throw; // Let cancellation propagate; it's not a failure.
        }
        catch (Exception ex)
        {
            return OperationResult<Unit>.Failure(
                $"An unexpected error occurred while installing Spotify: {ex.Message}", ex);
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

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
