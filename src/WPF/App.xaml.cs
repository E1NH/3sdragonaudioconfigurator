using System.IO;
using System.Windows;
using System.Windows.Threading;
using DragonOS.AudioConfigurator.Core.Services;
using DragonOS.AudioConfigurator.WPF.ViewModels;
using DragonOS.AudioConfigurator.WPF.Views;

namespace DragonOS.AudioConfigurator.WPF;

/// <summary>
/// Application entry point and manual dependency-injection root.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// App constructor — registers crash handlers BEFORE InitializeComponent and OnStartup
    /// so that BAML/XAML initialization failures are captured to disk rather than
    /// silently vanishing.
    /// </summary>
    public App()
    {
        // Background thread / AppDomain-level crashes.
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            File.WriteAllText("dragon_fatal_domain.log",
                $"[{DateTime.Now:u}] AppDomain unhandled exception:\n{args.ExceptionObject}");
        };

        // UI dispatcher thread crashes (BAML parse failures, binding errors that throw, etc.)
        // NOTE: We use Environment.Exit instead of Shutdown deliberately.
        // Calling Application.Shutdown from within DispatcherUnhandledException while
        // the layout engine is mid-flight re-triggers the layout pass, causing infinite
        // recursion that exhausts the USER32 message stack (STATUS_STACK_OVERFLOW).
        DispatcherUnhandledException += (sender, args) =>
        {
            try
            {
                File.WriteAllText("dragon_fatal_ui.log",
                    $"[{DateTime.Now:u}] Dispatcher unhandled exception:\n{args.Exception}");
            }
            catch { /* If we can't write the log, swallow — we're already dying. */ }

            args.Handled = true;
            Environment.Exit(1);
        };
    }

    /// <summary>
    /// Called by WPF after the constructor. Wires up services and opens the main window.
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var httpClient = new System.Net.Http.HttpClient
            {
                Timeout = TimeSpan.FromMinutes(10),
            };
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "DragonOS-AudioConfigurator/1.0 (3sdragon.eu)");

            IDependencyCheckService dependencyCheck = new DependencyCheckService();
            IDriverInstallService   driverInstall   = new DriverInstallService(httpClient);
            IAudioRouterService     audioRouter     = new AudioRouterService();

            var viewModel  = new MainViewModel(dependencyCheck, driverInstall, audioRouter);
            var mainWindow = new MainWindow { DataContext = viewModel };

            MainWindow = mainWindow;
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            File.WriteAllText("dragon_fatal_startup.log",
                $"[{DateTime.Now:u}] OnStartup exception:\n{ex}");

            MessageBox.Show(
                $"Fatal startup error:\n\n{ex.GetType().Name}: {ex.Message}\n\n{ex.StackTrace}",
                "Dragon OS — Startup Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(1);
        }
    }
}
