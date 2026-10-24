using System;

namespace AION_2_Companion.Core.Capture;

public sealed record CapturedPacket(
    DateTimeOffset Time,
    string InterfaceName,
    string Source,
    string Destination,
    byte[] Payload,
    string Protocol = "UDP",
    uint? TcpSequenceNumber = null,
    uint? TcpAcknowledgmentNumber = null,
    byte TcpFlags = 0
)
{
    public bool IsTcp => Protocol.Equals(
        "TCP",
        StringComparison.OrdinalIgnoreCase);

    public bool IsUdp => Protocol.Equals(
        "UDP",
        StringComparison.OrdinalIgnoreCase);
}