namespace AION_2_Companion.Core.GameData;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public sealed class BossesDatabase
{
    private readonly Dictionary<int, BossDefinition> _byTemplateId = [];
    private readonly Dictionary<string, BossDefinition> _byName =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<BossDefinition> Bosses { get; }

    private BossesDatabase(IEnumerable<BossDefinition> bosses)
    {
        Bosses = bosses.Where(boss => boss.Enabled).ToArray();
        if (Bosses.Count == 0)
            throw new InvalidDataException("The boss data pack is empty.");

        foreach (BossDefinition boss in Bosses)
        {
            if (string.IsNullOrWhiteSpace(boss.Id) || boss.TemplateIds.Length == 0)
                throw new InvalidDataException("Every boss needs an id and at least one template_id.");
            if (boss.TemplateIds.Any(id => id <= 0))
                throw new InvalidDataException($"Invalid template id for boss: {boss.Id}");
            if (boss.MaxHp < 0 || boss.EnrageSeconds is < 0 || boss.EnrageWarningSeconds is < 0)
                throw new InvalidDataException($"Invalid HP or enrage value for boss: {boss.Id}");

            foreach (int templateId in boss.TemplateIds)
            {
                if (!_byTemplateId.TryAdd(templateId, boss))
                    throw new InvalidDataException($"Duplicate boss template_id: {templateId}");
            }

            AddNames(boss, boss.Names.Values.Concat(boss.Aliases));
        }
    }

    public static BossesDatabase Load(string path) =>
        new(ForgeDataReader.LoadBosses(path));

    public BossDefinition? GetByTemplateId(int templateId) =>
        _byTemplateId.TryGetValue(templateId, out BossDefinition? boss) ? boss : null;

    public BossDefinition? GetByName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && _byName.TryGetValue(name.Trim(), out BossDefinition? boss)
            ? boss
            : null;

    public BossDefinition? Find(int templateId, string? name) =>
        GetByTemplateId(templateId) ?? GetByName(name);

    private void AddNames(BossDefinition boss, IEnumerable<string> names)
    {
        foreach (string name in names.Where(name => !string.IsNullOrWhiteSpace(name)))
            _byName.TryAdd(name.Trim(), boss);
    }
}

public static class BossCatalog
{
    private static BossesDatabase? _database;

    public static void Initialize(BossesDatabase? database) => _database = database;

    public static BossDefinition? Find(int templateId, string? name = null) =>
        _database?.Find(templateId, name);

    public static bool IsBoss(int templateId, string? name = null) =>
        Find(templateId, name) is not null;
}
