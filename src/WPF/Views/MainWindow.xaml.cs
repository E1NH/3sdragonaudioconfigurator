using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Navigation;

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

    // ── Custom chrome handlers ─────────────────────────────────────────────

    /// <summary>
    /// Allows the entire title bar to act as a drag handle.
    /// Called on <c>MouseLeftButtonDown</c> of the title bar Border.
    /// Button clicks inside the bar handle <c>MouseLeftButtonDown</c> themselves
    /// and mark it handled, so they never reach this method.
    /// </summary>
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => DragMove();

    /// <summary>Closes the application.</summary>
    private void BtnClose_Click(object sender, RoutedEventArgs e)
        => Application.Current.Shutdown();

    /// <summary>Collapses the window to the taskbar.</summary>
    private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    // ── Branding ───────────────────────────────────────────────────────────

    /// <summary>
    /// Opens the 3sdragon.eu website in the system default browser when the
    /// footer hyperlink is clicked.
    /// </summary>
    private void BrandingLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri)
            {
                UseShellExecute = true,
            });
        }
        catch { /* Non-fatal — browser may not be available. */ }

        e.Handled = true;
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
