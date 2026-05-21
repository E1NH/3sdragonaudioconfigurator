using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DragonOS.AudioConfigurator.WPF.ViewModels;

/// <summary>
/// Base class for all ViewModels. Provides a lean <see cref="INotifyPropertyChanged"/>
/// implementation without a dependency on external MVVM frameworks.
/// </summary>
/// <remarks>
/// The <see cref="SetField{T}"/> helper performs equality comparison before raising
/// <see cref="PropertyChanged"/>, preventing redundant binding re-evaluations and
/// potential infinite loops in two-way bindings.
/// </remarks>
public abstract class BaseViewModel : INotifyPropertyChanged
{
    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Raises <see cref="PropertyChanged"/> for the given property name.
    /// </summary>
    /// <param name="propertyName">
    /// The name of the property that changed.
    /// Populated automatically by the compiler via <see cref="CallerMemberNameAttribute"/>.
    /// </param>
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>
    /// Sets <paramref name="field"/> to <paramref name="value"/> and raises
    /// <see cref="PropertyChanged"/> if the value has changed.
    /// </summary>
    /// <typeparam name="T">The type of the backing field.</typeparam>
    /// <param name="field">Reference to the backing field.</param>
    /// <param name="value">The new value to assign.</param>
    /// <param name="propertyName">
    /// The property name. Automatically supplied by the compiler.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if <paramref name="field"/> was changed;
    /// <see langword="false"/> if the value was already equal.
    /// </returns>
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
