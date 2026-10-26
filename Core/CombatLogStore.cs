using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AION_2_Companion.Core.Parsers;
using AION_2_Companion.Core.GameData;
using AION_2_Companion.Views.Overlays;

namespace AION_2_Companion.Core;

public sealed class StoredSkillEvent
{
    public double Time { get; set; }
    public string Skill { get; set; } = string.Empty;
    public string SkillIcon { get; set; } = string.Empty;
    public long Amount { get; set; }
    public bool Crit { get; set; }
    public bool IsHeal { get; set; }
    public uint TargetRuntimeId { get; set; }
    public int TargetTemplateId { get; set; }
    public bool IsBossTarget { get; set; }
}

public sealed class StoredPlayerStat
{
    public string Name { get; set; } = string.Empty;
    public int ClassId { get; set; }
    public string ClassName { get; set; } = string.Empty;
    public string ClassColor { get; set; } = "#9CA3AF";
    public long Damage { get; set; }
    public long BossDamage { get; set; }
    public long Healing { get; set; }
    public long BossHealing { get; set; }
    public List<StoredSkillEvent> Events { get; set; } = new();
}

public sealed class CombatLogEntry
{
    public int SchemaVersion { get; set; } = 3;
    public string Id { get; set; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset EndedAtUtc { get; set; }
    public bool Completed { get; set; }
    public bool IsFavorite { get; set; }
    public string Notes { get; set; } = string.Empty;
    public string MobName { get; set; } = string.Empty;
    public double Duration { get; set; }
    public long MobMaxHp { get; set; }
    public int MobTemplateId { get; set; }
    public bool IsBossEncounter { get; set; }
    public string BossId { get; set; } = string.Empty;
    public int? EnrageSeconds { get; set; }
    public double BossStartOffset { get; set; }
    public double BossDuration { get; set; }
    public List<StoredPlayerStat> Players { get; set; } = new();

    public long TotalDamage => Players.Sum(player => player.Damage);
    public long TotalHealing => Players.Sum(player => player.Healing);

    public MeterSnapshot ToSnapshot(bool bossOnly = false)
    {
        bool hasTargetClassification = Players.Any(player =>
            player.Events.Any(skillEvent => !skillEvent.IsHeal &&
                (skillEvent.TargetRuntimeId != 0 || skillEvent.TargetTemplateId != 0)));

        List<PlayerStat> players = Players.Select(player => new PlayerStat
        {
            Name = player.Name,
            ClassId = player.ClassId,
            ClassName = player.ClassName,
            ClassColor = string.IsNullOrWhiteSpace(player.ClassColor)
                ? "#9CA3AF"
                : player.ClassColor,
            Duration = bossOnly && IsBossEncounter && BossDuration > 0
                ? BossDuration
                : Duration,
            Damage = bossOnly && IsBossEncounter
                ? hasTargetClassification ? player.BossDamage : player.Damage
                : player.Damage,
            BossDamage = player.BossDamage,
            Healing = bossOnly && IsBossEncounter && hasTargetClassification
                ? player.BossHealing
                : player.Healing,
            BossHealing = player.BossHealing,
            Events = (player.Events ?? new List<StoredSkillEvent>())
                .Where(skillEvent => !bossOnly || !IsBossEncounter ||
                    skillEvent.Time >= BossStartOffset &&
                    (skillEvent.IsHeal || !hasTargetClassification || skillEvent.IsBossTarget))
                .Select(skillEvent => new SkillEvent
                {
                    Time = bossOnly && IsBossEncounter
                        ? Math.Max(0, skillEvent.Time - BossStartOffset)
                        : skillEvent.Time,
                    Skill = skillEvent.Skill,
                    SkillIcon = skillEvent.SkillIcon,
                    Amount = skillEvent.Amount,
                    Crit = skillEvent.Crit,
                    IsHeal = skillEvent.IsHeal,
                    TargetRuntimeId = skillEvent.TargetRuntimeId,
                    TargetTemplateId = skillEvent.TargetTemplateId,
                    IsBossTarget = skillEvent.IsBossTarget,
                })
                .ToList(),
        }).ToList();

        return new MeterSnapshot
        {
            Players = players,
            Duration = bossOnly && IsBossEncounter && BossDuration > 0
                ? BossDuration
                : Duration,
            MobMaxHp = MobMaxHp,
            MobTemplateName = MobName,
            MobTemplateId = MobTemplateId,
            IsBossEncounter = IsBossEncounter,
            BossId = BossId,
            EnrageSeconds = EnrageSeconds,
            EnrageRemainingSeconds = EnrageSeconds.HasValue
                ? EnrageSeconds.Value - (BossDuration > 0 ? BossDuration : Duration)
                : null,
            BossStartOffset = bossOnly ? 0 : BossStartOffset,
            BossDuration = BossDuration,
            IsDemo = false,
        };
    }
}

public sealed class CombatLogStore
{
    private const int MaxEventsPerPlayerInLog = 20_000;
    private const long MaxLogStorageBytes = 256L * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly object _sync = new();

