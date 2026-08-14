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
    }

    private QualityMode SelectedQuality() => QualityCombo.SelectedIndex switch
    {
        0 => QualityMode.Performance,
        2 => QualityMode.HighQuality,
        _ => QualityMode.Balanced
    };

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
            MessageBox.Show(this, msg, "SecondScreen Local", MessageBoxButton.OK, MessageBoxImage.Warning));
        _session.Diagnostics.Updated += (_, _) => Dispatcher.Invoke(UpdateDiagnostics);

        StartButton.IsEnabled = false;
        DisconnectButton.IsEnabled = true;
        SetBadge("WAITING", (Brush)FindResource("Warn"));

        try { await _session.StartAsync(); }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to start hosting: {ex.Message}", "Error");
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
            case SessionState.Discovering: SetBadge("SEARCHING", (Brush)FindResource("Warn")); break;
            case SessionState.Connecting:
            case SessionState.Configuring: SetBadge("CONNECTING", (Brush)FindResource("Warn")); break;
            case SessionState.Pairing: SetBadge("PAIRING", (Brush)FindResource("Warn")); break;
            case SessionState.Streaming:
                SetBadge("CONNECTED", (Brush)FindResource("Accent"));
                PinCard.Visibility = Visibility.Collapsed;
                DeviceNameText.Text = _session?.PeerDevice.Name ?? "Android";
                break;
            case SessionState.Reconnecting: SetBadge("RECONNECTING", (Brush)FindResource("Warn")); break;
            case SessionState.Disconnected:
            case SessionState.Idle: ResetUi(); break;
        }
    }

    private void ShowPin(string pin)
    {
        PinCard.Visibility = Visibility.Visible;
        PinText.Text = $"{pin[..3]}   {pin[3..]}";
        PinPromptText.Text = $"\"{_session?.PeerDevice.Name}\" wants to connect. Enter this code on Android:";
        DeviceNameText.Text = _session?.PeerDevice.Name ?? "Android";
        Activate();
        // Also show the PIN in a popup so it is always visible (never clipped by the window).
        MessageBox.Show(this,
            $"Pairing code for \"{_session?.PeerDevice.Name ?? "Android"}\":\n\n        {pin[..3]}  {pin[3..]}\n\nEnter this 6-digit code in the SecondScreen app on your Android device.",
            "SecondScreen — Pairing Code", MessageBoxButton.OK, MessageBoxImage.Information);
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
        SetBadge("DISCONNECTED", (Brush)FindResource("FgMuted"));
        DeviceNameText.Text = "—";
        ConnText.Text = ResText.Text = FpsText.Text = LatencyText.Text = BitrateText.Text = "—";
    }

    protected override void OnClosed(EventArgs e)
    {
        _session?.Dispose();
        base.OnClosed(e);
    }
}
