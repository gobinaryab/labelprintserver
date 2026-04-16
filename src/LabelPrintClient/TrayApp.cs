using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;
using LabelPrintClient.Services;

namespace LabelPrintClient;

public class TrayApp : IDisposable
{
    private readonly TaskbarIcon _trayIcon;
    private readonly AppSettings _settings;
    private readonly PrintServerClient _client;
    private readonly DispatcherTimer _healthTimer;
    private bool _serverOnline;
    private bool _p750wAvailable;
    private bool _p300btAvailable;
    private string _diagInfo = "";
    private PrintWindow? _printWindow;
    private MenuItem? _p750wItem;
    private MenuItem? _p300btItem;

    private static TrayApp? _instance;

    public TrayApp()
    {
        _instance = this;
        _settings = AppSettings.Load();
        _client = new PrintServerClient();

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "Label Print Client",
        };
        SetIcon("gray");

        _trayIcon.TrayMouseDoubleClick += (_, _) => OpenPrintDialog();
        _trayIcon.TrayLeftMouseDown += (_, _) => OpenPrintDialog();

        BuildContextMenu();

        _healthTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _healthTimer.Tick += async (_, _) => await PollHealth();

        if (_settings.IsConfigured)
        {
            _client.SetBaseUrl(_settings.ServerAddress);
            _healthTimer.Start();
            _ = PollHealth();
        }
    }

    public bool RunFirstTimeSetup()
    {
        if (_settings.IsConfigured)
            return true;

        return ShowSetupDialog();
    }

    private bool ShowSetupDialog()
    {
        var setup = new SetupWindow(_settings.ServerAddress);
        if (setup.ShowDialog() == true)
        {
            _settings.ServerAddress = setup.ServerAddress;
            _settings.Save();
            _client.SetBaseUrl(_settings.ServerAddress);

            if (!_healthTimer.IsEnabled)
                _healthTimer.Start();
            _ = PollHealth();

            BuildContextMenu();
            return true;
        }
        return false;
    }

    private void OpenPrintDialog()
    {
        if (_printWindow is { IsLoaded: true })
        {
            _printWindow.Activate();
            return;
        }

        if (!_settings.IsConfigured)
        {
            if (!ShowSetupDialog())
                return;
        }

        var displayName = _settings.DefaultPrinter == "p300bt" ? "PT-P300BT" : "PT-P750W";
        _printWindow = new PrintWindow(displayName);
        _printWindow.Closed += (_, _) =>
        {
            var text = _printWindow.PrintText;
            _printWindow = null;

            if (!string.IsNullOrEmpty(text))
                _ = SendPrintJobAsync(_settings.DefaultPrinter, text);
        };
        _printWindow.Show();
        _printWindow.Activate();
    }

    private async Task SendPrintJobAsync(string printer, string text)
    {
        var result = await _client.PrintAsync(printer, text);
        if (!result.Success)
            ShowError($"Print failed: {result.Error}");
    }

    private void BuildContextMenu()
    {
        var menu = new ContextMenu();

        var printerHeader = new MenuItem { Header = "Printer", IsEnabled = false };
        menu.Items.Add(printerHeader);

        var p750wStatus = _p750wAvailable ? "" : " [offline]";
        _p750wItem = new MenuItem
        {
            Header = $"PT-P750W (USB){p750wStatus}",
            IsCheckable = true,
            IsChecked = _settings.DefaultPrinter == "p750w",
            IsEnabled = _p750wAvailable
        };
        _p750wItem.Click += (_, _) => SetPrinter("p750w");
        menu.Items.Add(_p750wItem);

        var p300btStatus = _p300btAvailable ? "" : " [offline]";
        _p300btItem = new MenuItem
        {
            Header = $"PT-P300BT (Bluetooth){p300btStatus}",
            IsCheckable = true,
            IsChecked = _settings.DefaultPrinter == "p300bt",
            IsEnabled = _p300btAvailable
        };
        _p300btItem.Click += (_, _) => SetPrinter("p300bt");
        menu.Items.Add(_p300btItem);

        menu.Items.Add(new Separator());

        var serverItem = new MenuItem { Header = "Server Settings..." };
        serverItem.Click += (_, _) => ShowSetupDialog();
        menu.Items.Add(serverItem);

        var startupItem = new MenuItem
        {
            Header = "Run at Startup",
            IsCheckable = true,
            IsChecked = StartupManager.IsEnabled
        };
        startupItem.Click += (_, _) =>
        {
            var enabled = !StartupManager.IsEnabled;
            StartupManager.SetEnabled(enabled);
            startupItem.IsChecked = enabled;
            _settings.RunAtStartup = enabled;
            _settings.Save();
        };
        menu.Items.Add(startupItem);

        menu.Items.Add(new Separator());

        if (!string.IsNullOrEmpty(_diagInfo))
        {
            var diagItem = new MenuItem
            {
                Header = _diagInfo,
                IsEnabled = false
            };
            menu.Items.Add(diagItem);
            menu.Items.Add(new Separator());
        }

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var versionItem = new MenuItem
        {
            Header = $"Version {version?.ToString(3) ?? "1.0.0"}",
            IsEnabled = false
        };
        menu.Items.Add(versionItem);

        var exitItem = new MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) =>
        {
            Dispose();
            Application.Current.Shutdown();
        };
        menu.Items.Add(exitItem);

        _trayIcon.ContextMenu = menu;
    }

    private void SetPrinter(string printer)
    {
        _settings.DefaultPrinter = printer;
        _settings.Save();
        BuildContextMenu();
    }

    private async Task PollHealth()
    {
        var status = await _client.CheckDetailedHealthAsync();
        bool changed = false;

        if (status.ServerOnline != _serverOnline)
        {
            _serverOnline = status.ServerOnline;
            SetIcon(_serverOnline ? "green" : "red");
            changed = true;
        }

        if (status.P750wAvailable != _p750wAvailable || status.P300btAvailable != _p300btAvailable)
        {
            _p750wAvailable = status.P750wAvailable;
            _p300btAvailable = status.P300btAvailable;
            changed = true;
        }

        if (status.DiagInfo != _diagInfo)
        {
            _diagInfo = status.DiagInfo;
            changed = true;
        }

        if (changed)
        {
            _trayIcon.ToolTipText = _serverOnline
                ? $"Label Print Client — Online\nP750W: {(_p750wAvailable ? "ok" : "offline")} | P300BT: {(_p300btAvailable ? "ok" : "offline")}"
                : $"Label Print Client — Offline\n{_diagInfo}";
            BuildContextMenu();
        }
    }

    private void SetIcon(string color)
    {
        var uri = new Uri($"pack://application:,,,/Resources/icon-{color}.ico", UriKind.Absolute);
        using var stream = Application.GetResourceStream(uri)?.Stream;
        if (stream != null)
            _trayIcon.Icon = new System.Drawing.Icon(stream);
    }

    public static void ShowError(string message)
    {
        _instance?._trayIcon.ShowBalloonTip("Label Print Client", message, BalloonIcon.Error);
    }

    public void Dispose()
    {
        _healthTimer.Stop();
        _trayIcon.Dispose();
        _client.Dispose();
    }
}