    public static CombatLogStore Shared { get; } = new();

    public static string LogDirectory { get; } = Path.Combine(
        CompanionSettings.SettingsDirectory,
        "combat-logs");

    public event EventHandler? Changed;
    public DateTimeOffset SessionStartedAtUtc { get; private set; } = DateTimeOffset.UtcNow;

    public void StartSession() => SessionStartedAtUtc = DateTimeOffset.UtcNow;

    public CombatLogEntry? Save(
        MeterSnapshot snapshot,
        DateTimeOffset startedAtUtc,
        DateTimeOffset endedAtUtc,
        bool completed)
    {
        if (snapshot.Players.Count == 0 ||
            snapshot.Players.All(player =>
                player.Damage == 0 &&
                player.Healing == 0 &&
                player.Events.Count == 0))
        {
            return null;
        }

        var entry = new CombatLogEntry
        {
            Id = $"{endedAtUtc:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}"[..34],
            StartedAtUtc = startedAtUtc,
            EndedAtUtc = endedAtUtc,
            Completed = completed,
            MobName = string.IsNullOrWhiteSpace(snapshot.MobTemplateName)
                ? "Ismeretlen ellenfél"
                : snapshot.MobTemplateName,
            Duration = snapshot.Duration,
            MobMaxHp = snapshot.MobMaxHp,
            MobTemplateId = snapshot.MobTemplateId,
            IsBossEncounter = snapshot.IsBossEncounter,
            BossId = snapshot.BossId,
            EnrageSeconds = snapshot.EnrageSeconds,
            BossStartOffset = snapshot.BossStartOffset,
            BossDuration = snapshot.BossDuration,
            Players = snapshot.Players.Select(player => new StoredPlayerStat
            {
                Name = player.Name,
                ClassId = player.ClassId,
                ClassName = player.ClassName,
                ClassColor = player.ClassColor,
                Damage = player.Damage,
                BossDamage = player.BossDamage,
                Healing = player.Healing,
                BossHealing = player.BossHealing,
                Events = player.Events.TakeLast(MaxEventsPerPlayerInLog).Select(skillEvent => new StoredSkillEvent
                {
                    Time = skillEvent.Time,
                    Skill = skillEvent.Skill,
                    SkillIcon = skillEvent.SkillIcon,
                    Amount = skillEvent.Amount,
                    Crit = skillEvent.Crit,
                    IsHeal = skillEvent.IsHeal,
                    TargetRuntimeId = skillEvent.TargetRuntimeId,
                    TargetTemplateId = skillEvent.TargetTemplateId,
                    IsBossTarget = skillEvent.IsBossTarget,
                }).ToList(),
            }).ToList(),
        };

        try
        {
            lock (_sync)
            {
                Directory.CreateDirectory(LogDirectory);

                WriteEntryLocked(entry);
                ApplyRetentionLimitLocked(CompanionSettings.CombatLogLimit);
            }

            DebugConsole.Log(
                "combatlog",
                $"Fight elmentve: {entry.MobName} | {entry.Duration:F1}s | " +
                $"Damage={entry.TotalDamage} | Heal={entry.TotalHealing}");

            NotifyChanged();
            return entry;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or JsonException)
        {
            DebugConsole.Log("combatlog", $"A fight nem menthető: {ex.Message}");
            return null;
        }
    }

