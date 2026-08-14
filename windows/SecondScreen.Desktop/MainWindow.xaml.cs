using System.Windows;
using System.Windows.Media;
using SecondScreen.Core;

namespace SecondScreen.Desktop;

public partial class MainWindow : Window
{
    private SessionManager? _session;

    public MainWindow()
    {
        InitializeComponent();
        Log.OnLog += (_, e) => Dispatcher.Invoke(() =>
            LogText.Text = $"[{e.level}] {e.message}");
        Loaded += async (_, _) => await RefreshDriverStatusAsync();
    }

    private async Task RefreshDriverStatusAsync()
    {
        DriverStatusText.Text = "Driver Layar 2: memeriksa…";
        var installed = await Task.Run(() => DriverInstaller.GetInstalledVersion());
        Dispatcher.Invoke(() =>
        {
            DriverStatusText.Text = installed != null
                ? $"Driver Layar 2: terpasang (v{installed}) ✓"
                : "Driver Layar 2: belum terpasang — akan dipasang otomatis saat Mulai";
        });
    }

    private QualityMode SelectedQuality() => QualityCombo.SelectedIndex switch
    {
        0 => QualityMode.Performance,
        2 => QualityMode.HighQuality,
        _ => QualityMode.Balanced
    };

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        StartButton.IsEnabled = false;
        bool useVirtualDisplay = VirtualDisplayCheck.IsChecked == true;

        // --- Step 1: one-step driver setup (check → skip if present → auto-install). ---------
        if (useVirtualDisplay)
        {
            SetBadge("MENYIAPKAN", (Brush)FindResource("Warn"));
            var driver = await DriverInstaller.EnsureInstalledAsync(
                m => Dispatcher.Invoke(() => LogText.Text = m));
            await RefreshDriverStatusAsync();

            if (!driver.Success)
            {
                var choice = MessageBox.Show(this,
                    driver.Message +
                    "\n\nYA = aktifkan Test Signing sekarang (perlu 1x restart Windows)." +
                    "\nTIDAK = lanjut tanpa Layar 2 virtual (tangkap layar utama PC)." +
                    "\nBATAL = berhenti.",
                    "HP ke Monitor — Driver", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

                if (choice == MessageBoxResult.Cancel) { ResetUi(); return; }
                if (choice == MessageBoxResult.Yes)
                {
                    var ts = await DriverInstaller.EnableTestSigningAsync(
                        m => Dispatcher.Invoke(() => LogText.Text = m));
                    MessageBox.Show(this, ts.Message, "HP ke Monitor",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    ResetUi();
                    return; // user restarts, then presses Mulai again
                }
                useVirtualDisplay = false; // No -> fall back to primary capture
            }
            else if (driver.RebootRequired)
            {
                MessageBox.Show(this,
                    "Driver terpasang. Sebaiknya restart Windows agar Layar 2 aktif penuh, " +
                    "lalu buka lagi dan tekan Mulai.",
                    "HP ke Monitor", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // --- Step 2: start hosting with the chosen configuration. ----------------------------
        var opts = new SessionOptions
        {
            HostName = Environment.MachineName,
            Quality = SelectedQuality(),
            UseVirtualDisplay = useVirtualDisplay,
            EncryptVideo = EncryptVideoCheck.IsChecked == true,
            UseHardwareEncoder = HwEncodeCheck.IsChecked == true
        };

        _session = new SessionManager(opts);
        _session.StateChanged += (_, s) => Dispatcher.Invoke(() => OnState(s));
        _session.PinReady += (_, pin) => Dispatcher.Invoke(() => ShowPin(pin));
        _session.Error += (_, msg) => Dispatcher.Invoke(() =>
            MessageBox.Show(this, msg, "HP ke Monitor", MessageBoxButton.OK, MessageBoxImage.Warning));
        _session.Diagnostics.Updated += (_, _) => Dispatcher.Invoke(UpdateDiagnostics);

        DisconnectButton.IsEnabled = true;
        SetBadge("MENUNGGU", (Brush)FindResource("Warn"));

        try { await _session.StartAsync(); }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Gagal memulai: {ex.Message}", "HP ke Monitor");
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
        // Open Windows Display Settings so the user can arrange Display 1 / Display 2.
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:display") { UseShellExecute = true }); }
        catch { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("desk.cpl") { UseShellExecute = true }); }
    }

    private void OnState(SessionState s)
    {
        switch (s)
        {
            case SessionState.Discovering: SetBadge("MENCARI", (Brush)FindResource("Warn")); break;
            case SessionState.Connecting:
            case SessionState.Configuring: SetBadge("MENYAMBUNG", (Brush)FindResource("Warn")); break;
            case SessionState.Pairing: SetBadge("PAIRING", (Brush)FindResource("Warn")); break;
            case SessionState.Streaming:
                SetBadge("TERSAMBUNG", (Brush)FindResource("Accent"));
                PinCard.Visibility = Visibility.Collapsed;
                DeviceNameText.Text = _session?.PeerDevice.Name ?? "Android";
                break;
            case SessionState.Reconnecting: SetBadge("MENYAMBUNG ULANG", (Brush)FindResource("Warn")); break;
            case SessionState.Disconnected:
            case SessionState.Idle: ResetUi(); break;
        }
    }

    private void ShowPin(string pin)
    {
        PinCard.Visibility = Visibility.Visible;
        PinText.Text = $"{pin[..3]}   {pin[3..]}";
        PinPromptText.Text = $"\"{_session?.PeerDevice.Name}\" ingin terhubung. Masukkan kode ini di Android:";
        DeviceNameText.Text = _session?.PeerDevice.Name ?? "Android";
        Activate();
        // Also show the PIN in a popup so it is always visible (never clipped by the window).
        MessageBox.Show(this,
            $"Kode sambungan untuk \"{_session?.PeerDevice.Name ?? "Android"}\":\n\n        {pin[..3]}  {pin[3..]}\n\nMasukkan kode 6 digit ini di aplikasi HP ke Monitor pada perangkat Android kamu.",
            "HP ke Monitor — Kode Sambungan", MessageBoxButton.OK, MessageBoxImage.Information);
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

    private void SetBadge(string text, Brush color)
    {
        StatusBadge.Text = text;
        StatusBadge.Foreground = color;
    }

    private void ResetUi()
    {
        StartButton.IsEnabled = true;
        DisconnectButton.IsEnabled = false;
        PinCard.Visibility = Visibility.Collapsed;
        SetBadge("TERPUTUS", (Brush)FindResource("FgMuted"));
        DeviceNameText.Text = "—";
        ConnText.Text = ResText.Text = FpsText.Text = LatencyText.Text = BitrateText.Text = "—";
    }

    protected override void OnClosed(EventArgs e)
    {
        _session?.Dispose();
        base.OnClosed(e);
    }
}
