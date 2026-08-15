using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;

namespace SecondScreen.Core;

public sealed class UpdateInfo
{
    public bool Available;
    public string LatestVersion = "";
    public string CurrentVersion = "";
    public string? InstallerUrl;
    public string Notes = "";
    public string Message = "";
}

// Checks GitHub Releases for a newer build and downloads/launches the installer. Internet is used
// ONLY when the user taps "Check for updates" — all streaming/config stays offline (LAN).
// The release feed is the repo's "latest" release; version comes from the attached version.json.
public static class Updater
{
    // Public GitHub repo that hosts the Releases (installer + apk + version.json).
    public const string RepoOwner = "fajarmaola";
    public const string RepoName = "HpToMonitor";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("HPkeMonitor-Updater");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    public static string CurrentVersion()
    {
        var v = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
        return v == null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    public static async Task<UpdateInfo> CheckAsync()
    {
        var info = new UpdateInfo { CurrentVersion = CurrentVersion() };
        try
        {
            var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
            var json = await Http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string? versionJsonUrl = null;
            string? exeUrl = null;
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.GetProperty("name").GetString() ?? "";
                    var dl = a.GetProperty("browser_download_url").GetString();
                    if (name.Equals("version.json", StringComparison.OrdinalIgnoreCase)) versionJsonUrl = dl;
                    else if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) exeUrl = dl;
                }
            }

            string latest = root.TryGetProperty("tag_name", out var tg) ? (tg.GetString() ?? "") : "";
            if (versionJsonUrl != null)
            {
                try
                {
                    var vj = await Http.GetStringAsync(versionJsonUrl);
                    using var vd = JsonDocument.Parse(vj);
                    if (vd.RootElement.TryGetProperty("version", out var vv)) latest = vv.GetString() ?? latest;
                    if (vd.RootElement.TryGetProperty("notes", out var nn)) info.Notes = nn.GetString() ?? "";
                }
                catch { /* fall back to tag_name */ }
            }

            latest = latest.TrimStart('v', 'V');
            info.LatestVersion = latest;
            info.InstallerUrl = exeUrl;
            info.Available = Compare(latest, info.CurrentVersion) > 0 && exeUrl != null;
            if (exeUrl == null) info.Message = "Aset installer (.exe) tidak ditemukan di rilis terbaru.";
            return info;
        }
        catch (Exception ex)
        {
            info.Message = "Gagal memeriksa pembaruan: " + ex.Message;
            return info;
        }
    }

    public static async Task<string?> DownloadInstallerAsync(string url, IProgress<double>? progress = null)
    {
        try
        {
            using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? -1;
            var path = Path.Combine(Path.GetTempPath(), "HPkeMonitor-Setup.exe");
            await using (var src = await resp.Content.ReadAsStreamAsync())
            await using (var dst = File.Create(path))
            {
                var buf = new byte[81920];
                long read = 0; int n;
                while ((n = await src.ReadAsync(buf)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n));
                    read += n;
                    if (total > 0) progress?.Report((double)read / total);
                }
            }
            return path;
        }
        catch { return null; }
    }

    // Runs the downloaded installer SILENTLY (no wizard clicks) so an update feels like an in-app
    // update, not a manual reinstall. Inno Setup closes the running app (Restart Manager) + our own
    // Application.Shutdown frees the files, then the installer relaunches the app in silent mode.
    // A single UAC prompt is unavoidable because the install writes to Program Files + installs the
    // display driver (pnputil) — both require elevation.
    public static void RunInstaller(string path) =>
        Process.Start(new ProcessStartInfo(path)
        {
            UseShellExecute = true,
            Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS"
        });

    // Returns >0 when a > b, <0 when a < b, 0 when equal (numeric, dotted).
    private static int Compare(string a, string b)
    {
        var pa = Parse(a); var pb = Parse(b);
        for (int i = 0; i < 4; i++)
        {
            int x = i < pa.Length ? pa[i] : 0;
            int y = i < pb.Length ? pb[i] : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }

    private static int[] Parse(string v)
    {
        var list = new List<int>();
        foreach (var p in v.Split('.', '-', '+', ' '))
        {
            if (int.TryParse(p, out var n)) list.Add(n);
            else if (list.Count > 0) break;
        }
        return list.ToArray();
    }
}
