using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DragonOS.AudioConfigurator.WPF.Views;

/// <summary>
/// Code-behind for <c>MainWindow.xaml</c>.
/// Intentionally thin — all logic lives in <c>MainViewModel</c>.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>Initialises the window.</summary>
    public MainWindow()
    {
        InitializeComponent();
    }
}

// ============================================================================
// Value converters — declared public so XAML can instantiate them directly
// via the local: namespace alias. Kept in this file to co-locate the view's
// supporting types without proliferating tiny converter files.
// ============================================================================

/// <summary>
/// Returns <see cref="Visibility.Collapsed"/> when the bound value is
/// <see langword="null"/> or an empty string; <see cref="Visibility.Visible"/> otherwise.
/// Used to show the Detail text only when it has been populated by the ViewModel.
/// </summary>
public sealed class NullToCollapsedConverter : IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    /// <inheritdoc/>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Returns <see cref="Visibility.Visible"/> when the bound value is
/// <see langword="null"/> or empty; <see cref="Visibility.Collapsed"/> otherwise.
/// Used to show the Description fallback text before a Detail message has been set.
/// </summary>
public sealed class NullToVisibleConverter : IValueConverter
{
    /// <inheritdoc/>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;

    /// <inheritdoc/>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
