using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

using AION_2_Companion.Core.GameData;

namespace AION_2_Companion.Core.Phaseward;

// Data-only loader: NXFG authentication/decryption remains in ForgeDataReader.
// Source-matched client IDs are candidate identifiers, NOT confirmed packet triggers.
public sealed record PhasewardMechanic(
    string Id, string Name, uint[] SkillIds, double? CastSeconds,
    double? CooldownSeconds, string[] BossLines, double[] HpThresholds,
    string[] Notes);

public sealed record PhasewardCue(
    string Id, string Name, double[] HpThresholds, string[] BossLines,
    string[] Details, string Evidence);

public sealed record PhasewardEncounter(
    string Id, string Name, string Instance, int[] BossNpcIds,
    PhasewardMechanic[] Mechanics, PhasewardCue[] Cues,
    string[] CommunityNotes, string[] FrenzyReferenceTimes);

public sealed class PhasewardPack
{
    private sealed record Candidate(PhasewardEncounter Encounter, int Priority);
    private readonly Dictionary<int, PhasewardEncounter> _byNpc = new();
    private readonly Dictionary<string, PhasewardEncounter> _byName =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<int> _ambiguousNpcIds = new();
    private readonly Dictionary<int, List<Candidate>> _candidates = new();

    public IReadOnlyCollection<int> AmbiguousBossNpcIds => _ambiguousNpcIds;
    public IReadOnlyCollection<PhasewardEncounter> Encounters => _byNpc.Values.Distinct().ToArray();

    public PhasewardEncounter? Find(int npcId) =>
        _byNpc.TryGetValue(npcId, out PhasewardEncounter? match) ? match : null;

    public PhasewardEncounter? Find(int npcId, params string?[] names)
    {
        if (Find(npcId) is PhasewardEncounter byNpc)
            return byNpc;

        foreach (string? name in names)
        {
            string normalized = NormalizeBossName(name);
            if (normalized.Length > 0 && _byName.TryGetValue(normalized, out PhasewardEncounter? byName))
                return byName;
        }

        return null;
    }

    public static PhasewardPack Load(string path)
    {
        // IMPORTANT: ForgeDataReader.Load() projects into the generic document model
        // and rejects a dataset-specific DBM/Phaseward envelope before we can parse it.
        // LoadPayloadJson authenticates NXFG v1/v2 using the SAME existing decoder,
        // but preserves the complete JSON payload, including any dataset wrapper.
        JsonElement json = ForgeDataReader.LoadPayloadJson(path);
        return FromJson(json);
    }

