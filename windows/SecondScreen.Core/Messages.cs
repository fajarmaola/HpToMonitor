using System.Text.Json.Serialization;

namespace SecondScreen.Core;

// Strongly-typed DTOs for control-channel JSON (PROTOCOL.md §3). Serialized with
// System.Text.Json. The "type" discriminator is read first via ControlEnvelope.

public sealed class ControlEnvelope
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
}

public sealed class DeviceInfo
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("os")] public string Os { get; set; } = "";
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }
    [JsonPropertyName("refreshHz")] public int RefreshHz { get; set; }
    [JsonPropertyName("battery")] public int Battery { get; set; }
}

public sealed class Capabilities
{
    [JsonPropertyName("codecs")] public List<string> Codecs { get; set; } = new();
    [JsonPropertyName("maxBitrateKbps")] public int MaxBitrateKbps { get; set; }
    [JsonPropertyName("hwDecode")] public bool HwDecode { get; set; }
}

public sealed class HelloMessage
{
    [JsonPropertyName("type")] public string Type { get; set; } = MessageType.Hello;
    [JsonPropertyName("v")] public int V { get; set; } = Protocol.Version;
    [JsonPropertyName("device")] public DeviceInfo Device { get; set; } = new();
    [JsonPropertyName("caps")] public Capabilities Caps { get; set; } = new();
    [JsonPropertyName("pubKey")] public string PubKey { get; set; } = "";
}

public sealed class HelloAckMessage
{
    [JsonPropertyName("type")] public string Type { get; set; } = MessageType.HelloAck;
    [JsonPropertyName("v")] public int V { get; set; } = Protocol.Version;
    [JsonPropertyName("host")] public DeviceInfo Host { get; set; } = new();
    [JsonPropertyName("pubKey")] public string PubKey { get; set; } = "";
    [JsonPropertyName("trusted")] public bool Trusted { get; set; }
}

public sealed class TokenMessage
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("token")] public string Token { get; set; } = "";
}

public sealed class SessionConfigMessage
{
    [JsonPropertyName("type")] public string Type { get; set; } = MessageType.SessionConfig;
    [JsonPropertyName("codec")] public string Codec { get; set; } = "h264";
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }
    [JsonPropertyName("fps")] public int Fps { get; set; }
    [JsonPropertyName("bitrateKbps")] public int BitrateKbps { get; set; }
    [JsonPropertyName("videoPort")] public int VideoPort { get; set; } = Protocol.VideoUdpPort;
    [JsonPropertyName("encryptVideo")] public bool EncryptVideo { get; set; } = true;
    [JsonPropertyName("orientation")] public string Orientation { get; set; } = "auto";
}

public sealed class PingMessage
{
    [JsonPropertyName("type")] public string Type { get; set; } = MessageType.Ping;
    [JsonPropertyName("ts")] public long Ts { get; set; }
}

public sealed class TouchMessage
{
    [JsonPropertyName("type")] public string Type { get; set; } = MessageType.Touch;
    [JsonPropertyName("deviceId")] public string DeviceId { get; set; } = "";
    [JsonPropertyName("pointerId")] public int PointerId { get; set; }
    [JsonPropertyName("x")] public double X { get; set; }
    [JsonPropertyName("y")] public double Y { get; set; }
    [JsonPropertyName("dx")] public double Dx { get; set; }
    [JsonPropertyName("dy")] public double Dy { get; set; }
    [JsonPropertyName("ts")] public long Ts { get; set; }
    [JsonPropertyName("event")] public string Event { get; set; } = "";
}

public sealed class StatsMessage
{
    [JsonPropertyName("type")] public string Type { get; set; } = MessageType.Stats;
    [JsonPropertyName("decodeFps")] public double DecodeFps { get; set; }
    [JsonPropertyName("renderFps")] public double RenderFps { get; set; }
    [JsonPropertyName("droppedFrames")] public long DroppedFrames { get; set; }
    [JsonPropertyName("jitterMs")] public double JitterMs { get; set; }
}

public sealed class SetQualityMessage
{
    [JsonPropertyName("type")] public string Type { get; set; } = MessageType.SetQuality;
    [JsonPropertyName("bitrateKbps")] public int BitrateKbps { get; set; }
    [JsonPropertyName("fps")] public int Fps { get; set; }
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }
}

public sealed class DisconnectMessage
{
    [JsonPropertyName("type")] public string Type { get; set; } = MessageType.Disconnect;
    [JsonPropertyName("reason")] public string Reason { get; set; } = "";
}

// Phone physical rotation in degrees (0/90/180/270); host rotates the virtual display to match.
public sealed class OrientationMessage
{
    [JsonPropertyName("type")] public string Type { get; set; } = MessageType.Orientation;
    [JsonPropertyName("rotation")] public int Rotation { get; set; }
}
