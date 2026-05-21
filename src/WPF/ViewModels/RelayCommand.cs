using System.Windows.Input;

namespace DragonOS.AudioConfigurator.WPF.ViewModels;

/// <summary>
/// A minimal <see cref="ICommand"/> implementation that delegates execution
/// and can-execute logic to caller-supplied delegates.
/// </summary>
/// <remarks>
/// This implementation does not depend on any external MVVM library, keeping the
/// WPF project's dependency graph minimal for community auditability.
/// </remarks>
public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Func<object?, bool>? _canExecute;

    /// <summary>
    /// Initialises a new <see cref="RelayCommand"/>.
    /// </summary>
    /// <param name="execute">The action to perform when the command is invoked.</param>
    /// <param name="canExecute">
    /// Optional predicate that controls whether the command is available.
    /// If <see langword="null"/>, the command is always available.
    /// </param>
    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
    {
        _execute    = execute    ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    /// <summary>
    /// Convenience constructor for commands that take no parameter.
    /// </summary>
    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute())
    { }

    /// <inheritdoc/>
    public event EventHandler? CanExecuteChanged
    {
        add    => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    /// <summary>
    /// Triggers WPF's command manager to re-evaluate <see cref="CanExecute"/>
    /// on all registered commands. Call this after changing properties that affect
    /// whether a command should be enabled or disabled.
    /// </summary>
    public static void RaiseCanExecuteChanged()
        => CommandManager.InvalidateRequerySuggested();

    /// <inheritdoc/>
    public bool CanExecute(object? parameter)
        => _canExecute?.Invoke(parameter) ?? true;

    /// <inheritdoc/>
    public void Execute(object? parameter)
        => _execute(parameter);
}
