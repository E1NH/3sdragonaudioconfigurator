using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using DragonOS.AudioConfigurator.Core.Interop;
using DragonOS.AudioConfigurator.Core.Models;

namespace DragonOS.AudioConfigurator.Core.Services;

/// <summary>
/// Default implementation of <see cref="IDriverInstallService"/>.
/// Downloads VB-Cable from the official VB-Audio CDN and runs the vendor installer.
/// </summary>
public sealed class DriverInstallService : IDriverInstallService, IDisposable
{
    // -------------------------------------------------------------------------
    // Constants
    // -------------------------------------------------------------------------

    /// <summary>
    /// The official VB-Audio download URL for the driver package.
    /// This targets the stable Pack43 release. Update this URL when a newer
    /// stable release is available and has been validated.
    /// </summary>
    private const string VbCableDownloadUrl =
        "https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack43.zip";

    /// <summary>
    /// The name of the 64-bit installer executable within the ZIP archive.
    /// VB-Audio ships both x86 and x64 installers; we always target x64.
    /// </summary>
    private const string InstallerExecutableName = "VBCABLE_Setup_x64.exe";

    /// <summary>
    /// The substring used to identify the VB-Cable input device among all
    /// active audio endpoints. This string is stable across VB-Cable versions.
    /// </summary>
    private const string CableInputFriendlyNameFragment = "CABLE Input";

    // -------------------------------------------------------------------------
    // State
    // -------------------------------------------------------------------------

    private readonly HttpClient _httpClient;
    private bool _disposed;

    // -------------------------------------------------------------------------
    // Constructor
    // -------------------------------------------------------------------------

    /// <summary>
    /// Initialises a new <see cref="DriverInstallService"/>.
    /// </summary>
    /// <param name="httpClient">
    /// The <see cref="HttpClient"/> instance to use for downloading the driver.
    /// Inject a shared instance (e.g. from <c>IHttpClientFactory</c>) rather than
    /// constructing one per call to avoid socket exhaustion.
    /// </param>
    public DriverInstallService(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    // -------------------------------------------------------------------------
    // IDriverInstallService
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public Task<OperationResult<bool>> IsVbCableInstalledAsync(
        CancellationToken cancellationToken = default)
    {
        // COM enumeration is a synchronous, fast call — no need to push to a pool thread.
        try
        {
            bool found = TryFindCableInputDevice(out _);
            return Task.FromResult(OperationResult<bool>.Success(found));
        }
        catch (COMException comEx)
        {
            return Task.FromResult(OperationResult<bool>.Failure(
                $"COM error while checking audio devices: 0x{(uint)comEx.ErrorCode:X8}", comEx));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult<bool>.Failure(
                $"Unexpected error while checking for VB-Cable: {ex.Message}", ex));
        }
    }

