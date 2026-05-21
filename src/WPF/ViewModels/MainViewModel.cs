using System.Collections.ObjectModel;
using System.Windows.Input;
using DragonOS.AudioConfigurator.Core.Models;
using DragonOS.AudioConfigurator.Core.Services;

namespace DragonOS.AudioConfigurator.WPF.ViewModels;

/// <summary>
/// The root ViewModel for the Dragon OS Audio Configurator.
/// Orchestrates the four-step pipeline: dependency check, Spotify install,
/// VB-Cable install, and per-application audio routing.
/// </summary>
/// <remarks>
/// <para>
/// All UI-bound properties are updated on the WPF dispatcher thread via
/// <see cref="System.Windows.Application.Current"/>.Dispatcher.Invoke to ensure
/// thread safety when progress callbacks arrive from background tasks.
/// </para>
/// <para>
/// Service layer calls are fire-and-forget from the UI's perspective;
/// the pipeline executes sequentially and stops at the first failure,
/// updating the relevant <see cref="StepViewModel"/> accordingly.
/// </para>
/// </remarks>
public sealed class MainViewModel : BaseViewModel
{
    // -------------------------------------------------------------------------
    // Dependencies (injected via constructor)
    // -------------------------------------------------------------------------

    private readonly IDependencyCheckService _dependencyCheck;
    private readonly IDriverInstallService   _driverInstall;
    private readonly IAudioRouterService     _audioRouter;

    // -------------------------------------------------------------------------
    // Observable state
    // -------------------------------------------------------------------------

    private bool _isRunning;

