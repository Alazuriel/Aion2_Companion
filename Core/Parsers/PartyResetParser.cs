using System;

namespace AION_2_Companion.Core.Parsers;

public enum PartyResetKind
{
    None,
    Kick,
    Leave
}

public static class PartyResetParser
{
    public static PartyResetKind Parse(
        ReadOnlySpan<byte> frame,
        ReadOnlySpan<byte> kickPrefix,
        ReadOnlySpan<byte> leavePrefix)
    {
        if (kickPrefix.Length > 0 &&
            frame.StartsWith(kickPrefix))
        {
            return PartyResetKind.Kick;
        }

        if (leavePrefix.Length > 0 &&
            frame.StartsWith(leavePrefix))
        {
            return PartyResetKind.Leave;
        }

        return PartyResetKind.None;
    }
}