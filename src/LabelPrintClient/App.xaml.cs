using System.Windows;

namespace LabelPrintClient;

public partial class App : Application
{
    private TrayApp? _trayApp;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _trayApp = new TrayApp();

        if (!_trayApp.RunFirstTimeSetup())
        {
            _trayApp.Dispose();
            Shutdown();
            return;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayApp?.Dispose();
        base.OnExit(e);
    }
}
