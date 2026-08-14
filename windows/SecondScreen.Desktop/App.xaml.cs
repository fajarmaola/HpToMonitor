using System.Windows;
using System.Windows.Threading;

namespace SecondScreen.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppSettings.Load(); // restore saved language before any window is shown

        // Brief branded splash, then the main window.
        var splash = new SplashWindow();
        splash.Show();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var main = new MainWindow();
            main.Show();
            splash.Close();
        };
        timer.Start();
    }
}