    // Used by tests/previews; does not create NFGs or weaken verification of Load(path).
    public static PhasewardPack FromJson(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.Object)
        {
            string dataset = Str(json, "dataset");
            if (!string.IsNullOrEmpty(dataset) &&
                !dataset.Equals("dbm", StringComparison.OrdinalIgnoreCase) &&
                !dataset.Equals("phaseward", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Expected Phaseward/dbm dataset; found '{dataset}'.");
        }
        var pack = new PhasewardPack();
        if (!LocateRecords(json, 0, out JsonElement rows, out bool areInstances))
        {
            string keys = json.ValueKind == JsonValueKind.Object
                ? string.Join(", ", json.EnumerateObject().Select(x => x.Name).Take(24))
                : json.ValueKind.ToString();
            throw new InvalidDataException(
                $"Phaseward NFG payload has no boss records. Root keys: [{keys}]. " +
                "Expected instances/encounters, possibly inside dbm, phaseward, data or payload. " +
                "Metadata (source/totals) alone is not enough.");
        }
        if (areInstances)
        {
            foreach (JsonElement instance in rows.EnumerateArray())
            {
                if (instance.ValueKind != JsonValueKind.Object) continue;
                string instanceName = Str(instance, "name", Str(instance, "instance_id"));
                if (Arr(instance, "bosses") is not JsonElement bosses) continue;
                foreach (JsonElement boss in bosses.EnumerateArray())
                    pack.CollectBoss(boss, instanceName);
            }
        }
        else
        {
            foreach (JsonElement boss in rows.EnumerateArray())
                pack.CollectBoss(boss, Str(boss, "instance", Str(boss, "instance_id")));
        }
        pack.FinishIndex();
        if (pack._byNpc.Count == 0)
            throw new InvalidDataException(
                $"No unambiguous Phaseward boss NPC IDs. Ambiguous IDs: {pack._ambiguousNpcIds.Count}. " +
                "Check boss_npc_id/template_ids and linked_npcs in the pack.");
        return pack;
    }

    // A strict, bounded envelope walk. Never treat metadata source/totals as boss records.
    private static bool LocateRecords(JsonElement node, int depth, out JsonElement rows, out bool instances)
    {
        rows = default;
        instances = false;
        if (depth > 5) return false;
        if (node.ValueKind == JsonValueKind.Array)
        {
            if (node.GetArrayLength() == 0) return false;
            JsonElement first = node.EnumerateArray().FirstOrDefault(x => x.ValueKind == JsonValueKind.Object);
            if (first.ValueKind != JsonValueKind.Object) return false;
            if (Arr(first, "bosses") is not null || Has(first, "instance_id") && Has(first, "name"))
            {
                rows = node;
                instances = true;
                return true;
            }
            if (Has(first, "boss_npc_id") || Has(first, "template_ids") ||
                (Has(first, "name") && Arr(first, "mechanics") is not null))
            {
                rows = node;
                instances = false;
                return true;
            }
            return false;
        }
        if (node.ValueKind != JsonValueKind.Object) return false;
        if (Num(node, "schema_version") is double ver && ver != 1)
            throw new InvalidDataException($"Unsupported Phaseward schema_version {ver}.");
        if (Arr(node, "instances") is JsonElement directInstances && directInstances.GetArrayLength() > 0)
        {
            rows = directInstances; instances = true; return true;
        }
        if (Arr(node, "encounters") is JsonElement directEncounters && directEncounters.GetArrayLength() > 0)
        {
            rows = directEncounters; instances = false; return true;
        }
        // Do not look for a typed collection named 'bosses' at the generic NXFG root:
        // other ForgeData datasets use that name for a completely different schema.
        foreach (string key in new[] { "phaseward", "dbm", "data", "payload", "content", "master", "items", "records", "source" })
        {
            if (!node.TryGetProperty(key, out JsonElement child)) continue;
            if (LocateRecords(child, depth + 1, out rows, out instances)) return true;
        }
        return false;
    }

    private void CollectBoss(JsonElement boss, string instance)
    {
        if (boss.ValueKind != JsonValueKind.Object) return;
        var ids = new HashSet<int>();
        foreach (string key in new[] { "boss_npc_id", "template_id", "npc_id" })
            if (PosInt(boss, key) is int id) ids.Add(id);
        foreach (string key in new[] { "boss_template_ids", "npc_ids", "template_ids" })
            if (Arr(boss, key) is JsonElement arr)
                foreach (JsonElement item in arr.EnumerateArray())
                    if (ToInt(item) is int id) ids.Add(id);

        // Overview pages have a *group* named "Bosses" and linked_npcs with the real
        // NPCs; do not create a fake "Bosses" encounter with all those IDs combined.
        if (ids.Count == 0 && Str(boss, "name").Equals("Bosses", StringComparison.OrdinalIgnoreCase))
        {
            if (Arr(boss, "linked_npcs") is JsonElement links)
            {
                foreach (JsonElement link in links.EnumerateArray())
                {
                    if (link.ValueKind != JsonValueKind.Object || PosInt(link, "id") is not int npcId) continue;
                    string name = Str(link, "name", $"NPC {npcId}");
                    var overview = new PhasewardEncounter($"{instance}/{npcId}", name, instance,
                        [npcId], [], [], [], []);
                    AddCandidate(npcId, overview, 0);
                }
            }
            return;
        }
        if (ids.Count == 0) return;

        string nameOfBoss = Str(boss, "name");
        if (string.IsNullOrWhiteSpace(nameOfBoss) && Prop(boss, "names") is JsonElement names)
            nameOfBoss = Str(names, "en");
        if (string.IsNullOrWhiteSpace(nameOfBoss)) nameOfBoss = $"NPC {ids.Min()}";
        string encounterId = Str(boss, "id", $"{instance}/{ids.Min()}");
        var mechanics = new List<PhasewardMechanic>();
        AddMechanics(boss, "mechanics", "mechanic", encounterId, mechanics);
        AddMechanics(boss, "additional_skills_without_warning", "effect", encounterId, mechanics);
        var cues = new List<PhasewardCue>();
        AddCues(boss, "players_notes", encounterId, cues);
        string[] community = Lines(boss, "players_notes")
            .Concat(Lines(boss, "community_notes")).Distinct().ToArray();
        string[] frenzy = Lines(boss, "frenzy_mmss");
        var encounter = new PhasewardEncounter(encounterId, nameOfBoss, instance, ids.ToArray(),
            mechanics.ToArray(), cues.ToArray(), community, frenzy);
        int priority = Str(boss, "source_kind").Equals("guide", StringComparison.OrdinalIgnoreCase)
            || mechanics.Count > 0 ? 2 : 1;
        foreach (int id in ids) AddCandidate(id, encounter, priority);
    }

    private static void AddMechanics(JsonElement boss, string key, string label,
        string encounterId, List<PhasewardMechanic> mechanics)
    {
        if (Arr(boss, key) is not JsonElement array) return;
        int index = 0;
        foreach (JsonElement skill in array.EnumerateArray())
        {
            if (skill.ValueKind != JsonValueKind.Object) continue;
            string kind = Str(skill, "kind");
            if (kind == "community_note" || kind == "community_player_note" || kind == "player_strategy")
                continue;
            string name = Str(skill, "name");
            if (string.IsNullOrWhiteSpace(name)) continue;
            double? cast = Num(skill, "cast_seconds");
            double? cooldown = Num(skill, "cooldown_seconds");
            if (cast is < 0 or > 3600 || cooldown is < 0 or > 3600)
                throw new InvalidDataException($"Invalid Phaseward timing for {name}");
            var ids = new HashSet<uint>();
            foreach (string skillKey in new[] { "skill_ids", "linked_skills" })
            {
                if (Arr(skill, skillKey) is not JsonElement links) continue;
                foreach (JsonElement entry in links.EnumerateArray())
                {
                    JsonElement id = entry;
                    if (entry.ValueKind == JsonValueKind.Object)
                        id = Prop(entry, "id") ?? Prop(entry, "skill_id") ?? default;
                    if (ToUint(id) is uint value) ids.Add(value);
                }
            }
            // Source-matched client IDs are *candidates*, not packet-verified. An
            // observed matching decoded hit is recorded by PhasewardEngine separately.
            string mechanicId = Str(skill, "id", $"{encounterId}/{label}/{index}");
            string[] bossLines = Lines(skill, "boss_lines");
            double[] hpThresholds = Numbers(skill, "hp_thresholds_reported")
                .Where(value => value is > 0 and <= 100).Distinct().OrderByDescending(value => value).ToArray();
            string[] notes = Lines(skill, "guide_warnings")
                .Concat(Lines(skill, "effects")).Distinct().ToArray();
            mechanics.Add(new PhasewardMechanic(mechanicId, name, ids.ToArray(), cast, cooldown,
                bossLines, hpThresholds, notes));
            index++;
        }
    }

    private static void AddCues(JsonElement boss, string key, string encounterId,
        List<PhasewardCue> cues)
    {
        if (Arr(boss, key) is not JsonElement array) return;
        int index = 0;
        foreach (JsonElement cue in array.EnumerateArray())
        {
            if (cue.ValueKind != JsonValueKind.Object) continue;
            double[] hp = Numbers(cue, "hp_thresholds_reported")
                .Where(value => value is > 0 and <= 100).Distinct().OrderByDescending(value => value).ToArray();
            string[] speech = Lines(cue, "boss_lines");
            if (hp.Length == 0 && speech.Length == 0) continue;
            string name = Str(cue, "name", $"Mechanic {index + 1}");
            string id = Str(cue, "id", $"{encounterId}/cue/{index}");
            string[] details = Lines(cue, "text")
                .Concat(Lines(cue, "effects")).Distinct().ToArray();
            cues.Add(new PhasewardCue(id, name, hp, speech, details,
                Str(cue, "source_evidence")));
            index++;
        }
    }

    private void AddCandidate(int id, PhasewardEncounter value, int priority)
    {
        if (!_candidates.TryGetValue(id, out var list)) _candidates[id] = list = [];
        list.Add(new Candidate(value, priority));
    }

    private void FinishIndex()
    {
        foreach (var (id, candidates) in _candidates)
        {
            int priority = candidates.Max(c => c.Priority);
            var best = candidates.Where(c => c.Priority == priority)
                .GroupBy(c => c.Encounter.Id, StringComparer.Ordinal)
                .Select(g => g.OrderByDescending(c => c.Encounter.Mechanics.Length).First())
                .ToArray();
            if (best.Length == 1) _byNpc[id] = best[0].Encounter;
            else _ambiguousNpcIds.Add(id);
        }

        foreach (IGrouping<string, Candidate> nameGroup in _candidates.Values
                     .SelectMany(candidates => candidates)
                     .Where(candidate => NormalizeBossName(candidate.Encounter.Name).Length > 0)
                     .GroupBy(candidate => NormalizeBossName(candidate.Encounter.Name),
                         StringComparer.OrdinalIgnoreCase))
        {
            int priority = nameGroup.Max(candidate => candidate.Priority);
            PhasewardEncounter[] best = nameGroup
                .Where(candidate => candidate.Priority == priority)
                .GroupBy(candidate => candidate.Encounter.Id, StringComparer.Ordinal)
                .Select(group => group
                    .OrderByDescending(candidate => candidate.Encounter.Mechanics.Length)
                    .First().Encounter)
                .ToArray();

            if (best.Length == 1)
                _byName[nameGroup.Key] = best[0];
        }
    }

    private static string NormalizeBossName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        string value = name.Trim();
        if (value.StartsWith("Hero ", StringComparison.OrdinalIgnoreCase))
            value = value[5..].TrimStart();

        int levelSuffix = value.IndexOf(" Lv ", StringComparison.OrdinalIgnoreCase);
        if (levelSuffix > 0)
            value = value[..levelSuffix];

        return string.Join(' ', value.Split(
            [' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool Has(JsonElement obj, string key) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(key, out _);
    private static JsonElement? Prop(JsonElement obj, string key) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(key, out var value) ? value : null;
    private static JsonElement? Arr(JsonElement obj, string key) =>
        Prop(obj, key) is JsonElement value && value.ValueKind == JsonValueKind.Array ? value : null;
    private static string Str(JsonElement obj, string key, string fallback = "") =>
        Prop(obj, key) is JsonElement value && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? fallback : fallback;
    private static double? Num(JsonElement obj, string key)
    {
        if (Prop(obj, key) is not JsonElement v) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out double d) && double.IsFinite(d)) return d;
        if (v.ValueKind == JsonValueKind.String &&
            double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double n)
            && double.IsFinite(n)) return n;
        return null;
    }
    private static int? PosInt(JsonElement obj, string key) =>
        Prop(obj, key) is JsonElement value ? ToInt(value) : null;
    private static int? ToInt(JsonElement value) =>
        ToUint(value) is uint n && n <= int.MaxValue ? (int)n : null;
    private static uint? ToUint(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetUInt32(out uint n) && n > 0) return n;
        if (value.ValueKind == JsonValueKind.String &&
            uint.TryParse(value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out uint v) && v > 0)
            return v;
        return null;
    }
    private static string[] Lines(JsonElement obj, string key)
    {
        if (Arr(obj, key) is not JsonElement array) return [];
        var list = new List<string>();
        foreach (JsonElement item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                list.Add(item.GetString()!);
            else if (item.ValueKind == JsonValueKind.Object)
            {
                string title = Str(item, "title", Str(item, "name"));
                string details = Str(item, "description");
                if (Arr(item, "text") is JsonElement parts)
                    details = string.Join(" ", parts.EnumerateArray()
                        .Where(x => x.ValueKind == JsonValueKind.String)
                        .Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)));
                else details = Str(item, "text", details);
                string combined = string.Join(" — ", new[] { title, details }
                    .Where(x => !string.IsNullOrWhiteSpace(x)));
                if (!string.IsNullOrWhiteSpace(combined)) list.Add(combined);
            }
        }
        return list.ToArray();
    }

    private static double[] Numbers(JsonElement obj, string key)
    {
        if (Arr(obj, key) is not JsonElement array) return [];
        var values = new List<double>();
        foreach (JsonElement item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Number && item.TryGetDouble(out double number) &&
                double.IsFinite(number))
            {
                values.Add(number);
            }
            else if (item.ValueKind == JsonValueKind.String &&
                     double.TryParse(item.GetString(), NumberStyles.Float,
                         CultureInfo.InvariantCulture, out double parsed) &&
                     double.IsFinite(parsed))
            {
                values.Add(parsed);
            }
        }
        return values.ToArray();
    }
}
