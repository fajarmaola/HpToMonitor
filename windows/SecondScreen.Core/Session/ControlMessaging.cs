using System.Text;
using System.Text.Json;

namespace SecondScreen.Core;

// Serialize/parse control JSON and (in the secure phase) wrap it with AES-256-GCM.
public static class ControlMessaging
{
    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public static byte[] Serialize(object message)
        => JsonSerializer.SerializeToUtf8Bytes(message, message.GetType(), JsonOpts);

    // Build a transport payload. If key != null, encrypt (secure phase); else plaintext (handshake).
    public static byte[] BuildFrame(object message, byte[]? key)
    {
        byte[] json = Serialize(message);
        return key == null ? json : CryptoUtil.Encrypt(key, json);
    }

    // Decode a received transport payload to UTF-8 JSON text. Decrypts if key != null.
    public static string DecodeText(byte[] payload, byte[]? key)
    {
        byte[] json = key == null ? payload : CryptoUtil.Decrypt(key, payload);
        return Encoding.UTF8.GetString(json);
    }

    public static string PeekType(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
        }
        catch { return ""; }
    }

    public static T? Parse<T>(string json) => JsonSerializer.Deserialize<T>(json, JsonOpts);
}