    /// <summary>
    /// <see langword="true"/> while the pipeline is executing.
    /// Drives the <see cref="ConfigureCommand"/>'s CanExecute predicate
    /// and the "Configure Audio" button's enabled state.
    /// </summary>
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (SetField(ref _isRunning, value))
                RelayCommand.RaiseCanExecuteChanged();
        }
    }

    private bool _isComplete;

    /// <summary>
    /// <see langword="true"/> after the pipeline has finished (success or failure).
    /// Drives the visibility of the completion banner in the UI.
    /// </summary>
    public bool IsComplete
    {
        get => _isComplete;
        private set => SetField(ref _isComplete, value);
    }

    private bool _succeeded;

    /// <summary>
    /// <see langword="true"/> if the pipeline completed with all steps successful.
    /// </summary>
    public bool Succeeded
    {
        get => _succeeded;
        private set => SetField(ref _succeeded, value);
    }

    private string _statusMessage = "Ready. Click 'Configure Audio' to begin.";

    /// <summary>
    /// A global status line displayed beneath the step list.
    /// Updated at each pipeline transition.
    /// </summary>
    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    // -------------------------------------------------------------------------
    // Step list
    // -------------------------------------------------------------------------

    /// <summary>
    /// The ordered list of pipeline steps displayed in the UI.
    /// Each entry is a <see cref="StepViewModel"/> whose properties drive
    /// the icon, colour, and progress bar in the DataTemplate.
    /// </summary>
    public ObservableCollection<StepViewModel> Steps { get; }

    // Individual step references — held for direct state manipulation without
    // index-based array lookups (which would break if steps are reordered).
    private readonly StepViewModel _stepSpotify;
    private readonly StepViewModel _stepVbCable;
    private readonly StepViewModel _stepRoute;
    private readonly StepViewModel _stepVerify;

    // -------------------------------------------------------------------------
    // Commands
    // -------------------------------------------------------------------------

    /// <summary>
    /// Starts the audio configuration pipeline.
    /// Disabled while the pipeline is running or after it has completed.
    /// </summary>
    public ICommand ConfigureCommand { get; }

    // -------------------------------------------------------------------------
    // Constructor
    // -------------------------------------------------------------------------

    /// <summary>
    /// Initialises the MainViewModel with its required services.
    /// </summary>
    /// <param name="dependencyCheck">Service for checking/installing Spotify.</param>
    /// <param name="driverInstall">Service for downloading/installing VB-Cable.</param>
    /// <param name="audioRouter">Service for per-process audio endpoint routing.</param>
    public MainViewModel(
        IDependencyCheckService dependencyCheck,
        IDriverInstallService   driverInstall,
        IAudioRouterService     audioRouter)
    {
        _dependencyCheck = dependencyCheck ?? throw new ArgumentNullException(nameof(dependencyCheck));
        _driverInstall   = driverInstall   ?? throw new ArgumentNullException(nameof(driverInstall));
        _audioRouter     = audioRouter     ?? throw new ArgumentNullException(nameof(audioRouter));

        // Initialise the step list. Order matches the pipeline execution sequence.
        _stepSpotify = new StepViewModel(
            "Check Spotify",
            "Verifies Spotify is installed; installs it via winget if absent.");

        _stepVbCable = new StepViewModel(
            "Install VB-Cable Driver",
            "Downloads and silently installs the VB-Audio Virtual Cable driver.");

        _stepRoute = new StepViewModel(
            "Route Spotify → CABLE Input",
            "Binds Spotify's audio output to the VB-Cable virtual endpoint.");

        _stepVerify = new StepViewModel(
            "Verify Configuration",
            "Confirms CABLE Input is active and Spotify processes are routed.");

        Steps = new ObservableCollection<StepViewModel>
        {
            _stepSpotify,
            _stepVbCable,
            _stepRoute,
            _stepVerify,
        };

        ConfigureCommand = new RelayCommand(
            execute:    () => _ = RunPipelineAsync(),
            canExecute: () => !IsRunning && !IsComplete);
    }

    // -------------------------------------------------------------------------
    // Pipeline
    // -------------------------------------------------------------------------

    /// <summary>
    /// Executes the four-step configuration pipeline asynchronously.
    /// Each step is run in sequence; a failure in any step aborts subsequent steps.
    /// </summary>
    private async Task RunPipelineAsync()
    {
        IsRunning  = true;
        IsComplete = false;
        Succeeded  = false;
        SetStatusMessage("Starting Dragon OS audio configuration...");

        // Reset all steps to pending in case the user retries.
        foreach (var step in Steps)
        {
            step.Status          = StepStatus.Pending;
            step.Detail          = null;
            step.ProgressPercent = 0;
        }

        try
        {
            // ---- Step 1: Spotify ---------------------------------------------------
            SetStepBegin(_stepSpotify, "Checking Spotify installation...");

            var spotifyResult = await _dependencyCheck.EnsureSpotifyInstalledAsync(
                progress: MakeProgressSink(_stepSpotify),
                cancellationToken: default);

            if (!spotifyResult.IsSuccess)
            {
                SetStepFailed(_stepSpotify, spotifyResult.ErrorMessage!);
                return;
            }

            SetStepComplete(_stepSpotify, "Spotify is ready.");

            // ---- Step 2: VB-Cable -------------------------------------------------
            SetStepBegin(_stepVbCable, "Checking VB-Cable driver...");

            var vbCableResult = await _driverInstall.EnsureVbCableInstalledAsync(
                progress: MakeProgressSink(_stepVbCable),
                cancellationToken: default);

            if (!vbCableResult.IsSuccess)
            {
                SetStepFailed(_stepVbCable, vbCableResult.ErrorMessage!);
                return;
            }

            // Check whether the driver is actually visible yet — it won't be until reboot.
            var driverVisible = await _driverInstall.IsVbCableInstalledAsync();
            if (!driverVisible.IsSuccess || !driverVisible.Value)
            {
                SetStepComplete(_stepVbCable,
                    "VB-Cable installed. A system reboot is required before routing.");
                SetStepFailed(_stepRoute,
                    "CABLE Input device not yet visible — please reboot and re-run the configurator.");
                SetStatusMessage("Reboot required to complete audio driver activation.");
                return;
            }

            SetStepComplete(_stepVbCable, "VB-Cable driver is active.");

            // ---- Step 3: Route Spotify → CABLE Input ------------------------------
            SetStepBegin(_stepRoute, "Binding Spotify processes to CABLE Input...");

            var routeResult = await _audioRouter.RouteApplicationToCableInputAsync(
                executableName: "Spotify",
                progress:       MakeProgressSink(_stepRoute),
                cancellationToken: default);

            if (!routeResult.IsSuccess)
            {
                SetStepFailed(_stepRoute, routeResult.ErrorMessage!);
                return;
            }

            SetStepComplete(_stepRoute,
                $"Routed {routeResult.Value} Spotify process(es) to CABLE Input.");

            // ---- Step 4: Verify ---------------------------------------------------
            SetStepBegin(_stepVerify, "Verifying configuration...");

            var devices      = _audioRouter.GetAvailableRenderDevices();
            bool cableActive = devices.IsSuccess &&
                               devices.Value!.Any(d => d.FriendlyName.Contains(
                                   "CABLE Input", StringComparison.OrdinalIgnoreCase));

            if (!cableActive)
            {
                SetStepFailed(_stepVerify,
                    "CABLE Input is not in the active device list — driver may need a reboot.");
                return;
            }

            SetStepComplete(_stepVerify, "CABLE Input is active. Configuration complete.");

            // ---- Done -------------------------------------------------------------
            Succeeded = true;
            SetStatusMessage("Dragon OS audio matrix is fully configured. " +
                             "Spotify output is now routed to VB-Cable.");
        }
        finally
        {
            IsRunning  = false;
            IsComplete = true;
        }
    }

    // -------------------------------------------------------------------------
    // Thread-safe UI helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates an <see cref="IProgress{ProgressReport}"/> sink that marshals
    /// updates to the WPF dispatcher thread before updating the given step's
    /// <see cref="StepViewModel.Detail"/> and <see cref="StepViewModel.ProgressPercent"/>.
    /// </summary>
    private IProgress<ProgressReport> MakeProgressSink(StepViewModel step)
        => new Progress<ProgressReport>(report =>
        {
            Dispatch(() =>
            {
                step.Detail          = report.Message;
                step.ProgressPercent = report.IsIndeterminate ? -1 : report.PercentComplete;
            });
        });

    private void SetStepBegin(StepViewModel step, string detail)
        => Dispatch(() => step.Begin(detail));

    private void SetStepComplete(StepViewModel step, string detail)
        => Dispatch(() => step.Complete(detail));

    private void SetStepFailed(StepViewModel step, string error)
    {
        Dispatch(() =>
        {
            step.Fail(error);
            SetStatusMessage($"Configuration failed: {error}");
        });
    }

    private void SetStatusMessage(string message)
        => Dispatch(() => StatusMessage = message);

    /// <summary>
    /// Marshals an action to the WPF dispatcher thread.
    /// Safe to call from any thread, including COM callback threads.
    /// </summary>
    private static void Dispatch(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.Invoke(action);
    }
}
