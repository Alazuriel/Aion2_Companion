
namespace AION_2_Companion.Core.GameData;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public sealed class SkillDatabase
{
    private readonly Dictionary<uint, SkillDefinition> _byId = [];
    private readonly Dictionary<string, List<SkillDefinition>> _byClass =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<SkillDefinition> Skills { get; }

    private SkillDatabase(SkillDefinition[] skills)
    {
        Skills = skills;

        foreach (SkillDefinition skill in skills)
        {
            if (string.IsNullOrWhiteSpace(skill.Id) ||
                !uint.TryParse(skill.Id, out uint id) ||
                id == 0)
            {
                throw new InvalidDataException(
                    $"Érvénytelen skill ID: '{skill.Id}', név: '{skill.Name}'.");
            }

            if (string.IsNullOrWhiteSpace(skill.Name))
            {
                throw new InvalidDataException(
                    $"A skill neve üres. ID: {skill.Id}");
            }

            if (!_byId.TryAdd(id, skill))
            {
                throw new InvalidDataException(
                    $"Duplikált skill ID az NFG-ben: {id}");
            }

            if (!string.IsNullOrWhiteSpace(skill.ClassName))
            {
                if (!_byClass.TryGetValue(
                        skill.ClassName, out List<SkillDefinition>? classSkills))
                {
                    classSkills = [];
                    _byClass.Add(skill.ClassName, classSkills);
                }

                classSkills.Add(skill);
            }
        }
    }

    public static SkillDatabase Load(string path)
    {
        SkillDefinition[] skills = ForgeDataReader.LoadSkills(path);
        return new SkillDatabase(skills);
    }

    public SkillDefinition? GetById(uint id)
    {
        return _byId.TryGetValue(id, out SkillDefinition? skill)
            ? skill
            : null;
    }

    public SkillDefinition? GetById(string id)
    {
        if (!uint.TryParse(id, out uint numericId))
            return null;

        return GetById(numericId);
    }

    public SkillDefinition? FindVariant(uint runtimeSkillId)
    {
        SkillDefinition? exact = GetById(runtimeSkillId);
        if (exact is not null)
            return exact;

        string id = runtimeSkillId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (id.Length != 8)
            return null;

        // A játék az alap skill négyjegyű családazonosítója mögött kódolja a
        // futásidejű szintet és variánst (például 16010000 -> 16010340).
        // Egy családon belül a numerikusan legközelebbi ismert definíció adja
        // a helyes nevet; így a summon-variánsok sem az első találat nevét
        // öröklik meg automatikusan.
        string prefix = id[..4];
        SkillDefinition? closest = null;
        uint closestDistance = uint.MaxValue;
        foreach (SkillDefinition skill in Skills)
        {
            if (skill.Id.Length != 8 ||
                !skill.Id.StartsWith(prefix, StringComparison.Ordinal) ||
                !uint.TryParse(skill.Id, out uint knownId))
            {
                continue;
            }

            uint distance = knownId > runtimeSkillId
                ? knownId - runtimeSkillId
                : runtimeSkillId - knownId;
            if (distance >= closestDistance)
                continue;

            closest = skill;
            closestDistance = distance;
        }

        return closest;
    }

    public IReadOnlyList<SkillDefinition> GetByClass(string className)
    {
        if (string.IsNullOrWhiteSpace(className))
            return [];

        return _byClass.TryGetValue(
            className, out List<SkillDefinition>? skills)
            ? skills
            : [];
    }

    public IReadOnlyList<SkillDefinition> SearchByName(
        string query, int maxResults = 50)
    {
        if (string.IsNullOrWhiteSpace(query) || maxResults <= 0)
            return [];

        return Skills
            .Where(skill => skill.Name.Contains(
                query, StringComparison.OrdinalIgnoreCase))
            .Take(maxResults)
            .ToArray();
    }

    public SkillReference? GetNextChainSkill(uint skillId)
    {
        SkillDefinition? skill = GetById(skillId);

        if (skill is null || skill.Chain.Count == 0)
            return null;

        // A chain első eleme gyakran maga az aktuális skill,
        // ezért a következő elem az első olyan, amely más ID-val rendelkezik.
        return skill.Chain.FirstOrDefault(
            entry => entry.IdValue != skillId);
    }
}