    /// <inheritdoc/>
    public async Task<OperationResult<Unit>> EnsureVbCableInstalledAsync(
        IProgress<ProgressReport>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            // --- Step 1: Check if already installed -----------------------------------
            var installed = await IsVbCableInstalledAsync(cancellationToken);
            if (installed.IsSuccess && installed.Value)
            {
                progress?.Report(new ProgressReport("VB-Cable is already installed.", 100));
                return OperationResult<Unit>.Success(Unit.Value);
            }

            // --- Step 2: Download the driver ZIP ------------------------------------
            progress?.Report(new ProgressReport("Downloading VB-Cable driver package...", 0));

            var downloadPath = Path.Combine(Path.GetTempPath(), "VBCABLE_Driver_Pack43.zip");
            var downloadResult = await DownloadWithProgressAsync(
                VbCableDownloadUrl, downloadPath, progress, cancellationToken);

            if (!downloadResult.IsSuccess)
                return OperationResult<Unit>.Failure(downloadResult.ErrorMessage!, downloadResult.Exception);

            // --- Step 3: Extract the ZIP -------------------------------------------
            progress?.Report(new ProgressReport("Extracting driver package...", 80));

            var extractDir = Path.Combine(Path.GetTempPath(), "VBCABLE_Driver_Pack43");
            if (Directory.Exists(extractDir))
                Directory.Delete(extractDir, recursive: true);

            ZipFile.ExtractToDirectory(downloadPath, extractDir);

            // --- Step 4: Strip Zone.Identifier ADS ------------------------------------
            // Windows tags files downloaded from the internet with a Zone.Identifier
            // Alternate Data Stream. This causes SmartScreen to intercept execution.
            // Removing the stream allows the signed vendor installer to run unimpeded.
            var setupPath = Path.Combine(extractDir, InstallerExecutableName);
            if (!File.Exists(setupPath))
            {
                return OperationResult<Unit>.Failure(
                    $"Installer executable '{InstallerExecutableName}' was not found in the " +
                    "downloaded archive. The VB-Audio package structure may have changed.");
            }

            StripZoneIdentifier(setupPath);

            // --- Step 5: Execute the vendor installer silently ----------------------
            // VB-Cable's NSIS-based installer accepts /S for silent installation.
            // Note: The installer requires administrator privileges (already granted
            // via the application manifest) and will install the kernel audio driver.
            // A reboot is typically required before the device appears in the audio graph.
            progress?.Report(ProgressReport.Indeterminate("Installing VB-Cable driver (may take 30 seconds)..."));

            var installResult = await RunInstallerAsync(setupPath, cancellationToken);
            if (!installResult.IsSuccess)
                return installResult;

            progress?.Report(new ProgressReport(
                "VB-Cable installed. A system reboot is required to activate the driver.", 100));

            return OperationResult<Unit>.Success(Unit.Value);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return OperationResult<Unit>.Failure(
                $"An unexpected error occurred during VB-Cable installation: {ex.Message}", ex);
        }
    }

    // -------------------------------------------------------------------------
    // IDisposable
    // -------------------------------------------------------------------------

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _httpClient.Dispose();
        _disposed = true;
    }

    // -------------------------------------------------------------------------
    // Private methods
    // -------------------------------------------------------------------------

    /// <summary>
    /// Downloads a file from <paramref name="url"/> to <paramref name="destinationPath"/>,
    /// reporting download progress as a percentage.
    /// </summary>
    private async Task<OperationResult<Unit>> DownloadWithProgressAsync(
        string url,
        string destinationPath,
        IProgress<ProgressReport>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(
                url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            using var networkStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var fileStream    = File.Create(destinationPath);

            var buffer     = new byte[81_920]; // 80 KB buffer — tuned for VB-Cable's ~5 MB download.
            long bytesRead = 0;
            int  read;

            while ((read = await networkStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                bytesRead += read;

                if (totalBytes > 0)
                {
                    double pct = Math.Round((double)bytesRead / totalBytes * 75, 1); // Reserve 25% for install.
                    progress?.Report(new ProgressReport(
                        $"Downloading VB-Cable driver ({bytesRead / 1024} KB / {totalBytes / 1024} KB)...", pct));
                }
            }

            return OperationResult<Unit>.Success(Unit.Value);
        }
        catch (HttpRequestException httpEx)
        {
            return OperationResult<Unit>.Failure(
                $"Failed to download VB-Cable from '{url}': {httpEx.Message} " +
                "Check your internet connection and verify the download URL is still valid.", httpEx);
        }
    }

    /// <summary>
    /// Removes the Zone.Identifier Alternate Data Stream from a file so that
    /// Windows SmartScreen does not block its execution.
    /// </summary>
    /// <remarks>
    /// ADS names use the pattern <c>filename:Zone.Identifier</c>. File.Exists and
    /// File.Delete handle ADS paths correctly on NTFS volumes. On non-NTFS file
    /// systems (e.g. FAT32 in %TEMP%), this is a no-op.
    /// </remarks>
    private static void StripZoneIdentifier(string filePath)
    {
        var adsPath = $"{filePath}:Zone.Identifier";
        try
        {
            if (File.Exists(adsPath))
                File.Delete(adsPath);
        }
        catch (UnauthorizedAccessException)
        {
            // Non-fatal: the ADS removal is a best-effort courtesy. If it fails,
            // the installer may still run if the file is code-signed by VB-Audio.
        }
    }

    /// <summary>
    /// Executes the VB-Cable installer silently and waits for it to complete.
    /// </summary>
    private static async Task<OperationResult<Unit>> RunInstallerAsync(
        string setupPath,
        CancellationToken cancellationToken)
    {
        try
        {
            using var installer = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName        = setupPath,
                    Arguments       = "/S",          // NSIS silent install flag.
                    UseShellExecute = true,          // Required for UAC elevation propagation.
                    Verb            = "runas",       // Explicitly request elevation (belt + suspenders).
                    CreateNoWindow  = false,         // NSIS /S should suppress the window; this is fallback.
                },
                EnableRaisingEvents = true,
            };

            installer.Start();
            await installer.WaitForExitAsync(cancellationToken);

            // VB-Cable installer exit codes:
            // 0 = success, 1 = user cancelled (rare in silent mode), anything else = error.
            if (installer.ExitCode != 0)
            {
                return OperationResult<Unit>.Failure(
                    $"VB-Cable installer exited with code {installer.ExitCode}. " +
                    "Try running the installer manually from %TEMP%\\VBCABLE_Driver_Pack43\\ " +
                    "to see if there is a driver signing or compatibility error.");
            }

            return OperationResult<Unit>.Success(Unit.Value);
        }
        catch (Exception ex)
        {
            return OperationResult<Unit>.Failure(
                $"Failed to execute the VB-Cable installer: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Enumerates active render endpoints and returns <see langword="true"/> if
    /// the "CABLE Input" virtual device is found.
    /// </summary>
    /// <param name="deviceId">
    /// When the method returns <see langword="true"/>, contains the Windows endpoint ID
    /// of the CABLE Input device. Otherwise <see langword="null"/>.
    /// </param>
    private static bool TryFindCableInputDevice(out string? deviceId)
    {
        deviceId = null;

        var enumeratorObj = new MMDeviceEnumeratorComObject();
        var enumerator    = (IMMDeviceEnumerator)enumeratorObj;

        int hr = enumerator.EnumAudioEndpoints(
            EDataFlow.eRender, DeviceState.Active, out var collection);

        NativeInterop.ThrowIfFailed(hr, "IMMDeviceEnumerator.EnumAudioEndpoints");

        hr = collection.GetCount(out uint count);
        NativeInterop.ThrowIfFailed(hr, "IMMDeviceCollection.GetCount");

        for (uint i = 0; i < count; i++)
        {
            hr = collection.Item(i, out var device);
            if (!NativeInterop.Succeeded(hr)) continue;

            if (!TryGetFriendlyName(device, out var name)) continue;
            if (!name!.Contains(CableInputFriendlyNameFragment, StringComparison.OrdinalIgnoreCase)) continue;

            device.GetId(out deviceId);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Reads the <c>PKEY_Device_FriendlyName</c> property from an <see cref="IMMDevice"/>.
    /// </summary>
    private static bool TryGetFriendlyName(IMMDevice device, out string? friendlyName)
    {
        friendlyName = null;

        int hr = device.OpenPropertyStore(StorageAccessMode.Read, out var props);
        if (!NativeInterop.Succeeded(hr)) return false;

        var key = PROPERTYKEY.DeviceFriendlyName;
        hr = props.GetValue(ref key, out var pv);
        if (!NativeInterop.Succeeded(hr)) return false;

        friendlyName = pv.ToStringValue();
        return friendlyName is not null;
    }
}
