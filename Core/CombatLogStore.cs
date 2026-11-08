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
    public byte? RawDirectionCode { get; set; }
    public bool IsDot { get; set; }
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
    public long? DamageTaken { get; set; }
    public long? BossDamageTaken { get; set; }
    // Nexforge Damage Taken Log Timeline v0.2 — new logs have hit timestamps; legacy files have an empty list.
    public List<IncomingDamageSample>? IncomingDamageEvents { get; set; } = new();
    public int? DeathCount { get; set; }
    public int? FrontHits { get; set; }
    public int? BackHits { get; set; }
    public int? UnknownHits { get; set; }
    public List<StoredSkillEvent> Events { get; set; } = new();
}

public sealed class StoredEncounterMob
{
    public int TemplateId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
}

public sealed class CombatLogEntry
{
    public int SchemaVersion { get; set; } = 4;
    public string Id { get; set; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset EndedAtUtc { get; set; }
    public bool Completed { get; set; }
    public bool IsFavorite { get; set; }
    public string Notes { get; set; } = string.Empty;
    public string MobName { get; set; } = string.Empty;
    public ushort ServerId { get; set; }
    public string ServerName { get; set; } = string.Empty;
    public double Duration { get; set; }
    public long MobMaxHp { get; set; }
    public int MobTemplateId { get; set; }
    public bool IsBossEncounter { get; set; }
    public string BossId { get; set; } = string.Empty;
    public int? EnrageSeconds { get; set; }
    public double BossStartOffset { get; set; }
    public double BossDuration { get; set; }
    public Dictionary<string, string> LeaderboardSubmissionIds { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
    public List<StoredEncounterMob> Mobs { get; set; } = new();
    public List<StoredPlayerStat> Players { get; set; } = new();

    public long TotalDamage => Players.Sum(player => player.Damage);
    public long TotalHealing => Players.Sum(player => player.Healing);
    public long TotalDamageTaken => Players.Sum(player => player.DamageTaken ?? 0);

    public MeterSnapshot ToSnapshot(bool bossOnly = false)
    {
        bool hasTargetClassification = Players.Any(player =>
            player.Events.Any(skillEvent => !skillEvent.IsHeal &&
                (skillEvent.TargetRuntimeId != 0 || skillEvent.TargetTemplateId != 0)));

        List<PlayerStat> players = Players.Select(player => new PlayerStat
        {
            Name = player.Name,
            DeathCount = player.DeathCount,
            FrontHits = player.FrontHits,
            BackHits = player.BackHits,
            UnknownHits = player.UnknownHits,
            ClassId = player.ClassId,
            ClassIcon = SavedClassIconResolver.Resolve(player.ClassId, player.ClassName),
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
            DamageTaken = bossOnly && IsBossEncounter ? player.BossDamageTaken : player.DamageTaken,
            BossDamageTaken = player.BossDamageTaken,
            IncomingDamageEvents = (player.IncomingDamageEvents ?? new List<IncomingDamageSample>())
                .Where(e => !bossOnly || !IsBossEncounter ||
                    (e.IsBossSource && e.Time >= BossStartOffset))
                .Select(e => bossOnly && IsBossEncounter
                    ? new IncomingDamageSample(Math.Max(0, e.Time - BossStartOffset), e.Amount, true)
                    : e).ToArray(),
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
                    RawDirectionCode = skillEvent.RawDirectionCode,
                    IsDot = skillEvent.IsDot,
                })
                .ToList(),
        }).ToList();

        return new MeterSnapshot
        {
            Players = players,
            Mobs = (Mobs ?? new List<StoredEncounterMob>()).Select(mob => new EncounterMob
            {
                TemplateId = mob.TemplateId,
                Name = mob.Name,
                Count = mob.Count,
            }).ToList(),
            ServerId = ServerId,
            ServerName = ServerName,
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
        };
    }
}

