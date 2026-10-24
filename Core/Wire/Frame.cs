using System;

namespace AION_2_Companion.Core.Wire;

[Flags]
public enum FrameFlags : uint
{
    None       = 0,
    FromServer = 1 << 0,
    FromClient = 1 << 1,
    WasLZ4     = 1 << 2,
    WasBundled = 1 << 3,
    Resynced   = 1 << 4
}

public sealed record Frame(
    DateTimeOffset Time,
    string Source,
    string Destination,
    Opcode Opcode,
    FrameFlags Flags,
    int InterfaceIndex,
    byte[] Payload);