using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace BookShopPortalUI;

public partial class MainWindow : Window
{
    private Process? _agentProcess;
    private readonly string _agentExe;
    private NotifyIconWrapper? _tray;

    public MainWindow()
    {
        InitializeComponent();
        _agentExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BookShopPrintAgent.exe");
        var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "book.ico");
        if (File.Exists(iconPath))
            Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri(iconPath));
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Activate();
        try
        {
            await Initialize();
        }
        catch (Exception ex)
        {
            SetStatus("Failed", $"Error: {ex.Message}", true);
        }
    }

    private async Task Initialize()
    {
        SetStatus("Checking...", "Looking for running agent...", false);

        if (await PingAgent())
        {
            SetReady("Agent is already running and responding.");
            return;
        }

        if (IsPortInUse(8080))
        {
            SetStatus("Failed", "Port 8080 is in use by another program.\nClose the other application and restart.", true);
            return;
        }

        if (!File.Exists(_agentExe))
        {
            SetStatus("Failed", $"Agent not found:\n{_agentExe}\nPlease reinstall.", true);
            return;
        }

        SetStatus("Starting...", "Launching print agent...", false);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _agentExe,
                WorkingDirectory = Path.GetDirectoryName(_agentExe),
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
                UseShellExecute = true
            };

            _agentProcess = new Process { StartInfo = psi };
            _agentProcess.Start();

            SetStatus("Waiting...", "Waiting for agent to respond (up to 30s)...", false);

            for (int i = 0; i < 60; i++)
            {
                await Task.Delay(500);

                if (_agentProcess.HasExited)
                {
                    SetStatus("Failed", $"Agent exited unexpectedly (code: {_agentProcess.ExitCode}).\nCheck that all files are present.", true);
                    return;
                }

                if (await PingAgent())
                {
                    SetReady("Agent started and responding.");
                    return;
                }
            }

            SetStatus("Failed", "Agent is not responding on http://127.0.0.1:8080.\nRestart your computer and try again.", true);
        }
        catch (Exception ex)
        {
            SetStatus("Failed", $"Could not start agent:\n{ex.Message}", true);
        }
    }

    private static bool IsPortInUse(int port)
    {
        try
        {
            var properties = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties();
            return properties.GetActiveTcpListeners().Any(ep => ep.Port == port);
        }
        catch { return false; }
    }

    private async Task<bool> PingAgent()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var response = await client.GetAsync("http://127.0.0.1:8080/api/print-job/health");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private void SetReady(string detail)
    {
        statusMessage.Text = "✓ All working good and ready to print";
        statusMessage.Foreground = new SolidColorBrush(Color.FromRgb(46, 204, 113));
        detailMessage.Text = detail;
        Background = new SolidColorBrush(Color.FromRgb(39, 174, 96));
        hintMessage.Visibility = Visibility.Visible;
        _tray = new NotifyIconWrapper(this, "DR Bahig Books Portal - Ready");
        _tray.Show();
    }

    private void SetStatus(string status, string detail, bool isError)
    {
        statusMessage.Text = isError ? $"✗ {status}" : status;
        statusMessage.Foreground = new SolidColorBrush(
            isError ? Color.FromRgb(231, 76, 60) : Color.FromRgb(243, 156, 18));
        detailMessage.Text = detail;
        if (isError)
        {
            Background = new SolidColorBrush(Color.FromRgb(192, 57, 43));
            _tray = new NotifyIconWrapper(this, "DR Bahig Books Portal - Failed");
            _tray.Show();
            hintMessage.Visibility = Visibility.Visible;
        }
    }

    protected override void OnStateChanged(EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            Hide();
            _tray?.Show();
        }
        base.OnStateChanged(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _tray?.Dispose();
        _agentProcess?.Dispose();
        base.OnClosed(e);
        System.Windows.Application.Current.Shutdown();
    }
}

internal class NotifyIconWrapper : IDisposable
{
    private readonly Window _window;
    private readonly System.Windows.Forms.NotifyIcon _icon;

    public NotifyIconWrapper(Window window, string text)
    {
        _window = window;

        var iconHandle = System.Drawing.Icon.ExtractAssociatedIcon(
            System.Reflection.Assembly.GetExecutingAssembly().Location);

        _icon = new System.Windows.Forms.NotifyIcon
        {
            Icon = iconHandle ?? System.Drawing.SystemIcons.Application,
            Text = text,
            Visible = false
        };

        _icon.DoubleClick += (s, e) => Restore();

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Show Window", null, (s, e) => Restore());
        menu.Items.Add("Exit", null, (s, e) =>
        {
            _window.Close();
        });
        _icon.ContextMenuStrip = menu;
    }

    public void Show() => _icon.Visible = true;
    public void Hide() => _icon.Visible = false;

    private void Restore()
    {
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
        Hide();
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
