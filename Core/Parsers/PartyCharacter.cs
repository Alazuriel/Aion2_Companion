namespace AION_2_Companion.Core.Parsers;

public sealed class PartyCharacter
{
    public uint Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public ushort ServerId { get; init; }

    public byte? ClassId { get; init; }

    public byte? Level { get; init; }

    public uint Gs { get; init; }
}