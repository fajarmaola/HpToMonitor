using System.Text.Json;
using System.Text.Json.Serialization;

namespace SecondScreen.Core;

// Persists paired ("trusted") Android devices locally so future connections can skip the
// PIN step (PROTOCOL.md §3.2 step 2/6). No cloud — a JSON file under %LOCALAPPDATA%.
public sealed class TrustedDevice
{
    [JsonPropertyName("deviceName")] public string DeviceName { get; set; } = "";
    [JsonPropertyName("publicKeyB64")] public string PublicKeyB64 { get; set; } = "";
    [JsonPropertyName("pairedAtUtc")] public DateTime PairedAtUtc { get; set; }
    [JsonPropertyName("failedAttempts")] public int FailedAttempts { get; set; }
}

public sealed class TrustedDeviceStore
{
    private readonly string _path;
    private readonly object _lock = new();
    private Dictionary<string, TrustedDevice> _byKey = new();

    public TrustedDeviceStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SecondScreenLocal", "trusted_devices.json");
        Load();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                var list = JsonSerializer.Deserialize<List<TrustedDevice>>(json) ?? new();
                _byKey = list.ToDictionary(d => d.PublicKeyB64, d => d);
            }
        }
        catch { _byKey = new(); }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var json = JsonSerializer.Serialize(_byKey.Values.ToList(),
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_path, json);
    }

    public bool IsTrusted(string publicKeyB64)
    {
        lock (_lock) return _byKey.ContainsKey(publicKeyB64);
    }

    public TrustedDevice? Get(string publicKeyB64)
    {
        lock (_lock) return _byKey.TryGetValue(publicKeyB64, out var d) ? d : null;
    }

    public void Trust(string publicKeyB64, string deviceName)
    {
        lock (_lock)
        {
            _byKey[publicKeyB64] = new TrustedDevice
            {
                DeviceName = deviceName,
                PublicKeyB64 = publicKeyB64,
                PairedAtUtc = DateTime.UtcNow,
                FailedAttempts = 0
            };
            Save();
        }
    }

    // Rate-limit PIN guessing (PROTOCOL.md §3.2). Returns true if the device is locked out.
    public bool RegisterFailureAndCheckLockout(string publicKeyB64)
    {
        lock (_lock)
        {
            if (!_byKey.TryGetValue(publicKeyB64, out var d))
            {
                d = new TrustedDevice { PublicKeyB64 = publicKeyB64 };
                _byKey[publicKeyB64] = d;
            }
            d.FailedAttempts++;
            Save();
            return d.FailedAttempts >= Protocol.MaxPairFailures;
        }
    }

    public void Revoke(string publicKeyB64)
    {
        lock (_lock)
        {
            if (_byKey.Remove(publicKeyB64)) Save();
        }
    }

    public IReadOnlyCollection<TrustedDevice> All()
    {
        lock (_lock) return _byKey.Values.ToList();
    }
}
