using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using AION_2_Companion.Views.Overlays;
using AION_2_Companion.Core.GameData;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AION_2_Companion.Core;

namespace AION_2_Companion.ViewModels;
using System.Collections.ObjectModel;
using System.Globalization;
using AION_2_Companion.Core.Wire;
using CommunityToolkit.Mvvm.Input;
using AION_2_Companion.Core.Capture;
using AION_2_Companion.Core.Parsers;
using SharpPcap;
using Avalonia.Threading;
using AION_2_Companion.Core.Map.Pets;
using AION_2_Companion.Core.Accounts;
using AION_2_Companion.Core.Combat;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly EndpointTracker _endpointTracker = new();
    private readonly CaptureService _captureService = new();
    private TcpStreamDecoder? _tcpStreamDecoder;
    private long _receivedPackets;
    private long _pendingReceivedPackets;
    private bool _isSubscribed;
    private uint _SelfId = 0;
    private byte? _selfClassId;
    private readonly DispatcherTimer _meterTimer;
    private readonly DispatcherTimer _dpsOverlayHideTimer;
    private bool _autoManagedDpsOverlay;
    private long _fightEndTimestamp;
    private string _selfName = "";
    private string _currentMobName = string.Empty;
    private int _currentMobTemplateId;
    private BossDefinition? _currentBoss;
    private uint _currentBossRuntimeId;
    private long _bossStartTimestamp;
    private long _bossLastHitTimestamp;
    private readonly Dictionary<uint, uint> _mobHealthRaw = new();
    private MeterSnapshot _lastBossSnapshot = new();
    public static PetsDatabase? _ElyosPets;
    private readonly Dictionary<int, (int Level, int Soul)> _ownedPets = new();
    public static MobsDatabase? _mobsDatabaseStatic;
    private readonly HashSet<uint> _pendingSummons = new();
    private readonly CombatEventDeduplicator _combatEventDeduplicator = new();
    private string _characterName = LocalizationService.Get("WaitingForCharacter");
    private Bitmap? _selfClassIcon;
    private string _selfClassName = string.Empty;
    private string _selfClassColor = "#9CA3AF";
    private ForgeDataLoader _forgeDataLoader;
    private const double FightEndGraceSeconds = 8.5;

    private bool _fightEndPending;
    private long _fightEndRequestedAt;
    private long _fightLastHitTimestamp;

    private readonly HashSet<uint> _fightTargets = new();
    private readonly HashSet<uint> _deadFightTargets = new();

    // Ideiglenesen lezárt fightok. Egy másik mob sebzése NEM archiválja
    // az előzőt azonnal; 8,5s-ig vissza tudunk térni rá.
    private readonly List<PausedFightState> _pausedFights = new();
    private const int MaxPausedFights = 12;
    private sealed record SkillOwnerResult(
        PartyMemberEntry? Owner,
        bool Found,
        bool Ambiguous);
    private readonly Dictionary<uint, int>
        _KnowMobs = new();
    public string CharacterName
    {
        get => _characterName;
        private set
        {
            if (_characterName == value) return;
            _characterName = value;
            OnPropertyChanged(nameof(CharacterName));
        }
    }

    private string _characterClass = "—";
    public string CharacterClass
    {
        get => _characterClass;
        private set
        {
            if (_characterClass == value) return;
            _characterClass = value;
            OnPropertyChanged(nameof(CharacterClass));
        }
    }

    private string _characterClassColor = "#9CA3AF";
    public string CharacterClassColor
    {
        get => _characterClassColor;
        private set
        {
            if (_characterClassColor == value) return;
            _characterClassColor = value;
            OnPropertyChanged(nameof(CharacterClassColor));
        }
    }
    private sealed class PendingDamage
    {
        public DamageHit Hit { get; init; } = new();
        public long Timestamp { get; init; }
    }
    private void BufferPendingDamage(uint actorId, DamageHit hit)
{
    if (actorId == 0 || hit.Damage <= 0)
        return;

    if (!_pendingDamageByActor.TryGetValue(actorId, out var list))
    {
        if (_pendingDamageByActor.Count >= MaxPendingDamageActors)
        {
            uint oldestKey = _pendingDamageByActor.Keys.First();
            _pendingDamageByActor.Remove(oldestKey);
            
        }

        list = new List<PendingDamage>();
        _pendingDamageByActor[actorId] = list;
    }

    if (list.Count >= MaxPendingDamagePerActor)
    {
        return;
    }

    list.Add(new PendingDamage
    {
        Hit = hit,
        Timestamp = Stopwatch.GetTimestamp()
    });
}

