namespace DragonOS.AudioConfigurator.WPF.ViewModels;

/// <summary>
/// Represents the execution state of a single pipeline step in the UI.
/// </summary>
public enum StepStatus
{
    /// <summary>The step has not yet started.</summary>
    Pending,

    /// <summary>The step is currently executing.</summary>
    InProgress,

    /// <summary>The step completed successfully.</summary>
    Succeeded,

    /// <summary>The step failed. See <see cref="StepViewModel.Detail"/> for the reason.</summary>
    Failed,

    /// <summary>The step was skipped (e.g. dependency already satisfied).</summary>
    Skipped,
}

/// <summary>
/// ViewModel for a single step row in the Dragon OS Audio Configurator pipeline UI.
/// Bound to the <c>ItemsControl</c> in <c>MainWindow.xaml</c>.
/// </summary>
public sealed class StepViewModel : BaseViewModel
{
    // -------------------------------------------------------------------------
    // Immutable identity
    // -------------------------------------------------------------------------

    /// <summary>The display name of this step (e.g. "Install VB-Cable Driver").</summary>
    public string Name { get; }

    /// <summary>A brief description shown as subtext when the step is pending.</summary>
    public string Description { get; }

    // -------------------------------------------------------------------------
    // Observable state
    // -------------------------------------------------------------------------

    private StepStatus _status = StepStatus.Pending;

    /// <summary>
    /// The current execution status of this step.
    /// Changing this value raises <see cref="System.ComponentModel.INotifyPropertyChanged.PropertyChanged"/>
    /// and triggers DataTriggers in the XAML DataTemplate to update the icon and colour.
    /// </summary>
    public StepStatus Status
    {
        get => _status;
        set
        {
            if (SetField(ref _status, value))
            {
                // Notify derived display properties so XAML converters don't need separate triggers.
                OnPropertyChanged(nameof(IsInProgress));
                OnPropertyChanged(nameof(StatusIcon));
            }
        }
    }

    private string? _detail;

    /// <summary>
    /// A dynamic status string — updated while the step executes to show
    /// granular progress messages (e.g. "Downloading 3.2 MB / 5.1 MB...").
    /// On failure, contains the human-readable error description.
    /// </summary>
    public string? Detail
    {
        get => _detail;
        set => SetField(ref _detail, value);
    }

    private double _progressPercent = 0;

    /// <summary>
    /// Download/install progress in the range [0, 100].
    /// Negative values map to <see cref="IsProgressIndeterminate"/> = <see langword="true"/>.
    /// </summary>
    public double ProgressPercent
    {
        get => _progressPercent;
        set
        {
            if (SetField(ref _progressPercent, value))
                OnPropertyChanged(nameof(IsProgressIndeterminate));
        }
    }

    // -------------------------------------------------------------------------
    // Derived display helpers (no backing fields — derived from Status / Progress)
    // -------------------------------------------------------------------------

    /// <summary>
    /// <see langword="true"/> while this step's <see cref="Status"/> is <see cref="StepStatus.InProgress"/>.
    /// Drives the visibility of the progress bar in the DataTemplate.
    /// </summary>
    public bool IsInProgress => Status == StepStatus.InProgress;

    /// <summary>
    /// <see langword="true"/> when <see cref="ProgressPercent"/> is negative,
    /// indicating the amount of work is unknown.
    /// </summary>
    public bool IsProgressIndeterminate => ProgressPercent < 0;

    /// <summary>
    /// The Unicode character used as the step status icon in the UI.
    /// </summary>
    public string StatusIcon => Status switch
    {
        StepStatus.Pending    => "○",
        StepStatus.InProgress => "◌",
        StepStatus.Succeeded  => "✓",
        StepStatus.Failed     => "✗",
        StepStatus.Skipped    => "⊘",
        _                     => "?",
    };

    // -------------------------------------------------------------------------
    // Constructor
    // -------------------------------------------------------------------------

    /// <summary>
    /// Initialises a new <see cref="StepViewModel"/> with a name and description.
    /// </summary>
    public StepViewModel(string name, string description)
    {
        Name        = name        ?? throw new ArgumentNullException(nameof(name));
        Description = description ?? throw new ArgumentNullException(nameof(description));
    }

    /// <summary>
    /// Convenience method to transition this step to the <see cref="StepStatus.InProgress"/> state
    /// and set an initial detail message.
    /// </summary>
    public void Begin(string? initialDetail = null)
    {
        Detail          = initialDetail;
        ProgressPercent = -1; // Indeterminate until first progress callback.
        Status          = StepStatus.InProgress;
    }

    /// <summary>
    /// Transitions this step to <see cref="StepStatus.Succeeded"/>.
    /// </summary>
    public void Complete(string? completionDetail = null)
    {
        Detail          = completionDetail;
        ProgressPercent = 100;
        Status          = StepStatus.Succeeded;
    }

    /// <summary>
    /// Transitions this step to <see cref="StepStatus.Failed"/> with a reason.
    /// </summary>
    public void Fail(string errorMessage)
    {
        Detail = errorMessage;
        Status = StepStatus.Failed;
    }

    /// <summary>
    /// Marks the step as <see cref="StepStatus.Skipped"/> (e.g. dependency already satisfied).
    /// </summary>
    public void Skip(string reason)
    {
        Detail = reason;
        Status = StepStatus.Skipped;
    }
}
