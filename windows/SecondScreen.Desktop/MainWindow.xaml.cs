using System.Windows;
using System.Windows.Media;
using SecondScreen.Core;

namespace SecondScreen.Desktop;

public partial class MainWindow : Window
{
    private SessionManager? _session;
    private string _badgeKey = "badge.disconnected";

    public MainWindow()
    {
        InitializeComponent();
        Log.OnLog += (_, e) => Dispatcher.Invoke(() =>
            LogText.Text = $"[{e.level}] {e.message}");
        Loc.Changed += ApplyLanguage;
        ApplyLanguage();
    }

    // ---- Localization ---------------------------------------------------------------------
    private void ApplyLanguage()
    {
        LangButton.Content = Loc.ToggleLabel();
        TaglineText.Text = Loc.T("tagline");
        LblDevice.Text = Loc.T("lbl.device");
        LblConn.Text = Loc.T("lbl.connection");
        LblRes.Text = Loc.T("lbl.resolution");
        LblLatency.Text = Loc.T("lbl.latency");
        LblPerf.Text = Loc.T("lbl.performance");
        Qi0.Content = Loc.T("q.performance");
        Qi1.Content = Loc.T("q.balanced");
        Qi2.Content = Loc.T("q.high");
        VirtualDisplayCheck.Content = Loc.T("chk.vd");
        EncryptVideoCheck.Content = Loc.T("chk.enc");
        HwEncodeCheck.Content = Loc.T("chk.hw");
        DriverHintText.Text = Loc.T("driver.hint");
        StartButton.Content = Loc.T("btn.start");
        DisconnectButton.Content = Loc.T("btn.disconnect");
        DisplaySettingsButton.Content = Loc.T("btn.display");
        HealthButton.Content = Loc.T("btn.health");
        StatusBadge.Text = Loc.T(_badgeKey);
    }

    private void LangButton_Click(object sender, RoutedEventArgs e)
    {
        Loc.ToggleLang();
        AppSettings.Save();
    }

    private void HealthButton_Click(object sender, RoutedEventArgs e)
    {
        var w = new HealthWindow { Owner = this };
        w.ShowDialog();
    }

    private QualityMode SelectedQuality() => QualityCombo.SelectedIndex switch
    {
        0 => QualityMode.Performance,
        2 => QualityMode.HighQuality,
        _ => QualityMode.Balanced
    };

    // ---- Start / stop hosting (NO driver install here — that happens in the installer). -----
    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        var opts = new SessionOptions
        {
            HostName = Environment.MachineName,
            Quality = SelectedQuality(),
            UseVirtualDisplay = VirtualDisplayCheck.IsChecked == true,
            EncryptVideo = EncryptVideoCheck.IsChecked == true,
            UseHardwareEncoder = HwEncodeCheck.IsChecked == true
        };

        _session = new SessionManager(opts);
        _session.StateChanged += (_, s) => Dispatcher.Invoke(() => OnState(s));
        _session.PinReady += (_, pin) => Dispatcher.Invoke(() => ShowPin(pin));
        _session.Error += (_, msg) => Dispatcher.Invoke(() =>
            MessageBox.Show(this, msg, "HP ke Monitor", MessageBoxButton.OK, MessageBoxImage.Warning));
        _session.Diagnostics.Updated += (_, _) => Dispatcher.Invoke(UpdateDiagnostics);

        StartButton.IsEnabled = false;
        DisconnectButton.IsEnabled = true;
        SetBadge("badge.waiting", (Brush)FindResource("Warn"));

        try { await _session.StartAsync(); }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"{Loc.T("start.fail")}: {ex.Message}", "HP ke Monitor");
            ResetUi();
        }
    }

    private void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        _session?.Disconnect("user");
        _session?.Dispose();
        _session = null;
        ResetUi();
    }

    private void DisplaySettings_Click(object sender, RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:display") { UseShellExecute = true }); }
        catch { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("desk.cpl") { UseShellExecute = true }); }
    }

    private void OnState(SessionState s)
    {
        switch (s)
        {
            case SessionState.Discovering: SetBadge("badge.searching", (Brush)FindResource("Warn")); break;
            case SessionState.Connecting:
            case SessionState.Configuring: SetBadge("badge.connecting", (Brush)FindResource("Warn")); break;
            case SessionState.Pairing: SetBadge("badge.pairing", (Brush)FindResource("Warn")); break;
            case SessionState.Streaming:
                SetBadge("badge.connected", (Brush)FindResource("Accent"));
                PinCard.Visibility = Visibility.Collapsed;
                DeviceNameText.Text = _session?.PeerDevice.Name ?? "Android";
                break;
            case SessionState.Reconnecting: SetBadge("badge.reconnecting", (Brush)FindResource("Warn")); break;
            case SessionState.Disconnected:
            case SessionState.Idle: ResetUi(); break;
        }
    }

    private void ShowPin(string pin)
    {
        PinCard.Visibility = Visibility.Visible;
        PinText.Text = $"{pin[..3]}   {pin[3..]}";
        PinPromptText.Text = Loc.T("pin.prompt");
        DeviceNameText.Text = _session?.PeerDevice.Name ?? "Android";
        Activate();
        MessageBox.Show(this,
            $"{Loc.T("pin.prompt")}\n\n        {pin[..3]}  {pin[3..]}",
            Loc.T("pin.title"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void UpdateDiagnostics()
    {
        var d = _session!.Diagnostics;
        ConnText.Text = d.Connection;
        ResText.Text = d.Width > 0 ? $"{d.Width} × {d.Height}" : "—";
        FpsText.Text = d.Fps > 0 ? $"{d.Fps} FPS" : "—";
        LatencyText.Text = d.NetworkLatencyMs > 0 ? $"{d.NetworkLatencyMs:0} ms" : "—";
        BitrateText.Text = d.BitrateMbps > 0 ? $"{d.BitrateMbps:0.0} Mbps" : "—";
        CodecText.Text = d.Codec;
    }

    private void SetBadge(string key, Brush color)
    {
        _badgeKey = key;
        StatusBadge.Text = Loc.T(key);
        StatusBadge.Foreground = color;
    }

    private void ResetUi()
    {
        StartButton.IsEnabled = true;
        DisconnectButton.IsEnabled = false;
        PinCard.Visibility = Visibility.Collapsed;
        SetBadge("badge.disconnected", (Brush)FindResource("FgMuted"));
        DeviceNameText.Text = "—";
        ConnText.Text = ResText.Text = FpsText.Text = LatencyText.Text = BitrateText.Text = "—";
    }

    protected override void OnClosed(EventArgs e)
    {
        Loc.Changed -= ApplyLanguage;
        _session?.Dispose();
        base.OnClosed(e);
    }
}