private void ReplayPendingDamage(uint summonId, uint ownerId)
{
    // Csak ismert saját vagy party tulajdonoshoz rendelünk.
    if (ownerId == 0 || !IsKnownOwner(ownerId))
        return;

    if (!_pendingDamageByActor.Remove(summonId, out var pending))
        return;

    string ownerName = GetOwnerName(ownerId);

    DebugConsole.Log("damage",
        $"[DAMAGE REPLAY] Summon={summonId} | Owner={ownerId} " +
        $"({ownerName}) | Hits={pending.Count}");

    foreach (var entry in pending)
    {
        var hit = entry.Hit;

        // Védelem: a replay ugyanazokat az alapvető szűréseket
        // használja, de nem tároljuk el újra az ismeretlen találatokat.
        if (hit.Target == hit.Actor || hit.Damage <= 0)
            continue;

        bool isSelf = ownerId == _SelfId;
        bool isParty = PartyMembers.Any(p => p.Id == ownerId);

        if (!isSelf && !isParty)
            continue;

        DebugConsole.Log("damage",
            $"[DAMAGE RESTORED] PET ({summonId}) | " +
            $"Skill={hit.Skill} | " +
            $"{(hit.IsDot ? "DOT" : "Dmg")}={hit.Damage}" +
            $"{(hit.IsCritical ? " CRIT" : "")}");

        RecordDamage(
            ownerId,
            ownerName,
            hit, entry.Timestamp);
    }
}
    private readonly Dictionary<uint, List<PendingDamage>>
        _pendingDamageByActor = new();

    private const int MaxPendingDamageActors = 128;
    private const int MaxPendingDamagePerActor = 256;
    
    private sealed class PlayerDamageStats
    {
        public string Name { get; set; } = "";
        public long TotalDamage { get; set; }
        public long BossDamage { get; set; }
        public long TotalHealing { get; set; }
        public long BossHealing { get; set; }

        public byte? ClassId { get; set; }
        public string ClassName { get; set; } = "";
        public string ClassColor { get; set; } = "#9CA3AF";
        public Bitmap? ClassIcon { get; set; }

        public List<StoredHit> Hits { get; } = new();
        public List<SkillEvent> SnapshotEvents { get; } = new();
        public int SnapshotHitCount { get; set; }
        public string SnapshotClassName { get; set; } = string.Empty;
        public bool SnapshotNeedsSort { get; set; }
    }

    private sealed class PausedFightState
    {
        public required Dictionary<uint, PlayerDamageStats> Players { get; init; }
        public required HashSet<uint> Targets { get; init; }
        public required HashSet<uint> DeadTargets { get; init; }
        public required HashSet<uint> CombatMobs { get; init; }
        public required MeterSnapshot ArchiveSnapshot { get; init; }
        public required string? MobName { get; init; }
        public required int MobTemplateId { get; init; }
        public required string BossId { get; init; }
        public required uint BossRuntimeId { get; init; }
        public required long BossStartTimestamp { get; init; }
        public required long BossLastHitTimestamp { get; init; }
        public required long StartTimestamp { get; init; }
        public required long LastDamageTimestamp { get; init; }
        public required long LastHitTimestamp { get; init; }
        public required long PauseTimestamp { get; init; }
        public required long ExpiryTimestamp { get; init; }
    }

    // A damage/heal összeg ettől függetlenül mindig tovább számolódik. Ez csak
    // egy beragadt, órákig tartó fight részletes timeline-ját korlátozza.
    private const int MaxStoredCombatEventsPerPlayer = 50_000;

    private sealed class StoredHit
    {
        public long Timestamp { get; init; }
        public uint Skill { get; init; }
        public long Amount { get; init; }
        public bool Crit { get; init; }
        public bool IsDot { get; init; }
        public bool IsHeal { get; init; }
        public uint TargetRuntimeId { get; init; }
        public int TargetTemplateId { get; set; }
        public bool IsBossTarget { get; set; }
    }
    

    private readonly Dictionary<uint, PlayerDamageStats> _damageByPlayer = new();

    private long _fightStartTimestamp;
    private bool _fightActive;
    private long _lastDamageTimestamp;
    // Eredeti 8,5 másodperces timeout megtartva.
    private const double CombatInactivityTimeoutSeconds = 8.5;
    
    // Summon runtime ID -> tulajdonos runtime ID
    private readonly Dictionary<uint, uint> _summonOwners = new();
    private readonly HashSet<uint> _MobsInCombat = new();
    public string PartyName { get; private set; } = "Még nem észlelhető";
    
    public string LatestEvent { get; private set; } =
        "Várakozás az első dekódolt frame-re...";

    public string LatestOpcode { get; private set; } = "--";

    public string LatestConfidence { get; private set; } = "Nincs adat";

    public ObservableCollection<string> RecentFindings { get; } = new();
    //public ObservableCollection<string> PartyMembers { get; } = new();
    public ObservableCollection<PartyMemberEntry> PartyMembers { get; } = new();

    
    public sealed class PartyMemberEntry
    {
        public uint Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public byte? ClassId { get; set; }
        public string ClassName { get; set; } = string.Empty;
        public string ClassColor { get; set; } = "#9CA3AF";
        public Bitmap? ClassIcon { get; set; }
        public uint SpellPrefix { get; set; }
    }

    
    private void CombatStartCheck(WireFrame frame)
    {
        if (_SelfId == 0 || frame.Opcode != OpcodeId.EnterCombat ||
            !CombatStartFrameParser.TryParse(frame.Payload, out var start) ||
            start is null ||
            start.actorId != _SelfId)
            return;

        // EnterCombat önmagában nem bizonyít új sebzést, ezért nem
        // von vissza pending lezárást vagy soft Leave/Disengage jelzést.
        // Ezt kizárólag az ugyanarra a célpontra érkező elfogadott Hit teszi.

        _MobsInCombat.Add(start.targetId);
        TrackTargetMetadata(start.targetId);

        DebugConsole.Log("dpstime",
            $"[COMBAT ENTER] Target={start.targetId} | " +
            $"TargetsInFight={_fightTargets.Count} | Pending={_fightEndPending}");
    }

    private void CombatEndCheck(WireFrame frame)
    {
        Opcode opcode = frame.Opcode;
        if (opcode == OpcodeId.HP)
        {
            if (HpFrameParser.TryParse(frame.Payload, out var hp) && hp is not null && hp.Target != 0)
            {
                _mobHealthRaw[hp.Target] = hp.Health;
                TrackTargetMetadata(hp.Target);
                if (hp.Health == 0)
                    ConfirmCombatTargetDeath(hp.Target, "HP=0");
            }
            return;
        }

        // A Leave/Disengage bossmechanika alatt is érkezhet.
        // NEM indítunk vele lezárást, csak diagnosztikát.
        if (opcode == OpcodeId.LeaveCombat ||
            opcode == OpcodeId.DisengageCombat)
        {
            if (CombatEndFrameParser.TryParse(frame.Payload, out var entityId) &&
                _fightActive && _fightTargets.Contains(entityId))
            {
                DebugConsole.Log("dpstime",
                    $"[COMBAT SOFT SIGNAL] Target={entityId} | Opcode={opcode} | " +
                    "IgnoredAsFightEnd=True");
            }
        }
    }

    private (int TemplateId, string Name, BossDefinition? Boss) ResolveTarget(uint runtimeId)
    {
        if (!_KnowMobs.TryGetValue(runtimeId, out int templateId))
            return (0, string.Empty, null);

        string name = _forgeDataLoader._mobsDatabase?.GetById(templateId)?.Name ?? string.Empty;
        BossDefinition? boss = _forgeDataLoader._bossesDatabase?.Find(templateId, name);
        return (templateId, name, boss);
    }

    private void TrackTargetMetadata(uint runtimeId)
    {
        var target = ResolveTarget(runtimeId);
        if (target.TemplateId == 0)
            return;

        if (target.Boss is not null)
        {
            bool newlyDetected = _currentBoss is null;
            _currentBoss = target.Boss;
            _currentBossRuntimeId = runtimeId;
            _currentMobTemplateId = target.TemplateId;
            _currentMobName = target.Boss.LocalizedName(
                LocalizationService.CurrentLanguageCode,
                target.Name);
            ReclassifyBossTarget(runtimeId, target.TemplateId);

            if (newlyDetected && CompanionSettings.DpsEncounterMode == DpsEncounterMode.Bosses &&
                _fightActive && CompanionSettings.AutoManageDpsOverlay)
                ShowDpsOverlayForCombat();
            return;
        }

        if (_currentBoss is null && !string.IsNullOrWhiteSpace(target.Name))
        {
            _currentMobTemplateId = target.TemplateId;
            _currentMobName = target.Name;
        }
    }

    private void ReclassifyBossTarget(uint runtimeId, int templateId)
    {
        foreach (PlayerDamageStats stats in _damageByPlayer.Values)
        {
            bool changed = false;
            foreach (StoredHit hit in stats.Hits)
            {
                if (hit.IsHeal || hit.TargetRuntimeId != runtimeId || hit.IsBossTarget)
                    continue;

                hit.TargetTemplateId = templateId;
                hit.IsBossTarget = true;
                stats.BossDamage += hit.Amount;
                if (_bossStartTimestamp == 0 || hit.Timestamp < _bossStartTimestamp)
                    _bossStartTimestamp = hit.Timestamp;
                _bossLastHitTimestamp = Math.Max(_bossLastHitTimestamp, hit.Timestamp);
                changed = true;
            }

            if (changed)
            {
                stats.SnapshotEvents.Clear();
                stats.SnapshotHitCount = 0;
                stats.SnapshotClassName = "\0";
                stats.SnapshotNeedsSort = true;
            }
        }
    }

        private void CombatDeathCheck(WireFrame frame)
    {
        if (frame.Opcode != OpcodeId.Death)
            return;

        if (DeathFrameParser.TryParse(frame.Payload, out var entityId))
            ConfirmCombatTargetDeath(entityId, "Death");
    }

    private void ConfirmCombatTargetDeath(uint entityId, string signal)
    {
        // Csak az AKTUÁLIS fightban valóban sebzett target halála számít.
        if (!_fightActive || entityId == 0 || !_fightTargets.Contains(entityId))
            return;

        _deadFightTargets.Add(entityId);
        _MobsInCombat.Remove(entityId);

        DebugConsole.Log("dpstime",
            $"[COMBAT TARGET DEAD] Target={entityId} | Via={signal} | " +
            $"Confirmed={_deadFightTargets.Count}/{_fightTargets.Count}");

        // Egy add halála NEM zárhatja le az élő bosst.
        StopCombat();
    }
    
    private void CheckCombatTimeout()
    {
        long now = Stopwatch.GetTimestamp();
        FlushExpiredPausedFights(now);

        if (!_fightActive)
            return;

        // HP=0 / Death esetén az óra megállt, de 8,5s-ig újranyitható.
        if (_fightEndPending)
        {
            double pendingSeconds =
                (now - _fightEndRequestedAt) / (double)Stopwatch.Frequency;

            if (pendingSeconds >= FightEndGraceSeconds)
                FinalizeCombat("HpZeroOrDeathGraceExpired");
            return;
        }

        // Ha HP=0 packet elveszik, a 8,5s damage-idle védőháló.
        if (_lastDamageTimestamp == 0)
            return;

        double inactiveSeconds =
            (now - _lastDamageTimestamp) / (double)Stopwatch.Frequency;
        if (inactiveSeconds >= CombatInactivityTimeoutSeconds)
        {
            DebugConsole.Log("dpstime",
                $"[COMBAT IDLE] NoDamage={inactiveSeconds:F1}s | " +
                $"Targets={_fightTargets.Count}");
            FinalizeCombat("InactivityTimeout");
        }
    }

    private void StopCombat()
    {
        if (!_fightActive || _fightEndPending || _fightTargets.Count == 0)
            return;

        // Egy boss add halála nem zárja le az egész fightot.
        if (!_fightTargets.All(_deadFightTargets.Contains))
            return;

        _fightEndPending = true;
        _fightEndRequestedAt = Stopwatch.GetTimestamp();

        DebugConsole.Log("dpstime",
            $"[FIGHT PAUSED] Reason=AllDamagedTargetsHP0OrDeath | " +
            $"Targets={_fightTargets.Count} | " +
            $"RecoverableFor={FightEndGraceSeconds:F1}s");
    }

    // Másik mob első ütésekor eltesszük a régi fight TELJES állapotát.
    // A snapshot csak lejáratkor kerül Historyba. A mob ID ugyanaz marad.
    private void SuspendCurrentFight(string reason, long now)
    {
        if (!_fightActive || _damageByPlayer.Count == 0)
            return;

        if (!_fightEndPending)
        {
            _fightEndPending = true;
            _fightEndRequestedAt = now;
        }

        long pauseAt = _fightEndRequestedAt;
        long expiry = pauseAt +
            (long)(FightEndGraceSeconds * Stopwatch.Frequency);

        var state = new PausedFightState
        {
            Players = new Dictionary<uint, PlayerDamageStats>(_damageByPlayer),
            Targets = new HashSet<uint>(_fightTargets),
            DeadTargets = new HashSet<uint>(_deadFightTargets),
            CombatMobs = new HashSet<uint>(_MobsInCombat),
            ArchiveSnapshot = BuildSnapshot(),
            MobName = _currentMobName,
            MobTemplateId = _currentMobTemplateId,
            BossId = _currentBoss?.Id ?? string.Empty,
            BossRuntimeId = _currentBossRuntimeId,
            BossStartTimestamp = _bossStartTimestamp,
            BossLastHitTimestamp = _bossLastHitTimestamp,
            StartTimestamp = _fightStartTimestamp,
            LastDamageTimestamp = _lastDamageTimestamp,
            LastHitTimestamp = _fightLastHitTimestamp,
            PauseTimestamp = pauseAt,
            ExpiryTimestamp = expiry
        };
        _pausedFights.Add(state);

        DebugConsole.Log("dpstime",
            $"[FIGHT SUSPENDED] Reason={reason} | " +
            $"Targets={string.Join(",", state.Targets)} | " +
            $"Remaining={Math.Max(0, (expiry - now) / (double)Stopwatch.Frequency):F2}s");

        // A régi statisztikát még NEM mentjük, és nem is veszítjük el.
        _fightActive = false;
        _fightEndPending = false;
        _fightEndRequestedAt = 0;
        _fightStartTimestamp = 0;
        _fightLastHitTimestamp = 0;
        _fightEndTimestamp = 0;
        _lastDamageTimestamp = 0;
        _damageByPlayer.Clear();
        _fightTargets.Clear();
        _deadFightTargets.Clear();
        _MobsInCombat.Clear();
        _currentMobName = string.Empty;
        _currentMobTemplateId = 0;
        _currentBoss = null;
        _currentBossRuntimeId = 0;
        _bossStartTimestamp = 0;
        _bossLastHitTimestamp = 0;

        // Memóriakorlát extrém, sokmobszos szituációra.
        while (_pausedFights.Count > MaxPausedFights)
        {
            var oldest = _pausedFights[0];
            _pausedFights.RemoveAt(0);
            SavePausedFight(oldest, completed: true, "PauseBufferLimit");
        }
    }

    private bool TryResumePausedFight(uint targetId, long hitTimestamp, long now)
    {
        // A korábbi summon replayek nem új harci aktivitások!
        if (hitTimestamp < now - (long)(2.0 * Stopwatch.Frequency))
            return false;

        PausedFightState? previous = _pausedFights.LastOrDefault(p =>
            p.Targets.Contains(targetId) && now < p.ExpiryTimestamp &&
            hitTimestamp >= p.PauseTimestamp);
        if (previous is null)
            return false;

        // A közben indult másik fightot is eltesszük, így nem keverjük
        // az új mob sebzését a visszahozott boss sebzésével.
        if (_fightActive)
            SuspendCurrentFight("ReturnToEarlierTarget", now);

        _pausedFights.Remove(previous);

        _damageByPlayer.Clear();
        foreach (var (id, stats) in previous.Players)
            _damageByPlayer[id] = stats;
        _fightTargets.Clear();
        _fightTargets.UnionWith(previous.Targets);
        _deadFightTargets.Clear();
        _deadFightTargets.UnionWith(previous.DeadTargets);
        _MobsInCombat.Clear();
        _MobsInCombat.UnionWith(previous.CombatMobs);

        _fightStartTimestamp = previous.StartTimestamp;
        _fightLastHitTimestamp = previous.LastHitTimestamp;
        _lastDamageTimestamp = previous.LastDamageTimestamp;
        _fightEndTimestamp = 0;
        _fightActive = true;
        _fightEndPending = false;
        _fightEndRequestedAt = 0;
        _currentMobName = previous.MobName ?? string.Empty;
        _currentMobTemplateId = previous.MobTemplateId;
        _currentBoss = _forgeDataLoader._bossesDatabase?.Find(
            previous.MobTemplateId, previous.MobName);
        _currentBossRuntimeId = previous.BossRuntimeId;
        _bossStartTimestamp = previous.BossStartTimestamp;
        _bossLastHitTimestamp = previous.BossLastHitTimestamp;

        _deadFightTargets.Remove(targetId);  // téves HP=0 feloldása
        _MobsInCombat.Add(targetId);

        DebugConsole.Log("dpstime",
            $"[FIGHT RESTORED] Target={targetId} | " +
            $"OriginalStart={previous.StartTimestamp} | " +
            $"Players={previous.Players.Count}");
        return true;
    }

    private void FlushExpiredPausedFights(long now)
    {
        for (int i = _pausedFights.Count - 1; i >= 0; i--)
        {
            PausedFightState paused = _pausedFights[i];
            if (now < paused.ExpiryTimestamp)
                continue;

            _pausedFights.RemoveAt(i);
            SavePausedFight(paused, completed: true, "GraceExpired");
        }
    }

    private void SavePausedFight(PausedFightState paused, bool completed, string reason)
    {
        MeterSnapshot snapshot = paused.ArchiveSnapshot;
        DebugConsole.Log("dpstime",
            $"[FIGHT FINAL PAUSED] Reason={reason} | " +
            $"Targets={string.Join(",", paused.Targets)} | " +
            $"Duration={snapshot.Duration:F2}s");

        foreach (uint target in paused.Targets)
            _mobHealthRaw.Remove(target);

        if (snapshot.Duration <= 0 ||
            (snapshot.TotalDamage <= 0 && snapshot.TotalHealing <= 0))
            return;

        double ageSeconds = Math.Max(0,
            (Stopwatch.GetTimestamp() - paused.LastHitTimestamp) /
            (double)Stopwatch.Frequency);
        DateTimeOffset endedAt = DateTimeOffset.UtcNow -
            TimeSpan.FromSeconds(ageSeconds);
        CombatLogStore.Shared.Save(snapshot,
            endedAt - TimeSpan.FromSeconds(snapshot.Duration),
            endedAt, completed);

        if (snapshot.IsBossEncounter)
            _lastBossSnapshot = snapshot.ToBossOnly();

    }

        private void FinalizeCombat(string reason = "Unknown")
    {
        if (!_fightActive)
            return;

        _fightEndTimestamp = Math.Max(
            _fightStartTimestamp,
            _fightLastHitTimestamp);

        _fightActive = false;
        _fightEndPending = false;

        DebugConsole.Log("dpstime",
            $"[FIGHT FINAL] Reason={reason} | " +
            $"Duration={(double)(_fightEndTimestamp - _fightStartTimestamp) / Stopwatch.Frequency:F2}s | " +
            $"Targets={_fightTargets.Count}");

        MeterSnapshot finalSnapshot = BuildSnapshot();
        if (finalSnapshot.IsBossEncounter)
            _lastBossSnapshot = BuildSnapshot(bossOnly: true);

        SaveFightToCombatLog(completed: true);
        CountDps();

        foreach (uint target in _fightTargets)
            _mobHealthRaw.Remove(target);

        // A snapshot elkészítése UTÁN törlünk, hogy a boss nevét ne veszítsük el.
        _MobsInCombat.Clear();
        _deadFightTargets.Clear();
        ScheduleAutomaticDpsOverlayHide();
    }

    private void SaveFightToCombatLog(bool completed)
    {
        if (_damageByPlayer.Count == 0)
            return;

        MeterSnapshot snapshot = BuildSnapshot();
        if (snapshot.Duration <= 0 || snapshot.TotalDamage <= 0 && snapshot.TotalHealing <= 0)
            return;

        long endedTimestamp = _fightEndTimestamp > 0
            ? _fightEndTimestamp
            : Stopwatch.GetTimestamp();

        double secondsSinceFightEnd = Math.Max(
            0,
            (Stopwatch.GetTimestamp() - endedTimestamp) /
            (double)Stopwatch.Frequency);

        DateTimeOffset endedAtUtc =
            DateTimeOffset.UtcNow - TimeSpan.FromSeconds(secondsSinceFightEnd);
        DateTimeOffset startedAtUtc =
            endedAtUtc - TimeSpan.FromSeconds(snapshot.Duration);

        CombatLogStore.Shared.Save(
            snapshot,
            startedAtUtc,
            endedAtUtc,
            completed);
    }

    public void SaveActiveFightForShutdown()
    {
        // A szüneteltetett boss fightokat se veszítsük el leállításkor.
        foreach (PausedFightState paused in _pausedFights.ToArray())
            SavePausedFight(paused, completed: false, "ApplicationShutdown");
        _pausedFights.Clear();

        if (!_fightActive || _damageByPlayer.Count == 0)
            return;

        _fightEndTimestamp = Math.Max(
            _fightStartTimestamp,
            _fightLastHitTimestamp > 0
                ? _fightLastHitTimestamp
                : Stopwatch.GetTimestamp());

        _fightActive = false;
        _fightEndPending = false;
        SaveFightToCombatLog(completed: false);
    }

        private void ProcessDamage(WireFrame frame)
    {
        var opcode = frame.Opcode;

        if (opcode == OpcodeId.Hit)
            ApplyHits(frame.Time, DamageScanner.ScanFrame(0x04, 0x38, frame.Payload, minDamage: 1));
        else if (opcode == OpcodeId.DoT)
            ApplyHits(frame.Time, DamageScanner.ScanFrame(0x05, 0x38, frame.Payload, minDamage: 1));
        else if (opcode == OpcodeId.Characters)
            ApplyHits(frame.Time, DamageScanner.ScanFrame(0x0B, 0x39, frame.Payload, minDamage: 1));
    }

    private void ApplyHits(DateTimeOffset capturedAt, List<DamageHit> hits)
    {
        foreach (var hit in hits)
        {
            if (_combatEventDeduplicator.ShouldAccept(hit, capturedAt))
                ApplyDamageHit(hit);
        }
    }
    
    private void ApplyHealHit(DamageHit hit)
    {
        if (hit.Damage <= 0 || _SelfId == 0)
            return;

        uint originalActor = hit.Actor;

        uint ownerId = _summonOwners.TryGetValue(
            originalActor, out uint summonOwner)
            ? summonOwner
            : originalActor;

        bool isSelf = ownerId == _SelfId;

        PartyMemberEntry? member = PartyMembers.FirstOrDefault(
            p => p.Id != 0 && p.Id == ownerId);

        if (!isSelf && member is null)
            return;

        string ownerName = isSelf
            ? _selfName
            : member!.Name;

        RecordHealing(ownerId, ownerName, hit);
    }
    
    private void RecordHealing(uint ownerId, string ownerName, DamageHit hit, long? eventTimestamp = null)
    {
        long timestamp = eventTimestamp ?? Stopwatch.GetTimestamp();

        if (!_fightActive && !TryStartFightFromHealing(timestamp))
            return;

        if (hit.Damage <= 0)
            return;

        _lastDamageTimestamp = Math.Max(_lastDamageTimestamp, timestamp);
        _fightLastHitTimestamp = Math.Max(_fightLastHitTimestamp, timestamp);
        
        if (!_damageByPlayer.TryGetValue(ownerId, out var stats))
        {
            stats = new PlayerDamageStats
            {
                Name = ownerName
            };

            if (ownerId == _SelfId)
            {
                stats.ClassId = _selfClassId;
                stats.ClassName = _selfClassName;
                stats.ClassColor = _selfClassColor;
                stats.ClassIcon = _selfClassIcon;
            }
            else
            {
                var member = PartyMembers.FirstOrDefault(p => p.Id == ownerId);

                if (member != null)
                {
                    stats.ClassId = member.ClassId;
                    stats.ClassName = member.ClassName;
                    stats.ClassColor = member.ClassColor;
                    stats.ClassIcon = member.ClassIcon;
                }
            }

            _damageByPlayer[ownerId] = stats;
        }

        stats.TotalHealing += hit.Damage;
        if (_currentBoss is not null && _bossStartTimestamp > 0 && timestamp >= _bossStartTimestamp)
            stats.BossHealing += hit.Damage;

        if (stats.Hits.Count < MaxStoredCombatEventsPerPlayer)
        {
            if (stats.Hits.Count > 0 &&
                timestamp < stats.Hits[^1].Timestamp)
            {
                stats.SnapshotNeedsSort = true;
            }

            stats.Hits.Add(new StoredHit
            {
                Timestamp = timestamp,
                Skill = hit.Skill,
                Amount = hit.Damage,
                Crit = hit.IsCritical,
                IsDot = false,
                IsHeal = true,
                TargetRuntimeId = hit.Target,
            });
        }

        // Az overlayt a másodpercenkénti meter timer frissíti. Healenként
        // teljes snapshotot építeni hosszú fightnál O(n²) allokációt okozott.
    }

    private bool TryStartFightFromHealing(long timestamp)
    {
        if (_MobsInCombat.Count == 0)
            return false;

        _damageByPlayer.Clear();
        _fightTargets.Clear();
        _deadFightTargets.Clear();
        _currentMobName = string.Empty;
        _currentMobTemplateId = 0;
        _currentBoss = null;
        _currentBossRuntimeId = 0;
        _bossStartTimestamp = 0;
        _bossLastHitTimestamp = 0;

        foreach (uint target in _MobsInCombat)
        {
            _fightTargets.Add(target);
            TrackTargetMetadata(target);
        }

        _fightStartTimestamp = timestamp;
        _fightLastHitTimestamp = timestamp;
        _lastDamageTimestamp = timestamp;
        _fightEndTimestamp = 0;
        _fightEndRequestedAt = 0;
        _fightEndPending = false;
        _fightActive = true;

        if (_currentBoss is not null)
        {
            _bossStartTimestamp = timestamp;
            _bossLastHitTimestamp = timestamp;
        }

        DebugConsole.Log("healing",
            $"[HEALING FIGHT START] Targets={_fightTargets.Count}");

        if (CompanionSettings.DpsEncounterMode == DpsEncounterMode.All ||
            _currentBoss is not null)
        {
            ShowDpsOverlayForCombat();
        }

        return true;
    }

    private void ApplyDamageHit(DamageHit hit)
    {
        if (_SelfId == 0)
            return;

        uint originalActor = hit.Actor;
        uint ownerId = _summonOwners.TryGetValue(
            originalActor, out uint summonOwner)
            ? summonOwner
            : originalActor;

        bool isSelf = ownerId == _SelfId;
        PartyMemberEntry? actorMember = PartyMembers.FirstOrDefault(
            member => member.Id != 0 && member.Id == ownerId);
        bool isParty = actorMember is not null;
        bool friendlyTarget = hit.Target == _SelfId || PartyMembers.Any(
            member => member.Id != 0 && member.Id == hit.Target);

        if ((isSelf || isParty) && friendlyTarget)
        {
            string className = isSelf
                ? _selfClassName
                : actorMember?.ClassName ?? string.Empty;
            SkillDefinition? skill = GetSkillDatabase(className)?.FindVariant(hit.Skill);

            if (HealingSemantics.IsHealingHit(hit, skill))
            {
                ApplyHealHit(hit);
                return;
            }

            if (!hit.IsDot && hit.Type == 2 && (hit.Switch & 0x0F) == 4)
            {
                DebugConsole.Log("healing",
                    $"[HEAL DROP] Skill={hit.Skill} | Actor={hit.Actor} | " +
                    $"Target={hit.Target} | Reason=NoVerifiedHpHealingDefinition");
            }
        }

        if (hit.Target == hit.Actor || hit.Damage <= 0 ||
            hit.Damage > DamageScanner.MaxAcceptedDamage)
        {
            if (hit.Damage > DamageScanner.MaxAcceptedDamage)
            {
                DebugConsole.Log("damage",
                    $"[DAMAGE DROP] Implausible damage={hit.Damage} | " +
                    $"Actor={hit.Actor} | Target={hit.Target} | Skill={hit.Skill}");
            }
            return;
        }

        if (!isSelf && !isParty)
        {
            BufferPendingDamage(originalActor, hit);
            return;
        }

        // Közvetlen játékostalálatnál csak a kaszt NFG-jében ténylegesen
        // szereplő skill fogadható el. A korábbi kétjegyű prefix-ellenőrzés
        // véletlen payloadokat is valódi skillnek nézett. Summonok saját
        // skilljei nem feltétlenül szerepelnek a játékos kasztadatbázisában.
        if (originalActor == ownerId)
        {
            PartyMemberEntry? member = isSelf
                ? null
                : PartyMembers.FirstOrDefault(p => p.Id != 0 && p.Id == ownerId);
            string className = isSelf ? _selfClassName : member?.ClassName ?? string.Empty;
            SkillDatabase? database = GetSkillDatabase(className);
            if (database is not null && database.GetById(hit.Skill) is null)
            {
                DebugConsole.Log("skill",
                    $"[SKILL DROP] Skill={hit.Skill} | Owner={ownerId} | " +
                    $"Class={className} | Reason=NotInClassDatabase");
                return;
            }
        }

        string ownerName = isSelf
            ? _selfName
            : PartyMembers.First(p => p.Id == ownerId).Name;

        RecordDamage(ownerId, ownerName, hit);
    }
    
    private void AddIDSToPartyMembers(WireFrame frame)
    {
        if (frame.Opcode != OpcodeId.Player &&
            frame.Opcode != OpcodeId.PlayerLegacy)
        {
            return;
        }
        

        bool parsed = PartyCharacterParser.TryParse(
            frame.Payload, out var character);

        if (!parsed || character is null)
        {
            return;
        }
        

        foreach (var p in PartyMembers)
        {
        }

        if (character.Id == _SelfId)
        {
            return;
        }

        var member = PartyMembers.FirstOrDefault(p =>
            string.Equals(
                p.Name.Trim(),
                character.Name.Trim(),
                StringComparison.OrdinalIgnoreCase));

        if (member is null)
        {
            return;
        }

        if (member.Id != 0 && member.Id != character.Id)
        {
            return;
        }

        member.Id = character.Id;

        if (character.ClassId.HasValue)
            ApplyPartyClass(member, character.ClassId.Value);

        DebugConsole.Log("party",
            $" {member.Name} -> {member.Id}");

        PushMeter();
    }


    private void SearchSelf(WireFrame frame)
    {
        if (frame.Opcode != OpcodeId.Self)
            return;

        if (!OwnCharacterParser.TryParse(frame.Payload, out var self) || self is null)
            return;
        
        PetCollectionBridge.BindCharacter(self.Name);

        bool identityChanged = _SelfId != self.Id ||
            !string.Equals(_selfName, self.Name, StringComparison.OrdinalIgnoreCase);
        
        _selfName = self.Name;
        _SelfId = self.Id;
        _selfClassId = self.ClassId;
        
        if (identityChanged)
            PushMeter();
        
        CharacterName = string.IsNullOrWhiteSpace(self.Name)
            ? "Unknown"
            : self.Name;
        
        CharacterClass = "—";
        CharacterClassColor = "#9CA3AF";

        string? classIconPath = null;

        if (self.ClassId is byte jobValue)
        {
            var definition = _forgeDataLoader._classDatabase?.GetByJobValue(jobValue);

            if (definition is null)
                CharacterClass = LocalizationService.Format("UnknownJob", jobValue);

            if (definition is not null)
            {
                CharacterClass = definition.Name;
                CharacterClassColor = definition.Color;

                if (!string.Equals(_selfClassName, definition.Name, StringComparison.Ordinal) ||
                    _selfClassIcon is null)
                    _selfClassIcon = LoadClassIcon(definition.Icon);

                _selfClassName = definition.Name;
                _selfClassColor = definition.Color;
                classIconPath = definition.Icon;
            }

            DebugConsole.Log("class",
                $" RawJobValue={jobValue} | " +
                $"ResolvedClass={CharacterClass} | " +
                $"Color={CharacterClassColor} | Icon={classIconPath}");
        }
        else
        {
            DebugConsole.Log("class",
                " A karakter parser nem adott vissza ClassId értéket.");
        }

        DebugConsole.Log("self",
            $" Name={_selfName} | ID={_SelfId} | " +
            $"Level={self.Level} | GS={self.Gs} | Class={CharacterClass}");

        DetectedCharacterService.Shared.Update(new DetectedCharacterSnapshot(
            self.Name,
            CharacterClass,
            classIconPath,
            self.Level,
            self.Gs == 0 ? null : self.Gs,
            self.ServerId == 0 ? null : self.ServerId,
            self.Id,
            DateTimeOffset.UtcNow));

        if (identityChanged)
            _KnowMobs.Clear();
    }
    private Bitmap? LoadClassIcon(string? iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath))
            return null;
        
        try
        {
            var uri = new Uri($"avares://{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}/{iconPath.TrimStart('/')}");
            using var stream = AssetLoader.Open(uri);
            return new Bitmap(stream);
        }
        catch (Exception ex)
        {
            DebugConsole.Log("ICON",$" resource load failed: {ex.Message}");
            return null;
        }
    }
    private void CheckSummonedCreature(WireFrame frame)
    {
        
        if (frame.Payload is null || frame.Payload.Length == 0)
            return;

        TryLinkSummons(frame.Opcode, frame.Payload);
    }
        private static byte[] EncodeVarint(uint value)
    {
        var list = new List<byte>();
        do
        {
            byte b = (byte)(value & 0x7F);
            value >>= 7;
            if (value != 0) b |= 0x80;
            list.Add(b);
        } while (value != 0);
        return list.ToArray();
    }

    private static bool ContainsBytes(byte[] hay, byte[] needle)
    {
        for (int i = 0; i + needle.Length <= hay.Length; i++)
        {
            bool ok = true;
            for (int k = 0; k < needle.Length; k++)
            {
                if (hay[i + k] != needle[k]) { ok = false; break; }
            }
            if (ok) return true;
        }
        return false;
    }

    private static bool FrameHasOwnerRef(byte[] p, uint ownerId)
    {
        byte[] u32 =
        {
            (byte)ownerId, (byte)(ownerId >> 8),
            (byte)(ownerId >> 16), (byte)(ownerId >> 24)
        };

        for (int i = 0; i + 7 <= p.Length; i++)
        {
            if (p[i] == 0x07 && p[i + 1] == 0x02
                && p[i + 3] == u32[0] && p[i + 4] == u32[1]
                && p[i + 5] == u32[2] && p[i + 6] == u32[3])
                return true;
        }

        byte[] vi = EncodeVarint(ownerId);
        byte[] pat = new byte[2 + vi.Length];
        pat[0] = 0x2A;
        pat[1] = 0x38;
        Array.Copy(vi, 0, pat, 2, vi.Length);
        return ContainsBytes(p, pat);
    }

    private void RetryPendingSummons(WireFrame frame)
    {
        if (_pendingSummons.Count == 0 || _SelfId == 0)
            return;

        byte[] p = frame.Payload;
        if (!FrameHasOwnerRef(p, _SelfId))
            return;

        foreach (uint id in _pendingSummons.ToList())
        {
            byte[] idBytes = EncodeVarint(id);

            // Az egybájtos varint (pl. 100) túl gyakran fordul elő véletlenül.
            if (idBytes.Length < 2)
                continue;

            if (!ContainsBytes(p, idBytes))
                continue;

            _pendingSummons.Remove(id);
            _summonOwners[id] = _SelfId;
            ReplayPendingDamage(id, _SelfId);

            DebugConsole.Log("summon",
                $"[SUMMON LATE] Summon={id} | Owner={_SelfId} ({_selfName}) — utólag igazolva");
        }
    }
    private void TryLinkSummons(Opcode opcode, byte[] payload)
    {
        if (!SummonFrameParser.TryParse(opcode, payload, IsKnownOwner, out var links, out var mobs))
            return;

        if (mobs != null)
        {
            uint _mobId = mobs.MobId;
            int _mobTemplateId = Convert.ToInt32(mobs.MobTemplateId);
            _KnowMobs[_mobId] = _mobTemplateId;

            if (_fightTargets.Contains(_mobId) || _MobsInCombat.Contains(_mobId))
                TrackTargetMetadata(_mobId);

            if (_forgeDataLoader._mobsDatabase != null)
                DebugConsole.Log("spawn", $"[SPAWN] MobId={mobs.MobId} | " +
                                           $"TemplateId={mobs.MobTemplateId}");
            return;
        }

        foreach (var link in links)
        {
            if (_MobsInCombat.Contains(link.OwnerId))
            {
                DebugConsole.Log("summon",
                    $"[SUMMON CONFLICT] OwnerId={link.OwnerId} | " +
                    $"SummonId={link.SummonId} | " +
                    "Reason=Owner is currently registered as a mob in combat. " +
                    "Summon ownership link was not added.");
                return;
            }
            if (!link.IsVerified)
            {
                if (PartyMembers.Any(p => p.Id == link.SummonId))
                {
                    continue;
                }

                _pendingSummons.Add(link.SummonId);
                continue;
            }
        
            if (_summonOwners.TryGetValue(link.SummonId, out uint existing)
                && existing != link.OwnerId)
            {
                DebugConsole.Log("summon",
                    $"[SUMMON CONFLICT] Summon={link.SummonId} régi owner={existing}, " +
                    $"új owner={link.OwnerId} — elutasítva");
                continue;
            }

            _summonOwners[link.SummonId] = link.OwnerId;
            ReplayPendingDamage(link.SummonId, link.OwnerId);
            
            DebugConsole.Log("summon",
                $"[SUMMON OK] Summon={link.SummonId} | Owner={link.OwnerId} " +
                $"({GetOwnerName(link.OwnerId)}) | NpcId={link.NpcId}");
        }
    }

    private bool IsKnownOwner(uint id)
    {
        if (id == 0)
            return false;

        return id == _SelfId || PartyMembers.Any(p => p.Id == id);
    }

    private string GetOwnerName(uint id)
    {
        if (id == _SelfId)
            return _selfName;

        return PartyMembers.FirstOrDefault(p => p.Id == id)?.Name ?? "?";
    }

    private void TryRemoveDeathEntity(WireFrame frame)
    {
        Opcode opcode = frame.Opcode;
        byte[] payload = frame.Payload;
        
        if (opcode != OpcodeId.Death)
            return;
        
        if (DeathFrameParser.TryParse(payload, out var self))
        {
            if (_summonOwners.ContainsKey(self))
            {
                _summonOwners.Remove(self);
                DebugConsole.Log("summon",$"[SUMMON] Pet {self} eltávolítva (meghalt)");
            }

            if (_pendingSummons.Contains(self))
            {
                _pendingSummons.Remove(self);
            }
        }
        
    }
    private static string? ParsePartyName(byte[] payload)
    {
        // A vizsgált 02 97 minták:
        // [4 bájt fejléc] [névhossz] [UTF-8 party név]
        if (payload == null || payload.Length < 6)
            return null;

        int nameLength = payload[4];
        int nameStart = 5;

        if (nameLength <= 0 ||
            nameStart + nameLength > payload.Length)
        {
            return null;
        }

        string name = System.Text.Encoding.UTF8.GetString(
            payload, nameStart, nameLength).Trim();

        return string.IsNullOrWhiteSpace(name) ? null : name;
    }


    private static List<string> ParsePartyMemberNames(byte[] payload)
    {
        var names = new List<string>();

        if (payload == null || payload.Length < 4)
            return names;
        
        var utf8 = new System.Text.UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);

        for (int i = 0; i + 2 < payload.Length; i++)
        {
            if (payload[i + 1] != 0x05 &&
                payload[i + 1] != 0x08 &&
                payload[i + 1] != 0x09)
            {
                continue;
            }

            int nameLength = payload[i + 2];
            int nameStart = i + 3;

            // A mező hossza bájtokban értendő.
            if (nameLength < 2 || nameLength > 48)
                continue;

            if (nameStart + nameLength > payload.Length)
                continue;

            string name;

            try
            {
                name = utf8.GetString(
                    payload,
                    nameStart,
                    nameLength);
            }
            catch (System.Text.DecoderFallbackException)
            {
                // Nem érvényes UTF-8: ez nem elfogadható névmező.
                continue;
            }

            name = name.Trim();

            if (string.IsNullOrWhiteSpace(name))
                continue;

            bool valid = true;
            bool hasLetter = false;

            foreach (char c in name)
            {
                // Unicode betűk: latin, cirill és más írásrendszerek.
                if (char.IsLetter(c))
                {
                    hasLetter = true;
                    continue;
                }

                if (char.IsDigit(c))
                    continue;

                // Engedélyezett, gyakori névkarakterek.
                if (c == '_' || c == '-' || c == '.' ||
                    c == '\'' || c == ' ' ||
                    c == '[' || c == ']' ||
                    c == '(' || c == ')')
                {
                    continue;
                }

                // Vezérlőkarakter és ismeretlen szimbólum:
                // valószínűleg bináris mezőből származik.
                valid = false;
                break;
            }

            if (!valid || !hasLetter)
                continue;

            if (!names.Contains(name, StringComparer.OrdinalIgnoreCase))
                names.Add(name);

            // Ne vizsgáljuk újra a név belsejét rekordprefixként.
            i = nameStart + nameLength - 1;
        }

        return names;
    }




    private void UpdatePartyObservations(WireFrame frame)
    {
        Opcode opcode = frame.Opcode;

        bool fromServer = frame.Source.EndsWith(
            ":13328",
            StringComparison.Ordinal);

        if (!fromServer)
            return;
        
        if (opcode == OpcodeId.PartyKick ||
            opcode == OpcodeId.PartyLeave)
        {
            DebugConsole.Log("party",
                $"[PARTY LEAVE/KICK] Opcode={opcode} | " +
                $"Source={frame.Source} | " +
                $"Destination={frame.Destination}");
            return;
        }
        
        if (opcode != OpcodeId.Party &&
            opcode != OpcodeId.PartyMemberUpdate)
        {
            return;
        }

        byte[] payload = frame.Payload;

        if (opcode == OpcodeId.Party)
        {
            string? parsedPartyName = ParsePartyName(payload);
            List<string> names = ParsePartyMemberNames(payload);

            DebugConsole.Log("party",
                $"[PARTY FULL UPDATE] Name='{parsedPartyName ?? "?"}' | " +
                $"Members={string.Join(" | ", names)}");

            if (parsedPartyName is null)
            {
                DebugConsole.Log("party",
                    "[PARTY] Party név nem olvasható; " +
                    "a meglévő lista változatlan marad.");

                return;
            }

            var incomingNames = new HashSet<string>(
                names.Where(n => !string.IsNullOrWhiteSpace(n)),
                StringComparer.OrdinalIgnoreCase);

            var currentNames = new HashSet<string>(
                PartyMembers
                    .Select(p => p.Name)
                    .Where(n => !string.IsNullOrWhiteSpace(n)),
                StringComparer.OrdinalIgnoreCase);

            bool sameParty = currentNames.SetEquals(incomingNames);

            if (!sameParty)
            {
                DebugConsole.Log("party",
                    $"[PARTY CHANGE] Tagok megváltoztak! " +
                    $"Régi=[{string.Join(", ", currentNames)}] | " +
                    $"Új=[{string.Join(", ", incomingNames)}]");

                PartyMembers.Clear();
            }

            if (!string.Equals(
                    PartyName,
                    parsedPartyName,
                    StringComparison.OrdinalIgnoreCase))
            {
                PartyName = parsedPartyName;
                OnPropertyChanged(nameof(PartyName));
            }

            for (int i = PartyMembers.Count - 1; i >= 0; i--)
            {
                // FONTOS:
                // A Name property nevét igazítsd a saját
                // PartyMembers modelljéhez.
                string existingName = PartyMembers[i].Name;

                if (!incomingNames.Contains(existingName))
                {
                    DebugConsole.Log("party",
                        $"[PARTY MEMBER REMOVED] {existingName}");

                    PartyMembers.RemoveAt(i);
                }
            }

            // A meglévő tagokat az AddPartyMember frissíti,
            // az újakat pedig hozzáadja.
            foreach (string name in names)
            {
                AddPartyMember(name, payload);
            }
        }
        else
        {
            List<string> names = ParsePartyMemberNames(payload);

            DebugConsole.Log("party",
                $"[PARTY MEMBER UPDATE] Members={string.Join(" | ", names)}");

            // Részleges frissítésnél nem törlünk tagot,
            // mert nem tudjuk, hogy a lista teljes-e.
            foreach (string name in names)
            {
                AddPartyMember(name, payload);
            }
        }

        DebugConsole.Log("party",
            $"[PARTY] Name={PartyName} | Members={PartyMembers.Count}");

        PushMeter();
    }




    private byte? TryGetPartyClassId(string name, byte[] payload)
    {
        if (string.IsNullOrWhiteSpace(name) || payload.Length == 0)
            return null;

        byte[] nameBytes =  System.Text.Encoding.UTF8.GetBytes(name);

        for (int i = 0; i <= payload.Length - nameBytes.Length; i++)
        {
            bool match = true;

            for (int j = 0; j < nameBytes.Length; j++)
            {
                if (payload[i + j] != nameBytes[j])
                {
                    match = false;
                    break;
                }
            }

            if (!match)
                continue;

            int afterName = i + nameBytes.Length;

            // Kísérleti keresés: a név után következő 4 bájtos mezők.
            for (int relativeOffset = 0;
                 relativeOffset <= 12;
                 relativeOffset += 4)
            {
                int offset = afterName + relativeOffset;

                if (offset + 4 > payload.Length)
                    break;

                uint value =
                    (uint)payload[offset] |
                    ((uint)payload[offset + 1] << 8) |
                    ((uint)payload[offset + 2] << 16) |
                    ((uint)payload[offset + 3] << 24);

                if (value > byte.MaxValue)
                    continue;

                byte candidate = (byte)value;

                var definition = _forgeDataLoader._classDatabase?
                    .GetByJobValue(candidate);

                if (definition is null)
                    continue;
                

                return candidate;
            }
        }

        DebugConsole.Log("party",
            $"[PARTY CLASS] {name}: nem találtam adatbázisban feloldható jelöltet.");

        return null;
    }

    private void AddPartyMember(string name, byte[] payload)
    {
        name = name.Trim();

        if (string.IsNullOrWhiteSpace(name))
            return;

        PartyMemberEntry? existing = PartyMembers.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.Ordinal));

        byte? classId = TryGetPartyClassId(name, payload);

        if (existing is not null)
        {
            if (classId.HasValue)
                ApplyPartyClass(existing, classId.Value);

            return;
        }

        var member = new PartyMemberEntry
        {
            Id = 0,
            Name = name
        };

        if (classId.HasValue)
            ApplyPartyClass(member, classId.Value);

        PartyMembers.Add(member);

        DebugConsole.Log("party",
            $"[PARTY] {name} | ClassId={member.ClassId?.ToString() ?? "?"} " +
            $"| Class={member.ClassName}");
    }

    private void ApplyPartyClass(PartyMemberEntry member, byte classId)
    {
        var definition = _forgeDataLoader._classDatabase?
            .GetByJobValue(classId);

        if (definition is null)
        {
            DebugConsole.Log("party",
                $"[PARTY CLASS] {member.Name}: ismeretlen jelölt {classId}");
            return;
        }

        member.ClassId = classId;
        member.ClassName = definition.Name;
        member.ClassColor = definition.Color;
        member.ClassIcon = LoadClassIcon(definition.Icon);
        member.SpellPrefix = definition.SkillPrefix;
        DebugConsole.Log("party",
            $"[PARTY CLASS] {member.Name} | Job={classId} " +
            $"| {member.ClassName}");
    }




    private static string DescribeFrame(WireFrame frame, string name)
    {
        string source = frame.Source;
        string destination = frame.Destination;

        bool sourceIsGameServer =
            source.EndsWith(":13328", StringComparison.Ordinal);

        bool destinationIsGameServer =
            destination.EndsWith(":13328", StringComparison.Ordinal);

        string direction = (sourceIsGameServer, destinationIsGameServer) switch
        {
            (true, false) => "Szerver → kliens",
            (false, true) => "Kliens → szerver",
            _ => "Irány nem megerősített"
        };

        return $"{name} — {direction} — {frame.PayloadLength + 2} bájt";
    }
    
    private void SetLatestEvent(
        string latestEvent,
        string latestOpcode,
        string latestConfidence)
    {
        LatestEvent = latestEvent;
        LatestOpcode = latestOpcode;
        LatestConfidence = latestConfidence;

        OnPropertyChanged(nameof(LatestEvent));
        OnPropertyChanged(nameof(LatestOpcode));
        OnPropertyChanged(nameof(LatestConfidence));
    }

    public long ReceivedPackets
    {
        get => _receivedPackets;
        private set
        {
            if (_receivedPackets == value)
                return;

            _receivedPackets = value;
            OnPropertyChanged(nameof(ReceivedPackets));
        }
    }

    public string CaptureDescription =>
        SelectedCaptureDevice is null
            ? LocalizationService.Get("CaptureNoAdapter")
            : LocalizationService.Format("CaptureAdapter", SelectedCaptureDevice.DisplayName);
    public ObservableCollection<LogEntry> LogEntries { get; } = new();
    public ObservableCollection<EndpointStats> Endpoints { get; } = new();

    public int LogCount => LogEntries.Count;

    public ObservableCollection<CaptureDeviceOption> CaptureDevices { get; }
        = new();

    private CaptureDeviceOption? _selectedCaptureDevice;
    
    public CaptureDeviceOption? SelectedCaptureDevice
    {
        get => _selectedCaptureDevice;
        set
        {
            if (_selectedCaptureDevice == value)
                return;

            _selectedCaptureDevice = value;

            OnPropertyChanged(nameof(SelectedCaptureDevice));
            OnPropertyChanged(nameof(CaptureDescription));
        }
    }
    
    private void UpdatePetCollection(WireFrame frame)
    {
        if (!PetListFrameParser.TryParse(
                frame.Payload, out var petList) ||
            petList is null)
        {
            Console.WriteLine(
                $"[PET LIST ERROR] Invalid payload | " +
                $"Length={frame.Payload.Length}");
            return;
        }

        // A teljes gyűjteményt frissítjük.
        _ownedPets.Clear();

        foreach (var pet in petList.Pets)
        {
            _ownedPets[pet.PetId] = (
                pet.Level,
                pet.SoulProgress);
        }


        Console.WriteLine(
            $"[PET LIST] Loaded={_ownedPets.Count} | " +
            $"ActivePet={petList.ActivePetId} | " +
            $"TrailingBytes={petList.TrailingBytes}");

        // Pet ID → NFG definíció (név és kategória).
        var definitions = _ElyosPets?.Pets
            .GroupBy(p => p.PetId)
            .ToDictionary(
                g => g.Key,
                g => g.First());

        // Az első 15 megszerzett pet részletesen.
        foreach (var pet in petList.Pets
                     .OrderBy(p => p.PetId)
                     .Take(15))
        {
            var definition =
                definitions is not null &&
                definitions.TryGetValue(pet.PetId, out var found)
                    ? found
                    : null;

            Console.WriteLine(
                $"[PET OWNED] ID={pet.PetId} | " +
                $"Name={definition?.PetName ?? "Unknown"} | " +
                $"Category={definition?.Category ?? "Unknown"} | " +
                $"Level={pet.Level} | " +
                $"Soul={pet.SoulProgress}");
        }

        if (definitions is null)
            return;

        int owned = definitions.Keys.Count(
            id => _ownedPets.ContainsKey(id));

        int missing = definitions.Count - owned;

        Console.WriteLine(
            $"[PET FARM PROGRESS] " +
            $"Owned={owned}/{definitions.Count} | " +
            $"Missing={missing}");

        foreach (var entry in definitions
                     .Where(x => !_ownedPets.ContainsKey(x.Key))
                     .OrderBy(x => x.Key))
        {
            Console.WriteLine(
                $"[PET MISSING] ID={entry.Key} | " +
                $"Name={entry.Value.PetName} | " +
                $"Category={entry.Value.Category}");
        }

        PetCollectionBridge.Publish(
            petList.Pets.Select(p =>
                new PetFarmOwnedState(
                    p.PetId,
                    p.Level,
                    p.SoulProgress)));

        Console.WriteLine(
            $"[PET BRIDGE] Published={PetCollectionBridge.Snapshot?.Count ?? 0}");
    }

    public MainWindowViewModel(bool autoStartCapture = false)
    {
        _forgeDataLoader = new ForgeDataLoader();
        _forgeDataLoader.DataLoader();
        
        DebugConsole.Log("debug", "ForgeDataLoader betöltés befejeződött.");
        
        _ElyosPets = _forgeDataLoader._elyosPetsDatabase;
        _mobsDatabaseStatic = _forgeDataLoader._mobsDatabase;
        
        foreach (var option in NetworkAdapterDetector.Detect())
            CaptureDevices.Add(option);

        SelectedCaptureDevice =
            CaptureDevices.FirstOrDefault(x => x.IsRecommended)
            ?? CaptureDevices.FirstOrDefault();

        if (SelectedCaptureDevice is null)
            StatusText = LocalizationService.Get("CaptureNoAdapterFound");

        _meterTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };

        _meterTimer.Tick += (_, _) =>
        {
            DrainCaptureCounters();
            CheckCombatTimeout();
            PushMeter();
        };

        _meterTimer.Start();

        _dpsOverlayHideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(15)
        };
        _dpsOverlayHideTimer.Tick += (_, _) =>
        {
            _dpsOverlayHideTimer.Stop();
            if (!_fightActive &&
                _autoManagedDpsOverlay &&
                CompanionSettings.AutoManageDpsOverlay &&
                OverlayWindowManager.DpsWindow is { IsVisible: true })
            {
                OverlayWindowManager.HideDps();
                _autoManagedDpsOverlay = false;
            }
        };

        if (autoStartCapture)
            Dispatcher.UIThread.Post(StartCapture);
    }
    [RelayCommand]
    private void StartCapture()
    {
        if (IsCapturing)
            return;

        if (SelectedCaptureDevice is null)
        {
            StatusText = LocalizationService.Get("CaptureNoAdapter");
            return;
        }

        try
        {
            if (!_isSubscribed)
            {
                _captureService.PacketReceived += OnPacketReceived;
                _isSubscribed = true;
            }

            _tcpStreamDecoder = new TcpStreamDecoder(OnFrameDecoded);

            _captureService.Start(SelectedCaptureDevice.Device);

            IsCapturing = true;
            StatusText = LocalizationService.Get("CaptureRunning");
            StatusColor = "#34D399";
        }
        catch (Exception ex)
        {
            _tcpStreamDecoder = null;
            IsCapturing = false;
            StatusText = LocalizationService.Get("CaptureStartFailed");
            StatusColor = "#F87171";
            DebugConsole.Log("capture", $"Capture start failed: {ex.Message}");
        }
    }

    public void StartCaptureAfterLogin() => StartCapture();
    
    [RelayCommand]
    private void StopCapture()
    {
        try
        {
            _captureService.Stop();
            _tcpStreamDecoder?.Flush(DateTimeOffset.Now);
            _tcpStreamDecoder = null;
        }
        catch (Exception ex)
        {
            StatusText = LocalizationService.Get("CaptureStopFailed");
            StatusColor = "#F87171";
            DebugConsole.Log("capture", $"Capture stop failed: {ex.Message}");
            return;
        }

        IsCapturing = false;
        StatusText = LocalizationService.Get("CaptureStopped");
        StatusColor = "#F87171";
    }


    

    private readonly object _probeLock = new();

    private readonly Dictionary<ushort, long> _probeCounts = new();
    private readonly Dictionary<ushort, string> _probeSamples = new();

    private long _probePacketCount;
    private readonly object _frameLock = new();
    private readonly Queue<WireFrame> _decodedFrames = new();
    private long _decodedFrameBytes;

    private const int MaxStoredFrames = 2_048;
    private const long MaxStoredFrameBytes = 32L * 1024 * 1024;
    private long _totalDecodedFrames;

    private readonly object _pendingFrameLock = new();
    private readonly Queue<WireFrame> _pendingFrames = new();
    private long _pendingFrameBytes;
    private bool _frameDrainScheduled;
    private long _droppedDecodedFrames;
    private long _lastReportedDroppedFrames;

    private const int MaxPendingFrames = 4_096;
    private const long MaxPendingFrameBytes = 32L * 1024 * 1024;
    private const int MaxFramesPerUiPass = 256;

    private void RecordDamage(
        uint ownerId,
        string ownerName,
        DamageHit hit,
        long? eventTimestamp = null)
    {
        if (hit.Damage <= 0)
            return;

        long hitTimestamp =
            eventTimestamp ?? Stopwatch.GetTimestamp();

        bool fightJustStarted = false;

        // Lejárt, félretett fightot előbb elmentjük.
        // Az új sebzés MINDIG a megfelelő runtime mobhoz kerüljön.
        long now = Stopwatch.GetTimestamp();
        FlushExpiredPausedFights(now);

        // Egy későn azonosított summon régi, pufferelt találata nem indíthat
        // új fightot és nem válthat át egy másik mobra. Ha ugyanaz a jelenlegi
        // célpont, az összeget megtartjuk, de nem engedjük visszahúzni az órát.
        if (eventTimestamp.HasValue &&
            hitTimestamp < now - (long)(2.0 * Stopwatch.Frequency))
        {
            if (!_fightActive || !_fightTargets.Contains(hit.Target))
            {
                DebugConsole.Log("damage",
                    $"[DAMAGE REPLAY DROP] Target={hit.Target} | Skill={hit.Skill} | " +
                    "Reason=StaleReplayCannotStartOrSwitchFight");
                return;
            }

            hitTimestamp = Math.Max(hitTimestamp, _fightStartTimestamp);
        }

        if (_fightActive && _fightEndPending &&
            (now - _fightEndRequestedAt) / (double)Stopwatch.Frequency >= FightEndGraceSeconds)
        {
            FinalizeCombat("GraceExpiredBeforeDamage");
        }

        bool sameCurrentTarget =
            _fightActive && _fightTargets.Contains(hit.Target);
        if (!sameCurrentTarget && !eventTimestamp.HasValue)
            TryResumePausedFight(hit.Target, hitTimestamp, now);

        if (_fightActive && _fightEndPending)
        {
            if (_fightTargets.Contains(hit.Target))
            {
                if (hitTimestamp >= _fightEndRequestedAt)
                {
                    _fightEndPending = false;
                    _fightEndRequestedAt = 0;
                    _deadFightTargets.Remove(hit.Target);
                    _MobsInCombat.Add(hit.Target);
                    DebugConsole.Log("dpstime",
                        $"[FIGHT RESUMED] Target={hit.Target} | Via=SameTargetDamage");
                }
            }
            else if (hitTimestamp >= _fightEndRequestedAt)
            {
                // Régi fight: ideiglenesen elrakjuk, nem FINALIZÁLJUK!
                if ((now - _fightEndRequestedAt) /
                    (double)Stopwatch.Frequency >= FightEndGraceSeconds)
                    FinalizeCombat("GraceExpiredBeforeNewTarget");
                else
                    SuspendCurrentFight("DifferentTargetDamage", now);
            }
        }

        if (!_fightActive)
        {
            _damageByPlayer.Clear();
            _fightTargets.Clear();
            _deadFightTargets.Clear();
            _MobsInCombat.Clear();
            _MobsInCombat.Add(hit.Target);

            _fightStartTimestamp = hitTimestamp;
            _fightLastHitTimestamp = hitTimestamp;
            _fightEndTimestamp = 0;
            _lastDamageTimestamp = hitTimestamp;
            _fightEndRequestedAt = 0;
            _currentMobName = string.Empty;
            _currentMobTemplateId = 0;
            _currentBoss = null;
            _currentBossRuntimeId = 0;
            _bossStartTimestamp = 0;
            _bossLastHitTimestamp = 0;

            _fightActive = true;
            _fightEndPending = false;
            fightJustStarted = true;

            DebugConsole.Log("dpstime",
                $"[FIGHT START] Target={hit.Target} | " +
                $"PausedFights={_pausedFights.Count}");
        }
        else if (hitTimestamp < _fightStartTimestamp)
        {
            _fightStartTimestamp = hitTimestamp;
        }

        _lastDamageTimestamp = Math.Max(_lastDamageTimestamp, hitTimestamp);

        _fightTargets.Add(hit.Target);
        TrackTargetMetadata(hit.Target);
        var targetMetadata = ResolveTarget(hit.Target);
        bool isBossTarget = targetMetadata.Boss is not null;
        if (isBossTarget)
        {
            if (_bossStartTimestamp == 0 || hitTimestamp < _bossStartTimestamp)
                _bossStartTimestamp = hitTimestamp;
            _bossLastHitTimestamp = Math.Max(_bossLastHitTimestamp, hitTimestamp);
        }

        // Csak friss damage írhat felül egy előző haláljelzést.
        if (!_fightEndPending || hitTimestamp >= _fightEndRequestedAt)
            _deadFightTargets.Remove(hit.Target);

        if (hitTimestamp > _fightLastHitTimestamp)
            _fightLastHitTimestamp = hitTimestamp;

        if (_fightEndPending)
        {
            DebugConsole.Log("damage",
                $"[LATE HIT RECOVERED] Target={hit.Target} | " +
                $"Skill={hit.Skill} | Damage={hit.Damage}");
        }
        
        if (!_damageByPlayer.TryGetValue(ownerId, out var stats))
        {
            stats = new PlayerDamageStats
            {
                Name = ownerName,

                ClassId = ownerId == _SelfId
                    ? _selfClassId
                    : null,

                ClassName = ownerId == _SelfId
                    ? _selfClassName
                    : "",

                ClassColor = ownerId == _SelfId
                    ? _selfClassColor
                    : "#9CA3AF",

                ClassIcon = ownerId == _SelfId
                    ? _selfClassIcon
                    : null,
            };

            _damageByPlayer[ownerId] = stats;
        }

        stats.TotalDamage += hit.Damage;
        if (isBossTarget)
            stats.BossDamage += hit.Damage;
        if (stats.Hits.Count < MaxStoredCombatEventsPerPlayer)
        {
            if (stats.Hits.Count > 0 &&
                hitTimestamp < stats.Hits[^1].Timestamp)
            {
                stats.SnapshotNeedsSort = true;
            }

            stats.Hits.Add(new StoredHit
            {
                Timestamp = hitTimestamp,
                Skill = hit.Skill,
                Amount = hit.Damage,
                Crit = hit.IsCritical,
                IsDot = hit.IsDot,
                TargetRuntimeId = hit.Target,
                TargetTemplateId = targetMetadata.TemplateId,
                IsBossTarget = isBossTarget,
            });
        }

        if (fightJustStarted &&
            (CompanionSettings.DpsEncounterMode == DpsEncounterMode.All || isBossTarget))
            ShowDpsOverlayForCombat();
    }

    private void ShowDpsOverlayForCombat()
    {
        if (!CompanionSettings.AutoManageDpsOverlay)
            return;

        _dpsOverlayHideTimer.Stop();

        OverlayWindowManager.ShowDps(BuildOverlaySnapshot());
        _autoManagedDpsOverlay = true;
    }

    private void ScheduleAutomaticDpsOverlayHide()
    {
        if (!_autoManagedDpsOverlay || !CompanionSettings.AutoManageDpsOverlay)
            return;

        _dpsOverlayHideTimer.Stop();
        _dpsOverlayHideTimer.Start();
    }

    // Pillanatkép az aktuális fightról az overlay számára.
    
