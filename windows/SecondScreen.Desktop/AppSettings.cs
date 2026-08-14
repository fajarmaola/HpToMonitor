using System.IO;
using System.Text.Json;

namespace SecondScreen.Desktop;

// Persists small user preferences (currently just language) under %AppData%\HPkeMonitor.
public static class AppSettings
{
    private sealed class Data { public string Language { get; set; } = "ID"; }

    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HPkeMonitor");
    private static string FilePath => Path.Combine(Dir, "settings.json");

    public static void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var d = JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath));
            if (d != null) Loc.Current = d.Language == "EN" ? AppLang.EN : AppLang.ID;
        }
        catch { /* ignore — default stays ID */ }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var d = new Data { Language = Loc.Current == AppLang.EN ? "EN" : "ID" };
            File.WriteAllText(FilePath, JsonSerializer.Serialize(d));
        }
        catch { /* ignore */ }
    }
}