public sealed record CombatLogPage(
    IReadOnlyList<CombatLogEntry> Entries,
    int NextOffset,
    int TotalCount);

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
                (player.DamageTaken ?? 0) == 0 &&
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
            ServerId = snapshot.ServerId,
            ServerName = snapshot.ServerName,
            Duration = snapshot.Duration,
            MobMaxHp = snapshot.MobMaxHp,
            MobTemplateId = snapshot.MobTemplateId,
            IsBossEncounter = snapshot.IsBossEncounter,
            BossId = snapshot.BossId,
            EnrageSeconds = snapshot.EnrageSeconds,
            BossStartOffset = snapshot.BossStartOffset,
            BossDuration = snapshot.BossDuration,
            Mobs = snapshot.Mobs.Select(mob => new StoredEncounterMob
            {
                TemplateId = mob.TemplateId,
                Name = mob.Name,
                Count = mob.Count,
            }).ToList(),
            Players = snapshot.Players.Select(player => new StoredPlayerStat
            {
                Name = player.Name,
                DeathCount = player.DeathCount,
                FrontHits = player.FrontHits,
                BackHits = player.BackHits,
                UnknownHits = player.UnknownHits,
                ClassId = player.ClassId,
                ClassName = player.ClassName,
                ClassColor = player.ClassColor,
                Damage = player.Damage,
                BossDamage = player.BossDamage,
                Healing = player.Healing,
                BossHealing = player.BossHealing,
                DamageTaken = player.DamageTaken,
                BossDamageTaken = player.BossDamageTaken,
                IncomingDamageEvents = player.IncomingDamageEvents
                    .Where(e => e.Amount > 0 && double.IsFinite(e.Time) && e.Time >= 0)
                    .TakeLast(MaxEventsPerPlayerInLog).ToList(),
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
                    RawDirectionCode = skillEvent.RawDirectionCode,
                    IsDot = skillEvent.IsDot,
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
            ex is IOException or UnauthorizedAccessException or JsonException or
                InvalidDataException or System.Security.Cryptography.CryptographicException)
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

                foreach (string path in EnumerateLogFiles())
                {
                    try
                    {
                        CombatLogEntry? entry = ReadEntryLocked(path);
                        if (entry is null)
                            continue;

                        entries.Add(entry);
                    }
                    catch (Exception ex) when (
                        ex is IOException or UnauthorizedAccessException or JsonException or
                            InvalidDataException or System.Security.Cryptography.CryptographicException)
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

    public CombatLogPage LoadPage(int offset, int pageSize)
    {
        int safeOffset = Math.Max(0, offset);
        int safePageSize = Math.Clamp(pageSize, 1, 500);
        var entries = new List<CombatLogEntry>(safePageSize);

        try
        {
            lock (_sync)
            {
                if (!Directory.Exists(LogDirectory))
                    return new CombatLogPage(entries, 0, 0);

                List<string> files = EnumeratePreferredLogFiles()
                    .OrderByDescending(
                        path => Path.GetFileNameWithoutExtension(path),
                        StringComparer.OrdinalIgnoreCase)
                    .ToList();

                List<string> pageFiles = files
                    .Skip(safeOffset)
                    .Take(safePageSize)
                    .ToList();

                foreach (string path in pageFiles)
                {
                    try
                    {
                        CombatLogEntry? entry = ReadEntryLocked(path);
                        if (entry is not null)
                            entries.Add(entry);
                    }
                    catch (Exception ex) when (
                        ex is IOException or UnauthorizedAccessException or JsonException or
                            InvalidDataException or System.Security.Cryptography.CryptographicException)
                    {
                        DebugConsole.Log(
                            "combatlog",
                            $"Sérült vagy nem olvasható fight log kihagyva: " +
                            $"{Path.GetFileName(path)} | {ex.Message}");
                    }
                }

                return new CombatLogPage(
                    entries.OrderByDescending(entry => entry.EndedAtUtc).ToList(),
                    safeOffset + pageFiles.Count,
                    files.Count);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DebugConsole.Log("combatlog", $"A Combat Log nem olvasható: {ex.Message}");
            return new CombatLogPage(entries, safeOffset, safeOffset);
        }
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
                entry.SchemaVersion = 4;
                WriteEntryLocked(entry);
                File.Delete(Path.Combine(LogDirectory, entry.Id + ".json"));
            }

            NotifyChanged();
            return true;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException or JsonException or
                InvalidDataException or System.Security.Cryptography.CryptographicException)
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
                string nslPath = Path.Combine(LogDirectory, id + ".nsl");
                if (!File.Exists(path) && !File.Exists(nslPath))
                    return false;

                File.Delete(path);
                File.Delete(nslPath);
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or
                                   InvalidDataException or System.Security.Cryptography.CryptographicException)
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
        string path = Path.Combine(LogDirectory, entry.Id + ".nsl");
        string temporaryPath = path + ".tmp";
        NslCombatLogCodec.Write(temporaryPath, entry);
        File.Move(temporaryPath, path, overwrite: true);
    }

    private static CombatLogEntry? ReadEntryLocked(string path)
    {
        bool legacyJson = string.Equals(
            Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase);
        CombatLogEntry? entry = legacyJson
            ? JsonSerializer.Deserialize<CombatLogEntry>(File.ReadAllText(path), JsonOptions)
            : NslCombatLogCodec.Read(path);

        if (entry is null || string.IsNullOrWhiteSpace(entry.Id))
            return null;

        entry.Players ??= new List<StoredPlayerStat>();
        entry.Mobs ??= new List<StoredEncounterMob>();
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
            return null;
        }

        if (legacyJson)
        {
            entry.SchemaVersion = 4;
            WriteEntryLocked(entry);
            File.Delete(path);
        }

        return entry;
    }

    private void ApplyRetentionLimitLocked(int limit)
    {
        if (limit <= 0)
            return;

        var entries = new List<(string Path, CombatLogEntry Entry, long Length)>();
        foreach (string path in EnumerateLogFiles())
        {
            try
            {
                CombatLogEntry? entry = string.Equals(
                    Path.GetExtension(path), ".nsl", StringComparison.OrdinalIgnoreCase)
                    ? NslCombatLogCodec.Read(path)
                    : JsonSerializer.Deserialize<CombatLogEntry>(File.ReadAllText(path), JsonOptions);
                if (entry is not null)
                    entries.Add((path, entry, new FileInfo(path).Length));
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException or JsonException or
                    InvalidDataException or System.Security.Cryptography.CryptographicException)
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

    private static IEnumerable<string> EnumerateLogFiles()
    {
        return Directory.EnumerateFiles(LogDirectory, "*.nsl")
            .Concat(Directory.EnumerateFiles(LogDirectory, "*.json"));
    }

    private static IEnumerable<string> EnumeratePreferredLogFiles()
    {
        return EnumerateLogFiles()
            .GroupBy(
                path => Path.GetFileNameWithoutExtension(path),
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.FirstOrDefault(path =>
                string.Equals(Path.GetExtension(path), ".nsl", StringComparison.OrdinalIgnoreCase))
                ?? group.First());
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