public MeterSnapshot BuildSnapshot(bool bossOnly = false)
{
    long endTs = _fightActive
        ? (_fightEndPending
            ? _fightLastHitTimestamp
            : Stopwatch.GetTimestamp())
        : _fightEndTimestamp;
    
    double duration = _damageByPlayer.Count == 0
        ? 0
        : Math.Max(
            0,
            (endTs - _fightStartTimestamp) /
            (double)Stopwatch.Frequency);
    double bossStartOffset = _bossStartTimestamp > 0
        ? Math.Max(0, (_bossStartTimestamp - _fightStartTimestamp) / (double)Stopwatch.Frequency)
        : 0;
    double bossDuration = _bossStartTimestamp > 0
        ? Math.Max(0, (endTs - _bossStartTimestamp) / (double)Stopwatch.Frequency)
        : 0;
    double viewDuration = bossOnly && _currentBoss is not null ? bossDuration : duration;

    var players = new List<PlayerStat>();

    foreach (var entry in _damageByPlayer)
    {
        var stats = entry.Value;
        
        var member = PartyMembers.FirstOrDefault(p =>
            string.Equals(p.Name, stats.Name, StringComparison.Ordinal));

        if (member?.ClassId is byte partyClassId)
        {
            var definition = _forgeDataLoader._classDatabase?
                .GetByJobValue(partyClassId);

            if (definition is not null)
            {
                stats.ClassId = partyClassId;
                stats.ClassName = definition.Name;
                stats.ClassColor = definition.Color;
                stats.ClassIcon = member.ClassIcon;
            }
        }
        
        SkillDatabase? db = GetSkillDatabase(stats.ClassName);

        players.Add(ToPlayerStat(
            stats,
            viewDuration,
            _fightStartTimestamp,
            db,
            bossOnly,
            bossStartOffset));
    }


    if (_SelfId != 0 &&
        !players.Any(p => p.Name == _selfName))
    {
        players.Add(new PlayerStat
        {
            Name = _selfName,
            ClassId = (int)(_selfClassId ?? 0),
            ClassName = _selfClassName,
            ClassColor = _selfClassColor,
            ClassIcon = _selfClassIcon,
            Duration = viewDuration,
            Damage = 0,
            BossDamage = 0,
            Healing = 0
        });
    }
    
    string mobName = GetCurrentMobName();
    foreach (var member in PartyMembers)
    {
        if (string.IsNullOrWhiteSpace(member.Name))
            continue;

        if (players.Any(p => string.Equals(
                p.Name,
                member.Name,
                StringComparison.Ordinal)))
        {
            continue;
        }

        players.Add(new PlayerStat
        {
            Name = member.Name,
            ClassId = member.ClassId ?? 0,
            ClassName = member.ClassName,
            ClassColor = member.ClassColor,
            ClassIcon = member.ClassIcon,
            Duration = viewDuration,
            Damage = 0,
            BossDamage = 0,
            Healing = 0
        });
    }

    BossDefinition? boss = _currentBoss;
    double? hpPercent = null;
    long? currentHp = null;
    if (boss is not null && _currentBossRuntimeId != 0 &&
        _mobHealthRaw.TryGetValue(_currentBossRuntimeId, out uint rawHp))
    {
        hpPercent = rawHp <= 100
            ? rawHp
            : rawHp <= 10_000 ? rawHp / 100d : null;
        if (hpPercent.HasValue && boss.MaxHp > 0)
            currentHp = (long)Math.Round(boss.MaxHp * hpPercent.Value / 100d);
    }

    return new MeterSnapshot
    {
        Players = players,
        Duration = viewDuration,
        MobMaxHp = boss?.MaxHp ?? 0,
        MobTemplateName = mobName,
        MobTemplateId = _currentMobTemplateId,
        IsBossEncounter = boss is not null,
        BossId = boss?.Id ?? string.Empty,
        BossHpPercent = hpPercent,
        BossCurrentHp = currentHp,
        EnrageSeconds = boss?.EnrageSeconds,
        EnrageRemainingSeconds = boss?.EnrageSeconds is int enrage
            ? enrage - bossDuration
            : null,
        BossStartOffset = bossOnly ? 0 : bossStartOffset,
        BossDuration = bossDuration,
    };
}

