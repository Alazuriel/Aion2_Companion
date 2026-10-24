namespace AION_2_Companion.Core.GameData;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public sealed class MobsDatabase
{
    private readonly Dictionary<int, MobDefinition> _byId = [];
    private readonly Dictionary<string, List<MobDefinition>> _byName =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<MobDefinition> Mobs { get; }

    private MobsDatabase(MobDefinition[] mobs)
    {
        Mobs = mobs;

        foreach (var mob in mobs)
        {
            if (mob.Id <= 0)
                throw new InvalidDataException($"Érvénytelen mob ID: {mob.Id}");

            if (string.IsNullOrWhiteSpace(mob.Name))
                throw new InvalidDataException($"A mob neve üres: ID {mob.Id}");

            if (string.IsNullOrWhiteSpace(mob.CreatureType))
                throw new InvalidDataException($"Hiányzik a creature_type: ID {mob.Id}");

            if (!_byId.TryAdd(mob.Id, mob))
                throw new InvalidDataException($"Duplikált mob ID: {mob.Id}");

            if (!_byName.TryGetValue(mob.Name, out var sameName))
            {
                sameName = [];
                _byName[mob.Name] = sameName;
            }

            sameName.Add(mob);
        }
    }

    public static MobsDatabase Load(string path)
    {
        return new MobsDatabase(ForgeDataReader.LoadMobs(path));
    }

    public MobDefinition? GetById(int id)
    {
        return _byId.TryGetValue(id, out var mob) ? mob : null;
    }

    /// <summary>
    /// Azonos nevű mobokat (pl. "Kapu") több ID is képviselhet, ezért lista jön vissza.
    /// </summary>
    public IReadOnlyList<MobDefinition> GetByName(string name)
    {
        return _byName.TryGetValue(name, out var mobs)
            ? mobs
            : Array.Empty<MobDefinition>();
    }

    public IReadOnlyList<MobDefinition> GetByCreatureType(string creatureType)
    {
        return Mobs
            .Where(x => string.Equals(x.CreatureType, creatureType, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public IReadOnlyList<string> GetCreatureTypes()
    {
        return Mobs
            .Select(x => x.CreatureType)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<MobDefinition> Search(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<MobDefinition>();

        return Mobs
            .Where(x => x.Name.Contains(text, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }
}