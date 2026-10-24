using System;
using System.Net;
using System.Net.Sockets;

namespace AION_2_Companion.Core.Wire;

[Flags]
public enum WireFrameFlags
{
    None = 0,
    FromServer = 1 << 0,
    FromClient = 1 << 1,
    WasLz4 = 1 << 2,
    WasBundled = 1 << 3,
    Resynced = 1 << 4
}

public sealed record WireFrame(
    DateTimeOffset Time,
    string Source,
    string Destination,
    Opcode Opcode,
    byte[] Payload,
    WireFrameFlags Flags = WireFrameFlags.None)
{
    public int PayloadLength => Payload.Length;
}

/// <summary>
/// Parses the existing CapturedPacket endpoint format "IP:port".
/// Splitting at the final colon supports IPv4 and unbracketed IPv6 strings.
/// </summary>
internal static class EndpointText
{
    public static bool TryParse(string text, out IPAddress address, out ushort port)
    {
        address = IPAddress.None;
        port = 0;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        int split = text.LastIndexOf(':');
        if (split <= 0 || split == text.Length - 1)
            return false;

        string host = text[..split].Trim('[', ']');
        string portText = text[(split + 1)..];

        if (!IPAddress.TryParse(host, out address!))
            return false;

        if (!ushort.TryParse(portText, out port))
            return false;

        return true;
    }
}
