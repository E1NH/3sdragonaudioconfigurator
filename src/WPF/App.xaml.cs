using System.Windows;
using DragonOS.AudioConfigurator.Core.Services;
using DragonOS.AudioConfigurator.WPF.ViewModels;
using DragonOS.AudioConfigurator.WPF.Views;

namespace DragonOS.AudioConfigurator.WPF;

/// <summary>
/// Application entry point and manual dependency-injection root.
/// </summary>
/// <remarks>
/// <para>
/// Services are wired up here without an IoC container, keeping the dependency
/// graph explicit and immediately auditable for community contributors.
/// </para>
/// <para>
/// <b>Administrator check:</b> Although the manifest enforces UAC elevation at
/// process launch, a runtime check is performed here as a defensive measure against
/// manifest stripping in certain deployment scenarios.
/// </para>
/// </remarks>
public partial class App : Application
{
    /// <summary>
    /// Called by WPF when the application starts. Wires up services and opens the main window.
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Defensive elevation check — the manifest should handle this, but belt + suspenders.
        if (!IsRunningAsAdministrator())
        {
            MessageBox.Show(
                "Dragon OS Audio Configurator requires administrator privileges.\n\n" +
                "Please right-click the executable and select 'Run as administrator'.",
                "Administrator Required",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            Shutdown(1);
            return;
        }

        // --- Compose the service graph -------------------------------------------
        // HttpClient is instantiated once and shared across the application lifetime.
        // For a single-window tool this is acceptable; in a larger app, use IHttpClientFactory.
        var httpClient = new System.Net.Http.HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10), // VB-Cable is ~5 MB; generous timeout for slow connections.
        };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "DragonOS-AudioConfigurator/1.0 (github.com/your-org/DragonOS.AudioConfigurator)");

        IDependencyCheckService dependencyCheck = new DependencyCheckService();
        IDriverInstallService   driverInstall   = new DriverInstallService(httpClient);
        IAudioRouterService     audioRouter     = new AudioRouterService();

        var viewModel = new MainViewModel(dependencyCheck, driverInstall, audioRouter);

        // --- Open the main window -------------------------------------------------
        var mainWindow = new MainWindow { DataContext = viewModel };
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    /// <summary>
    /// Checks whether the current process is running with administrator privileges.
    /// </summary>
    private static bool IsRunningAsAdministrator()
    {
        using var identity  = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }
}
