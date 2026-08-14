using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Media;
using SecondScreen.Core;

namespace SecondScreen.Desktop;

public partial class HealthWindow : Window
{
    public HealthWindow()
    {
        InitializeComponent();
        ApplyLanguage();
        Loaded += async (_, _) => await RefreshAsync();
    }

    private void ApplyLanguage()
    {
        Title = Loc.T("h.title");
        TitleText.Text = Loc.T("h.title");
        SubtitleText.Text = Loc.T("h.subtitle");
        DriverTitle.Text = Loc.T("h.driver");
        TsTitle.Text = Loc.T("h.testsigning");
        NetTitle.Text = Loc.T("h.network");
        DriverFixBtn.Content = Loc.T("h.fix");
        TsEnableBtn.Content = Loc.T("h.enable");
        NetBtn.Content = Loc.T("h.opennet");
        UninstallLabel.Text = Loc.T("h.uninstall");
        UninstallBtn.Content = Loc.T("h.uninstall");
        RefreshBtn.Content = Loc.T("h.refresh");
        CloseBtn.Content = Loc.T("h.close");
    }

    private void Log(string msg) => Dispatcher.Invoke(() => HealthLog.Text = msg);

    private async Task RefreshAsync()
    {
        DriverStatus.Text = TsStatus.Text = NetStatus.Text = Loc.T("h.checking");
        DriverStatus.Foreground = TsStatus.Foreground = NetStatus.Foreground = (Brush)FindResource("FgMuted");

        var (driver, ts) = await Task.Run(() =>
            (DriverInstaller.GetInstalledVersion(), DriverInstaller.IsTestSigningOn()));

        // Driver
        if (driver != null)
            SetStatus(DriverStatus, $"{Loc.T("h.installed")} — v{driver} ✓", "Accent");
        else
            SetStatus(DriverStatus, Loc.T("h.notinstalled"), "Warn");

        // Test signing
        if (ts == true) SetStatus(TsStatus, Loc.T("h.on"), "Accent");
        else if (ts == false) SetStatus(TsStatus, Loc.T("h.off"), "Warn");
        else SetStatus(TsStatus, Loc.T("h.unknown"), "FgMuted");

        // Network
        var ips = GetLocalIPv4();
        SetStatus(NetStatus, string.IsNullOrEmpty(ips) ? "—" : ips,
            string.IsNullOrEmpty(ips) ? "Warn" : "Accent");
    }

    private void SetStatus(System.Windows.Controls.TextBlock tb, string text, string brushKey)
    {
        tb.Text = text;
        tb.Foreground = (Brush)FindResource(brushKey);
    }

    private static string GetLocalIPv4()
    {
        try
        {
            var addrs = Dns.GetHostAddresses(Dns.GetHostName());
            var list = new List<string>();
            foreach (var a in addrs)
                if (a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
                    list.Add(a.ToString());
            return string.Join(", ", list);
        }
        catch { return ""; }
    }

    // ---- actions --------------------------------------------------------------------------
    private async void DriverFix_Click(object sender, RoutedEventArgs e)
    {
        DriverFixBtn.IsEnabled = false;
        var r = await DriverInstaller.EnsureInstalledAsync(Log);
        if (r.RebootRequired) Log(Loc.T("h.reboot"));
        await RefreshAsync();
        DriverFixBtn.IsEnabled = true;
    }

    private async void TsEnable_Click(object sender, RoutedEventArgs e)
    {
        TsEnableBtn.IsEnabled = false;
        var r = await DriverInstaller.EnableTestSigningAsync(Log);
        if (r.RebootRequired) Log(Loc.T("h.reboot"));
        await RefreshAsync();
        TsEnableBtn.IsEnabled = true;
    }

    private void NetOpen_Click(object sender, RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:network") { UseShellExecute = true }); }
        catch { /* ignore */ }
    }

    private async void Uninstall_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(this, Loc.T("h.uninstall.confirm"), Loc.T("h.uninstall"),
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        UninstallBtn.IsEnabled = false;
        try { using var vd = new VirtualDisplayController(); vd.Remove(); } catch { /* ignore */ }
        var r = await DriverInstaller.UninstallAsync(Log);
        if (r.RebootRequired) Log(Loc.T("h.reboot"));
        await RefreshAsync();
        UninstallBtn.IsEnabled = true;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
