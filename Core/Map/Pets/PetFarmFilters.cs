using System;
using System.Collections.Generic;
using System.Linq;

namespace AION_2_Companion.Core.Map.Pets;

// These are per-character, in-memory values. The .nfg remains read-only.
public sealed record PetFarmOwnedState(int PetId, int Level, int SoulProgress);
public sealed record PetFarmCatalogEntry(
    int PetId, string Name, string Category, int SpawnCount,
    int? Level, int SoulProgress, string MapId, string MapName);

public enum PetCollectionSyncState
{
    Unknown, // No pet ownership evidence yet.
    Partial, // Individual PetAdded/PetLevel events, but no complete 00 90.
    Ready    // Full, validated 00 90 for the current session/character.
}

public sealed record PetFarmFilterSettings(
    bool ShowMissing = true,
    bool ShowLevel1 = false,
    bool ShowLevel2 = false,
    bool ShowLevel3 = false,
    string? Category = null,
    int? SpecificPetId = null,
    string MapId = PetFarmMapCatalog.VerteronId);

/// <summary>
/// A short-lived, in-memory bridge between the decoded packets and the map UI.
/// "Partial" never means that absent Pet IDs are missing.
/// Invoke from the same UI thread that processes OnFrameDecoded.
/// </summary>
public static class PetCollectionBridge
{
    private static readonly Dictionary<int, PetFarmOwnedState> Known = new();
    private static string? _characterName;

    public static event Action<IReadOnlyList<PetFarmOwnedState>?>? Changed;

    public static PetCollectionSyncState Status { get; private set; } =
        PetCollectionSyncState.Unknown;

    public static string? CharacterName => _characterName;

    // Null = no confirmed ownership knowledge. Non-null may still be PARTIAL.
    public static IReadOnlyList<PetFarmOwnedState>? Snapshot { get; private set; }

    // A first seen character binds the current (possibly already received) 00 90.
    // A different confirmed character clears the old character's collection.
    public static void BindCharacter(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        string key = name.Trim();
        if (_characterName is null)
        {
            _characterName = key;
            Console.WriteLine($"[PET CHAR] Bound: {key}");
            return;
        }

        if (string.Equals(_characterName, key, StringComparison.OrdinalIgnoreCase))
            return;

        Console.WriteLine($"[PET CHAR] {_characterName} -> {key} | resetting collection");
        _characterName = key;
        ResetCollection();
    }

    public static void Publish(IEnumerable<PetFarmOwnedState> pets)
    {
        if (pets is null)
            throw new ArgumentNullException(nameof(pets));

        Known.Clear();
        foreach (PetFarmOwnedState pet in pets)
            if (ValidPetId(pet.PetId) && pet.Level is >= 1 and <= 3)
                Known[pet.PetId] = pet;

        Status = PetCollectionSyncState.Ready;
        Broadcast();
        Console.WriteLine($"[PET BRIDGE] FULL | Pets={Known.Count} | Character={_characterName ?? "unbound"}");
    }

    public static void NotePetAdded(int petId, int level)
    {
        if (!ValidPetId(petId) || level is < 1 or > 3)
            return;

        // An explicit acquisition is proof of ownership, even without 00 90.
        if (Known.TryGetValue(petId, out var old) && old.Level == level)
            return;

        Known[petId] = new PetFarmOwnedState(petId, level, 0);
        if (Status == PetCollectionSyncState.Unknown)
            Status = PetCollectionSyncState.Partial;

        Broadcast();
        Console.WriteLine($"[PET LIVE] ADDED | ID={petId} | Lv={level} | Sync={Status}");
    }

    public static void NotePetLevel(int petId, int level)
    {
        if (!ValidPetId(petId) || level is < 1 or > 3)
            return;

        // A level-up is evidence that this pet is owned.
        if (Known.TryGetValue(petId, out var old) &&
            old.Level == level && old.SoulProgress == 0)
            return;

        Known[petId] = new PetFarmOwnedState(petId, level, 0);
        if (Status == PetCollectionSyncState.Unknown)
            Status = PetCollectionSyncState.Partial;

        Broadcast();
        Console.WriteLine($"[PET LIVE] LEVEL | ID={petId} | Lv={level} | Sync={Status}");
    }

    public static void NotePetProgress(int petId, int gained)
    {
        if (!ValidPetId(petId) || gained is <= 0 or >= 1000)
            return;

        // A soul drop is NOT sufficient proof that a pet was unlocked.
        // Avoid creating a fake owned entry when no 00 90 has been seen.
        if (!Known.TryGetValue(petId, out PetFarmOwnedState? old))
        {
            Console.WriteLine($"[PET LIVE] SOUL | ID={petId} +{gained} | owner unknown (not added)");
            return;
        }

        if (old.Level >= 3 || old.SoulProgress > int.MaxValue - gained)
            return;

        Known[petId] = old with { SoulProgress = old.SoulProgress + gained };
        Broadcast();
        Console.WriteLine($"[PET LIVE] SOUL | ID={petId} +{gained} | Total={Known[petId].SoulProgress}");
    }

    // Call only for a proven disconnect/new capture session, or when the character changes.
    public static void Clear()
    {
        _characterName = null;
        ResetCollection();
    }

    private static void ResetCollection()
    {
        Known.Clear();
        Snapshot = null;
        Status = PetCollectionSyncState.Unknown;
        Changed?.Invoke(null);
    }

    private static void Broadcast()
    {
        Snapshot = Known.Values.OrderBy(x => x.PetId).ToArray();
        Changed?.Invoke(Snapshot);
    }

    private static bool ValidPetId(int id) => id is >= 1000 and <= 99999;
}
