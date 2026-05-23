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
    /// silently vanishing. Log files are written to the exe's working directory.
    /// </summary>
    public App()
    {
        // Background thread / AppDomain-level crashes (including host-level .NET failures).
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            File.WriteAllText("dragon_fatal_domain.log",
                $"[{DateTime.Now:u}] AppDomain unhandled exception:\n{args.ExceptionObject}");
        };

        // UI dispatcher thread crashes (BAML parse failures, binding errors that throw, etc.)
        // NOTE: We use Environment.Exit instead of Shutdown here deliberately.
        // Calling Application.Shutdown from within DispatcherUnhandledException while
        // the layout engine is mid-flight re-triggers the layout pass, which re-throws
        // the same exception, which re-enters this handler — an infinite recursion that
        // exhausts the USER32 message stack (STATUS_STACK_OVERFLOW, c00000fd).
        // Environment.Exit bypasses the dispatcher entirely and terminates immediately.
        DispatcherUnhandledException += (sender, args) =>
        {
            try
            {
                File.WriteAllText("dragon_fatal_ui.log",
                    $"[{DateTime.Now:u}] Dispatcher unhandled exception:\n{args.Exception}");
            }
            catch
            {
                // If we can't write the log, swallow silently — we're already dying.
            }

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
            File.WriteAllText("dragon_startup.log",
                $"[{DateTime.Now:u}] OnStartup reached. IsAdmin={IsRunningAsAdministrator()}");

            if (!IsRunningAsAdministrator())
            {
                // Self-elevate: relaunch the current executable with the runas verb.
                // This triggers the UAC prompt. By the time the elevated process starts,
                // the single-file bundle has already been extracted by this first run,
                // so Defender does not flag the elevated process as a dropper.
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName        = Environment.ProcessPath ?? Environment.GetCommandLineArgs()[0],
                    UseShellExecute = true,
                    Verb            = "runas",
                });

                Shutdown(0);
                return;
            }

            var httpClient = new System.Net.Http.HttpClient
            {
                Timeout = TimeSpan.FromMinutes(10),
            };
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "DragonOS-AudioConfigurator/1.0 (github.com/E1NH/3sdragonaudioconfigurator)");

            IDependencyCheckService dependencyCheck = new DependencyCheckService();
            IDriverInstallService   driverInstall   = new DriverInstallService(httpClient);
            IAudioRouterService     audioRouter     = new AudioRouterService();

            var viewModel  = new MainViewModel(dependencyCheck, driverInstall, audioRouter);
            var mainWindow = new MainWindow { DataContext = viewModel };

            MainWindow = mainWindow;
            mainWindow.Show();

            File.AppendAllText("dragon_startup.log",
                $"\n[{DateTime.Now:u}] Window shown successfully.");
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

    private static bool IsRunningAsAdministrator()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }
}
