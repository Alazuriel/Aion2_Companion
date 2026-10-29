using System;

namespace AION_2_Companion.Core.Accounts;

public sealed record DetectedCharacterSnapshot(
    string Name,
    string ClassName,
    string? ClassIconPath,
    byte? Level,
    uint? GearScore,
    ushort? ServerId,
    uint SessionEntityId,
    DateTimeOffset DetectedAtUtc);

public sealed class DetectedCharacterService
{
    public static DetectedCharacterService Shared { get; } = new();

    public event EventHandler? Changed;

    public DetectedCharacterSnapshot? Current { get; private set; }

    private DetectedCharacterService()
    {
    }

    public void Update(DetectedCharacterSnapshot character)
    {
        ArgumentNullException.ThrowIfNull(character);

        if (Current is not null &&
            Current.Name == character.Name &&
            Current.ClassName == character.ClassName &&
            Current.ClassIconPath == character.ClassIconPath &&
            Current.Level == character.Level &&
            Current.GearScore == character.GearScore &&
            Current.ServerId == character.ServerId &&
            Current.SessionEntityId == character.SessionEntityId)
            return;

        Current = character;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        if (Current is null)
            return;

        Current = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