private MeterSnapshot BuildOverlaySnapshot()
{
    if (CompanionSettings.DpsEncounterMode == DpsEncounterMode.All)
        return BuildSnapshot();

    return _currentBoss is not null
        ? BuildSnapshot(bossOnly: true)
        : _lastBossSnapshot;
}
private string GetCurrentMobName()
{
    if (_currentBoss is not null)
        return _currentBoss.LocalizedName(
            LocalizationService.CurrentLanguageCode,
            _currentMobName);

    foreach (uint runtimeId in _MobsInCombat)
    {
        if (_KnowMobs.TryGetValue(runtimeId, out int templateId) &&
            _forgeDataLoader._mobsDatabase?.GetById(templateId) is { } mob)
        {
            _currentMobName = mob.Name;
            _currentMobTemplateId = templateId;
            return mob.Name;
        }
    }
    if(_currentMobName == "")
        return "—";
    return _currentMobName;
}
    private SkillDatabase? GetSkillDatabase(string c)
    {
        return c switch
        {
            "Assassin" => _forgeDataLoader._asssasinDatabase,
            "Chanter" => _forgeDataLoader._chanterDatabase,
            "Cleric" => _forgeDataLoader._clericDatabase,
            "Elementalist" => _forgeDataLoader._elementalistDatabse,
            "Gladiator" => _forgeDataLoader._gladiatorDatabase,
            "Ranger" => _forgeDataLoader._rangerDatabase,
            "Sorcerer" => _forgeDataLoader._sorcererDatabase,
            "Templar" => _forgeDataLoader._templarDatabase,
            _ => null
        };
    }
    private static SkillDefinition? FindSkill(
        SkillDatabase? db,
        uint skillId)
    {
        return db?.FindVariant(skillId);
    }
    private static PlayerStat ToPlayerStat(
        PlayerDamageStats st,
        double duration,
        long fightStart,
        SkillDatabase? skillDatabase,
        bool bossOnly,
        double bossStartOffset)
    {
        // A fight teljes eseménylistáját nem építjük újra másodpercenként.
        // Csak az előző snapshot óta érkezett találatokat alakítjuk át.
        if (!string.Equals(st.SnapshotClassName, st.ClassName, StringComparison.Ordinal))
        {
            st.SnapshotEvents.Clear();
            st.SnapshotHitCount = 0;
            st.SnapshotClassName = st.ClassName;
            st.SnapshotNeedsSort = true;
        }

        for (int i = st.SnapshotHitCount; i < st.Hits.Count; i++)
        {
            StoredHit hit = st.Hits[i];
            SkillDefinition? skill = FindSkill(skillDatabase, hit.Skill);

            st.SnapshotEvents.Add(new SkillEvent
            {
                Time = Math.Max(
                    0,
                    (hit.Timestamp - fightStart) / (double)Stopwatch.Frequency),
                Skill = skill?.Name ?? $"Skill {hit.Skill}",
                SkillIcon = skill?.Icon ?? skill?.IconUrl ?? string.Empty,
                Amount = hit.Amount,
                Crit = hit.Crit,
                IsHeal = hit.IsHeal,
                TargetRuntimeId = hit.TargetRuntimeId,
                TargetTemplateId = hit.TargetTemplateId,
                IsBossTarget = hit.IsBossTarget,
            });
        }

        st.SnapshotHitCount = st.Hits.Count;
        if (st.SnapshotNeedsSort)
        {
            st.SnapshotEvents.Sort((a, b) => a.Time.CompareTo(b.Time));
            st.SnapshotNeedsSort = false;
        }

        return new PlayerStat
        {
            Name = st.Name,
            ClassId = (int)(st.ClassId ?? 0),
            ClassName = st.ClassName,
            ClassColor = st.ClassColor,
            ClassIcon = st.ClassIcon,
            Duration = duration,
            Damage = bossOnly ? st.BossDamage : st.TotalDamage,
            BossDamage = st.BossDamage,
            Healing = bossOnly ? st.BossHealing : st.TotalHealing,
            BossHealing = st.BossHealing,
            Events = bossOnly
                ? st.SnapshotEvents
                    .Where(entry => entry.Time >= bossStartOffset && (entry.IsHeal || entry.IsBossTarget))
                    .Select(entry => new SkillEvent
                    {
                        Time = Math.Max(0, entry.Time - bossStartOffset),
                        Skill = entry.Skill,
                        SkillIcon = entry.SkillIcon,
                        Amount = entry.Amount,
                        Crit = entry.Crit,
                        IsHeal = entry.IsHeal,
                        TargetRuntimeId = entry.TargetRuntimeId,
                        TargetTemplateId = entry.TargetTemplateId,
                        IsBossTarget = entry.IsBossTarget,
                    }).ToList()
                : st.SnapshotEvents,
        };
    }

    private void PushMeter()
    {
        if (OverlayWindowManager.DpsWindow is not { IsVisible: true })
            return;

        OverlayWindowManager.UpdateDps(BuildOverlaySnapshot());
    }

    private void CountDps()
    {
        if (_damageByPlayer.Count == 0)
            return;

        long endTimestamp = _fightActive
            ? Stopwatch.GetTimestamp()
            : _fightEndTimestamp;

        double elapsedSeconds =
            (endTimestamp - _fightStartTimestamp)
            / (double)Stopwatch.Frequency;

        if (elapsedSeconds <= 0)
            return;

        var players = _damageByPlayer
            .Select(player =>
            {
                long damage = player.Value.TotalDamage;

                return (
                    Name: player.Value.Name,
                    Class: "Unknown",
                    Damage: damage,
                    Dps: damage / elapsedSeconds
                );
            })
            .OrderByDescending(player => player.Damage)
            .ToList();

        DebugConsole.Log("dpstime",
            $"--- DPS | {(_fightActive ? "FIGHT" : "FINAL")} | " +
            $"Fight time: {elapsedSeconds:F1}s ---");

        foreach (var player in players)
        {
            DebugConsole.Log("dpstime",
                $"{player.Name,-20} " +
                $"Damage: {player.Damage,10:N0} | " +
                $"DPS: {player.Dps,8:N1}");
        }

        PushMeter();
    }

    private void OnFrameDecoded(WireFrame frame)
    {
        StoreDecodedFrame(frame);

        bool scheduleDrain = false;

        lock (_pendingFrameLock)
        {
            long frameBytes = frame.PayloadLength;

            if (_pendingFrames.Count >= MaxPendingFrames ||
                _pendingFrameBytes + frameBytes > MaxPendingFrameBytes)
            {
                Interlocked.Increment(ref _droppedDecodedFrames);
                return;
            }

            _pendingFrames.Enqueue(frame);
            _pendingFrameBytes += frameBytes;

            if (!_frameDrainScheduled)
            {
                _frameDrainScheduled = true;
                scheduleDrain = true;
            }
        }

        // Egyetlen dispatcher-lánc dolgozza fel a frame-eket, nem frame-enként
        // rakunk új closure-t az UI sorába.
        if (scheduleDrain)
            Dispatcher.UIThread.Post(DrainDecodedFrames);
    }

    private void StoreDecodedFrame(WireFrame frame)
    {
        lock (_frameLock)
        {
            _totalDecodedFrames++;
            _decodedFrames.Enqueue(frame);
            _decodedFrameBytes += frame.PayloadLength;

            while (_decodedFrames.Count > MaxStoredFrames ||
                   _decodedFrameBytes > MaxStoredFrameBytes)
            {
                WireFrame removed = _decodedFrames.Dequeue();
                _decodedFrameBytes -= removed.PayloadLength;
            }
        }
    }

    private void DrainDecodedFrames()
    {
        int processed = 0;

        while (processed < MaxFramesPerUiPass)
        {
            WireFrame frame;

            lock (_pendingFrameLock)
            {
                if (_pendingFrames.Count == 0)
                {
                    _frameDrainScheduled = false;
                    return;
                }

                frame = _pendingFrames.Dequeue();
                _pendingFrameBytes -= frame.PayloadLength;
            }

            try
            {
                ProcessDecodedFrameOnUi(frame);
            }
            catch (Exception ex)
            {
                DebugConsole.Log("debug",
                    $"Frame feldolgozási hiba ({frame.Opcode}): {ex.Message}");
            }

            processed++;
        }
        
        Dispatcher.UIThread.Post(DrainDecodedFrames);
    }

    private void ProcessDecodedFrameOnUi(WireFrame frame)
    {
        ReceivedFrames++;
        ParsedOpcodes++;

        string name = OpcodeCatalog.GetName(frame.Opcode);
        bool known = OpcodeCatalog.TryGetInfo(frame.Opcode, out var opcodeInfo);
        Opcode opcode = frame.Opcode;


        if (opcode == OpcodeId.PetList)
            UpdatePetCollection(frame);
        if(opcode == OpcodeId.PetAdded || opcode == OpcodeId.PetLevel || opcode == OpcodeId.PetProgress)
            PetCollectionLiveObserver.Observe(frame);
        
        if (opcode == OpcodeId.LeaveCombat ||
            opcode == OpcodeId.DisengageCombat ||
            opcode == OpcodeId.HP)
            CombatEndCheck(frame);
        
        if (opcode == OpcodeId.Self)
            SearchSelf(frame);

        if (opcode == OpcodeId.Spawn || opcode == OpcodeId.Characters ||
            opcode == OpcodeId.TheFuck || opcode == OpcodeId.TheFuck2)
        {
            CheckSummonedCreature(frame);
        }

        // Ha meghal a mob, a fightnak vége/a pet kivétele a summonjaink közül!
        if (opcode == OpcodeId.Death)
        {
            TryRemoveDeathEntity(frame);
            CombatDeathCheck(frame);
        }

        if (opcode == OpcodeId.Party ||
            opcode == OpcodeId.PartyMemberUpdate ||
            opcode == OpcodeId.PartyKick ||
            opcode == OpcodeId.PartyLeave)
        {
            UpdatePartyObservations(frame);
        }

        if (opcode == OpcodeId.Player || opcode == OpcodeId.PlayerLegacy)
            AddIDSToPartyMembers(frame);

        if (opcode == OpcodeId.EnterCombat)
            CombatStartCheck(frame);

        RetryPendingSummons(frame);
        ProcessDamage(frame);

        if (!known)
            UnknownOpcodes++;

        string confidence = string.IsNullOrWhiteSpace(opcodeInfo?.Confidence)
            ? (known ? "Ismert" : "Ismeretlen")
            : opcodeInfo.Confidence;

        string description = string.IsNullOrWhiteSpace(opcodeInfo?.Description)
            ? (known ? name : "Még nem regisztrált opcode.")
            : opcodeInfo.Description;

        string summary = DescribeFrame(frame, name);

        SetLatestEvent(
            $"{description} ({summary})",
            frame.Opcode.ToString(),
            confidence);
    }


    private void OnPacketReceived(CapturedPacket packet)
    {
        _endpointTracker.Observe(packet);

        // A TCP-csomagokat a streamdekódernek adjuk át.
        // Egy csomag nem feltétlenül tartalmaz teljes frame-et.
        if (packet.IsTcp)
            _tcpStreamDecoder?.Feed(packet);

        if (packet.Payload.Length >= 2)
        {
            ushort firstBytes = BinaryPrimitives.ReadUInt16LittleEndian(packet.Payload);

            lock (_probeLock)
            {
                _probePacketCount++;

                if (!_probeCounts.ContainsKey(firstBytes))
                    _probeCounts[firstBytes] = 0;

                _probeCounts[firstBytes]++;

                if (!_probeSamples.ContainsKey(firstBytes))
                {
                    var payload = packet.Payload;

                    _probeSamples[firstBytes] =
                        $"Protocol={packet.Protocol} | " +
                        $"Length={payload.Length} B | " +
                        $"HEX={Convert.ToHexString(
                            payload.AsSpan(0, Math.Min(payload.Length, 32)))} | " +
                        $"{packet.Source} -> {packet.Destination}";
                }
            }
        }
        else
        {
            lock (_probeLock)
            {
                _probePacketCount++;
            }
        }

        // A UI-nak csak az összesített számláló kell. Frame-enként külön UI
        // feladat helyett a timer másodpercenként egyszer üríti ezt.
        Interlocked.Increment(ref _pendingReceivedPackets);
    }

    private void DrainCaptureCounters()
    {
        long received = Interlocked.Exchange(ref _pendingReceivedPackets, 0);
        if (received > 0)
            ReceivedPackets += received;

        long dropped = Interlocked.Read(ref _droppedDecodedFrames);
        if (dropped != _lastReportedDroppedFrames)
        {
            long sinceLastReport = dropped - _lastReportedDroppedFrames;
            _lastReportedDroppedFrames = dropped;
            DebugConsole.Log("debug",
                $"Capture túlterhelés: {sinceLastReport} frame kihagyva " +
                $"(összesen: {dropped}).");
        }
    }

    [RelayCommand]
    private void ExportOpcodeProbe()
    {
        string report;

        lock (_probeLock)
        {
            report =
                $"Captured packets: {_probePacketCount}{Environment.NewLine}" +
                $"Unique first-byte pairs: {_probeCounts.Count}{Environment.NewLine}" +
                $"{Environment.NewLine}" +
                string.Join(
                    Environment.NewLine,
                    _probeCounts
                        .OrderByDescending(x => x.Value)
                        .Take(30)
                        .Select(x =>
                            $"{(byte)(x.Key & 0xFF):X2} {(byte)(x.Key >> 8):X2} | " +
                            $"Count: {x.Value} | " +
                            _probeSamples.GetValueOrDefault(x.Key, "No sample"))
                );
        }

        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "aion2_opcode_probe.txt"
        );

        File.WriteAllText(path, report);

        StatusText = $"Opcode report mentve: {path}";
    }
    
    
    [RelayCommand]
    private void ExportDecodedFrames()
    {
        WireFrame[] frames;
        long totalFrames;

        lock (_frameLock)
        {
            frames = _decodedFrames.ToArray();
            totalFrames = _totalDecodedFrames;
        }

        string desktop = Environment.GetFolderPath(
            Environment.SpecialFolder.DesktopDirectory);

        string timestamp = DateTime.Now.ToString(
            "yyyyMMdd_HHmmss",
            CultureInfo.InvariantCulture);

        string path = Path.Combine(
            desktop,
            $"aion2_decoded_frames_{timestamp}.csv");

        string Csv(string? value)
        {
            value ??= string.Empty;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        var lines = new List<string>
        {
            "Time,Source,Destination,Opcode,OpcodeName,PayloadLength,Flags,PayloadHex"
        };

        foreach (var frame in frames)
        {
            string opcodeName = OpcodeCatalog.GetName(frame.Opcode);

            lines.Add(string.Join(",",
                Csv(frame.Time.ToString("O", CultureInfo.InvariantCulture)),
                Csv(frame.Source),
                Csv(frame.Destination),
                Csv(frame.Opcode.ToString()),
                Csv(opcodeName),
                frame.PayloadLength.ToString(CultureInfo.InvariantCulture),
                Csv(frame.Flags.ToString()),
                Csv(Convert.ToHexString(frame.Payload))
            ));
        }

        File.WriteAllLines(
            path,
            lines,
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        string summaryPath = Path.Combine(
            desktop,
            $"aion2_decoded_summary_{timestamp}.txt");

        var summary = new List<string>
        {
            "AION 2 COMPANION — DECODED FRAME REPORT",
            $"Export time: {DateTime.Now:O}",
            $"Total decoded frames since capture: {totalFrames}",
            $"Frames included in this export: {frames.Length}",
            $"Storage limit: {MaxStoredFrames}",
            "",
            "OPCODE SUMMARY",
            "=============="
        };

        foreach (var group in frames
            .GroupBy(f => f.Opcode)
            .OrderByDescending(g => g.Count()))
        {
            summary.Add(
                $"{group.Key} | {OpcodeCatalog.GetName(group.Key)} | " +
                $"Count: {group.Count()} | " +
                $"Payload bytes: {group.Sum(f => (long)f.PayloadLength)}");
        }

        File.WriteAllLines(
            summaryPath,
            summary,
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        StatusText =
            $"Frame export kész: {frames.Length} frame | {path}";
    }
    
}