    public IReadOnlyList<CombatLogEntry> LoadAll()
    {
        var entries = new List<CombatLogEntry>();

        try
        {
            lock (_sync)
            {
                if (!Directory.Exists(LogDirectory))
                    return entries;

                foreach (string path in Directory.EnumerateFiles(LogDirectory, "*.json"))
                {
                    try
                    {
                        string json = File.ReadAllText(path);
                        CombatLogEntry? entry =
                            JsonSerializer.Deserialize<CombatLogEntry>(json, JsonOptions);

                        if (entry is null || string.IsNullOrWhiteSpace(entry.Id))
                            continue;

                        entry.Players ??= new List<StoredPlayerStat>();
                        foreach (StoredPlayerStat player in entry.Players)
                            player.Events ??= new List<StoredSkillEvent>();

                        BossDefinition? boss = BossCatalog.Find(entry.MobTemplateId, entry.MobName);
                        if (boss is not null)
                        {
                            entry.IsBossEncounter = true;
                            entry.BossId = string.IsNullOrWhiteSpace(entry.BossId) ? boss.Id : entry.BossId;
                            entry.MobMaxHp = entry.MobMaxHp > 0 ? entry.MobMaxHp : boss.MaxHp;
                            entry.EnrageSeconds ??= boss.EnrageSeconds;
                        }

                        if (entry.Players.Any(player => player.Events.Any(skillEvent =>
                                !skillEvent.IsHeal &&
                                skillEvent.Amount > DamageScanner.MaxAcceptedDamage)))
                        {
                            DebugConsole.Log(
                                "combatlog",
                                $"Hibás, irreális találatot tartalmazó fight elrejtve: " +
                                Path.GetFileName(path));
                            continue;
                        }

                        entries.Add(entry);
                    }
                    catch (Exception ex) when (
                        ex is IOException or UnauthorizedAccessException or JsonException)
                    {
                        DebugConsole.Log(
                            "combatlog",
                            $"Sérült vagy nem olvasható fight log kihagyva: " +
                            $"{Path.GetFileName(path)} | {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DebugConsole.Log("combatlog", $"A Combat Log nem olvasható: {ex.Message}");
        }

        return entries
            .OrderByDescending(entry => entry.EndedAtUtc)
            .GroupBy(entry => new
            {
                entry.MobName,
                StartedSecond = entry.StartedAtUtc.ToUnixTimeSeconds(),
                EndedSecond = entry.EndedAtUtc.ToUnixTimeSeconds(),
                entry.TotalDamage,
                entry.TotalHealing,
            })
            .Select(group => group.First())
            .ToList();
    }

    public bool UpdateMetadata(CombatLogEntry entry)
    {
        if (!IsValidId(entry.Id))
            return false;

        try
        {
            lock (_sync)
            {
                Directory.CreateDirectory(LogDirectory);
                entry.SchemaVersion = 3;
                WriteEntryLocked(entry);
            }

            NotifyChanged();
            return true;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or JsonException)
        {
            DebugConsole.Log("combatlog", $"A fight adatai nem menthetők: {ex.Message}");
            return false;
        }
    }

    public bool Delete(string id)
    {
        if (!IsValidId(id))
            return false;

        try
        {
            lock (_sync)
            {
                string path = Path.Combine(LogDirectory, id + ".json");
                if (!File.Exists(path))
                    return false;

                File.Delete(path);
            }

            NotifyChanged();
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DebugConsole.Log("combatlog", $"A fight nem törölhető: {ex.Message}");
            return false;
        }
    }

    public void ApplyRetentionLimit()
    {
        try
        {
            lock (_sync)
            {
                if (!Directory.Exists(LogDirectory))
                    return;

                ApplyRetentionLimitLocked(CompanionSettings.CombatLogLimit);
            }

            NotifyChanged();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            DebugConsole.Log("combatlog", $"A history limit nem alkalmazható: {ex.Message}");
        }
    }

    private static bool IsValidId(string id) =>
        !string.IsNullOrWhiteSpace(id) &&
        string.Equals(id, Path.GetFileName(id), StringComparison.Ordinal) &&
        id.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    private static void WriteEntryLocked(CombatLogEntry entry)
    {
        string path = Path.Combine(LogDirectory, entry.Id + ".json");
        string temporaryPath = path + ".tmp";
        string json = JsonSerializer.Serialize(entry, JsonOptions);

        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, path, overwrite: true);
    }

    private void ApplyRetentionLimitLocked(int limit)
    {
        if (limit <= 0)
            return;

        var entries = new List<(string Path, CombatLogEntry Entry, long Length)>();
        foreach (string path in Directory.EnumerateFiles(LogDirectory, "*.json"))
        {
            try
            {
                CombatLogEntry? entry = JsonSerializer.Deserialize<CombatLogEntry>(
                    File.ReadAllText(path), JsonOptions);
                if (entry is not null)
                    entries.Add((path, entry, new FileInfo(path).Length));
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException or JsonException)
            {
                DebugConsole.Log(
                    "combatlog",
                    $"A history limit kihagyott egy sérült logot: {Path.GetFileName(path)} | {ex.Message}");
            }
        }

        int excess = Math.Max(0, entries.Count - limit);
        long totalBytes = entries.Sum(item => item.Length);
        var deleted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var oldEntry in entries
                     .Where(item => !item.Entry.IsFavorite)
                     .OrderBy(item => item.Entry.EndedAtUtc))
        {
            bool overCount = deleted.Count < excess;
            bool overSize = totalBytes > MaxLogStorageBytes;
            if (!overCount && !overSize)
                break;

            File.Delete(oldEntry.Path);
            deleted.Add(oldEntry.Path);
            totalBytes -= oldEntry.Length;
        }
    }

    private void NotifyChanged()
    {
        try
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            DebugConsole.Log("combatlog", $"A Combat Log nézet nem frissült: {ex.Message}");
        }
    }
}
