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

/*
 *          TODO ÍGY SORBA!
 * 
 * A heal nem az aminek hittük, de legalább a szarokat kiszűrjük.
 *
 *  A Death-nél, ellenőrízzük az utolsó hit-et is arra a mobra, abból látjuk a hpját..
 *  Ha a hp-ja több mint a hit, remélhetőleg ígykell nézni, nem töröljük a mobot nem halt meg! 
    Habár első tesztnél nézhetjük úgyis hogy ameddig nem 0 a hp-ja, addig megtartjuk a Deathnél.
    - Jó a hit-be nem látjuk a hp-t!
    - De a hp-be látjuk xD
 *  A timer úgyis megoldja :3
 *  Kész
 *
 * 
 * 
 * Ha ismerjük a player classát, ellenőrzzük hogy tényleg ővé az a spell,
 * 2 opció van:
        Ha nem ővé a spell megkeressük kié lehet (Ez csak akkor jó ha abból a class-ból csak 1 ember van!)
        Ha nem ővé a spell eldobjuk a sebzést és senki nem kaphatja meg!
 *  Ezt a 2-öt persze egyszerre kell használni! AION2_Classes file-ba elkell tárolni a spell prefixet!
 *  Ismeretlen skilleket meg egyenlőre mindig kuka (Amit sehol se ismerünk!)
 * Kész
 *
 * Csökkentsük a vinyo terhelést. Automatikusan induljon az egész.
 * A logolást csak be lehessen kapcsolni (file-ba exportálás) mert jelenleg eltünik a helyem ha futtatom.
 * Debugnak így jó, de bele hal a gép néha xD 
 * (Program bezárása utánse jön vissza a hely a gépen csak gép resinél! )
 * 
 * 
 * A login-t figyelni kéne, mert azzal tudnánk beragt party-t törölni - Jó kérdés hogy lehet e
 * 
 * Vissza lehessen nézni a fightokat! A dps meter atljan legyen vissza fele nyil. Ha máshol is használható ott is!
 * Ha vissza mentünk másik fighht értelem szerűen, azt írjuk a Details-be és a Timelineon! - Jó ötlet még mindig 
 * 
 * A spellek level szintjének vissza fejtése, az id végén található számokból. (Pontosabban azaz extra amit rárakunk! 
 *  Ha ezis megvan, mehet a dps meter-re és a timeline-ra.
 *
 *
 *
 *  NFG fileok titkosítása, valahogy úgy hogy a program tudja csak vissza fejteni
 *  De ha a programot vissza fejtik ne tudják az NFG-t xD
 *  Illetve ha fentvan githubon, az NFG Loader marad, de amivel kibontja az nem! - Tökéletes
 */
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
    private string _currentMobName;
    public static PetsDatabase? _ElyosPets;
    private readonly Dictionary<int, (int Level, int Soul)> _ownedPets = new();
    private bool _petCollectionReady;
    public static MobsDatabase? _mobsDatabaseStatic;
    private readonly HashSet<uint> _pendingSummons = new();
    private string _characterName = "Várakozás a karakterre";
    private Bitmap? _selfClassIcon;
    private string _selfClassName = string.Empty;
    private string _selfClassColor = "#9CA3AF";
    private ForgeDataLoader _forgeDataLoader;
    private const double FightEndGraceSeconds = 0.75;

    private bool _fightEndPending;
    private long _fightEndRequestedAt;
    private long _fightLastHitTimestamp;

    private readonly HashSet<uint> _fightTargets = new();
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
        public DamageHit Hit { get; init; }
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
        public long TotalHealing { get; set; }

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
    }
    

    private readonly Dictionary<uint, PlayerDamageStats> _damageByPlayer = new();

    private long _fightStartTimestamp;
    private bool _fightActive;
    private long _lastDamageTimestamp;
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
        Opcode opcode = frame.Opcode;
        byte[] paylod = frame.Payload;
        
        if (_SelfId == 0)
            return;
        
        if (opcode == OpcodeId.EnterCombat)
        {
            if (CombatStartFrameParser.TryParse(paylod, out var start))
            {
                
                if (start.actorId == _SelfId)
                {
                    int _mobTemplateId = _KnowMobs.TryGetValue(start.targetId,  out var mobTemplate) ? mobTemplate : 0;
                    string? _mobName = _forgeDataLoader._mobsDatabase?.GetById(_mobTemplateId).Name;
                    DebugConsole.Log("damage",$"[DPS] Fight started. Target mob: {_mobName}");
                    if (_fightEndPending)
                    {
                        if (_fightTargets.Contains(start.targetId))
                            return;
                        FinalizeCombat();
                    }
                    _MobsInCombat.Add(start.targetId);
                }
            }
        }
    }

    private void CombatEndCheck(WireFrame frame)
    {
        Opcode opcode = frame.Opcode;
        byte[] paylod = frame.Payload;
        
        if (opcode == OpcodeId.HP)
        {
            if (HpFrameParser.TryParse(paylod, out var hp))
            {
               
                if(hp != null && hp.Target == 0)
                    return;
                
                if (_MobsInCombat.Contains(hp.Target) && hp.Health == 0)
                {
                    DebugConsole.Log("damage",$"Mob {hp.Target} removed. HP: {hp.Health}");
                    _MobsInCombat.Remove(hp.Target);
                    StopCombat();
                }
            }
        }
        else if (opcode == OpcodeId.LeaveCombat || opcode == OpcodeId.DisengageCombat)
        {
            if (CombatEndFrameParser.TryParse(paylod, out var id))
            {
                if(id == _SelfId)
                    return;
                
                if (_MobsInCombat.Contains(id))
                {
                    _MobsInCombat.Remove(id);
                    StopCombat();
                }
            }
        }
    }
    
    private void CheckCombatTimeout()
    {
        if (!_fightActive)
            return;

        long now = Stopwatch.GetTimestamp();

        if (_fightEndPending)
        {
            double waitingSeconds =
                (now - _fightEndRequestedAt) /
                (double)Stopwatch.Frequency;

            if (waitingSeconds >= FightEndGraceSeconds)
                FinalizeCombat();

            return;
        }

        if (_lastDamageTimestamp == 0)
            return;

        double inactiveSeconds =
            (now - _lastDamageTimestamp) /
            (double)Stopwatch.Frequency;

        if (inactiveSeconds < CombatInactivityTimeoutSeconds)
            return;

        _MobsInCombat.Clear();

        // Inaktivitás esetén is az utolsó hitig
        // számoljuk a fight időtartamát.
        FinalizeCombat();

        _lastDamageTimestamp = 0;
    }

    
    private void StopCombat()
    {
        if (_MobsInCombat.Count != 0)
            return;

        if (!_fightActive || _fightEndPending)
            return;

        // Még ne zárjuk le a fightot!
        _fightEndPending = true;
        _fightEndRequestedAt = Stopwatch.GetTimestamp();

        DebugConsole.Log("dpstime",
            "[COMBAT END PENDING] No active mobs. " +
            "Waiting for late damage.");
    }

    private void FinalizeCombat()
    {
        if (!_fightActive)
            return;

        // Nem a 750 ms várakozás végéig számoljuk a DPS-t!
        _fightEndTimestamp = Math.Max(
            _fightStartTimestamp,
            _fightLastHitTimestamp);

        _fightActive = false;
        _fightEndPending = false;

        DebugConsole.Log("dpstime",
            "[DPS] Fight ended.");

        SaveFightToCombatLog(completed: true);
        CountDps();
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
            ApplyHits(DamageScanner.ScanFrame(0x04, 0x38, frame.Payload, minDamage: 1));
        else if (opcode == OpcodeId.DoT)
            ApplyHits(DamageScanner.ScanFrame(0x05, 0x38, frame.Payload, minDamage: 1));
        else if (opcode == OpcodeId.Characters)
            ApplyHits(DamageScanner.ScanFrame(0x0B, 0x39, frame.Payload, minDamage: 1));
    }

    private void ApplyHits(List<DamageHit> hits)
    {
        foreach (var hit in hits)
            ApplyDamageHit(hit);
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
        // A heal önmagában nem indíthat fight-ot.
        if (!_fightActive)
            return;

        if (hit.Damage <= 0)
            return;
        
        long timestamp = eventTimestamp ?? Stopwatch.GetTimestamp();
        
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
                IsHeal = true
            });
        }

        // Szándékosan nem módosítjuk:
        // _lastDamageTimestamp
        // _fightStart
        // _fightEnd
        // _fightActive

        // Az overlayt a másodpercenkénti meter timer frissíti. Healenként
        // teljes snapshotot építeni hosszú fightnál O(n²) allokációt okozott.
    }

    private bool isSelfOwner(uint ownerId)
    {
        return ownerId == _SelfId;
    }

    
    private void ApplyDamageHit(DamageHit hit)
    {
        bool heal = PartyMembers.Any(
            p => p.Id != 0 && p.Id == hit.Target);
        if (heal)
        {
            ApplyHealHit(hit);
            return;
        }

        if (hit.Target == hit.Actor || hit.Damage <= 0)
            return;
        
        if (_SelfId == 0)
            return;
        
        PartyMemberEntry? findPlayer = PartyMembers
            .FirstOrDefault(p => p.Id != 0 && p.Id == hit.Actor);
        
        if (findPlayer is not null)
        {
            string className = findPlayer.ClassName;
            SkillDatabase? db = GetSkillDatabase(className);

            // A skill benne van a játékos kasztjának adatbázisában.
            if (db?.GetById(hit.Skill) is not null)
            {
                // Normál feldolgozás, mehet tovább a sebzés.
            }
            else
            {
                string skillId = hit.Skill.ToString();
                string skillPrefix = skillId[..Math.Min(2, skillId.Length)];

                // A skill nincs a DB-ben, de a prefix egyezik.
                if (skillPrefix == Convert.ToString(findPlayer.SpellPrefix))
                {
                    DebugConsole.Log("skill",
                        $"[SKILL PREFIX OK] Skill={hit.Skill} | " +
                        $"Owner={findPlayer.Name} | Class={className}");

                    // Elfogadjuk, és mehet tovább a sebzés.
                }
                else
                {
                    DebugConsole.Log("skill",
                        $"[SKILL DROP] Skill={hit.Skill} | " +
                        $"Class={className} | " +
                        $"Prefix={skillPrefix} | " +
                        $"ExpectedPrefix={findPlayer.SpellPrefix}");

                    return;
                }
            }
        }
        
        uint originalActor = hit.Actor;

        uint ownerId = _summonOwners.TryGetValue(
            originalActor, out uint summonOwner)
            ? summonOwner
            : originalActor;

        bool isSelf = ownerId == _SelfId;
        bool isParty = PartyMembers.Any(
            p => p.Id != 0 && p.Id == ownerId);

        if (!isSelf && !isParty)
        {
            BufferPendingDamage(originalActor, hit);
            return;
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

        if (!OwnCharacterParser.TryParse(frame.Payload, out var self))
            return;
        
        PetCollectionBridge.BindCharacter(self.Name);
        
        if (_SelfId == self.Id &&
            string.Equals(_selfName, self.Name, StringComparison.OrdinalIgnoreCase))
            return;
        
        
        _selfName = self.Name;
        _SelfId = self.Id;
        _selfClassId = self.ClassId;
        
        PushMeter();
        
        CharacterName = string.IsNullOrWhiteSpace(self.Name)
            ? "Unknown"
            : self.Name;
        
        CharacterClass = "—";
        CharacterClassColor = "#9CA3AF";

        string classIcon = "";

        if (self.ClassId is byte jobValue)
        {
            var definition = _forgeDataLoader._classDatabase?.GetByJobValue(jobValue);

            if (definition is null)
                CharacterClass = $"Ismeretlen (job {jobValue})";

            if (definition is not null)
            {
                CharacterClass = definition.Name;
                CharacterClassColor = definition.Color;

                _selfClassName = definition.Name;
                _selfClassColor = definition.Color;

                _selfClassIcon = LoadClassIcon(definition.Icon);
            }

            DebugConsole.Log("class",
                $" RawJobValue={jobValue} | " +
                $"ResolvedClass={CharacterClass} | " +
                $"Color={CharacterClassColor} | Icon={classIcon}");
        }
        else
        {
            DebugConsole.Log("class",
                " A karakter parser nem adott vissza ClassId értéket.");
        }

        DebugConsole.Log("self",
            $" Name={_selfName} | ID={_SelfId} | " +
            $"Level={self.Level} | GS={self.Gs} | Class={CharacterClass}");
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

        return PartyMembers.FirstOrDefault(p => p.Id == id).Name ?? "?";
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




    private static List<string> ExtractReadableText(byte[] payload)
    {
        var results = new List<string>();
        var current = new System.Text.StringBuilder();

        void Flush()
        {
            if (current.Length >= 3)
                results.Add(current.ToString());

            current.Clear();
        }

        foreach (byte b in payload)
        {
            if (b >= 0x20 && b <= 0x7E)
            {
                current.Append((char)b);
            }
            else
            {
                Flush();
            }
        }

        Flush();
        return results.Distinct().ToList();
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
            ? "Nincs kiválasztott hálózati adapter"
            : $"Adapter: {SelectedCaptureDevice.DisplayName}";
    public ObservableCollection<LogEntry> LogEntries { get; } = new();
    public ObservableCollection<EndpointStats> Endpoints { get; } = new();

    public int LogCount => LogEntries.Count;

    private void AddLogEntry(LogEntry entry)
    {
        LogEntries.Insert(0, entry);

        // Ne nőjön korlátlanul a napló.
        while (LogEntries.Count > 500)
            LogEntries.RemoveAt(LogEntries.Count - 1);

        OnPropertyChanged(nameof(LogCount));
    }
    
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

        _petCollectionReady = true;

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
        _petCollectionReady = true;

        PetCollectionBridge.Publish(
            petList.Pets.Select(p =>
                new PetFarmOwnedState(
                    p.PetId,
                    p.Level,
                    p.SoulProgress)));

        Console.WriteLine(
            $"[PET BRIDGE] Published={PetCollectionBridge.Snapshot?.Count ?? 0}");
    }

    private static bool TryReadPetVarint(
        ReadOnlySpan<byte> data,
        ref int pos,
        out int value)
    {
        value = 0;

        for (int i = 0; i < 5 && pos < data.Length; i++)
        {
            byte b = data[pos++];

            if (i == 4 && (b & 0xF8) != 0)
                return false;

            value |= (b & 0x7F) << (i * 7);

            if ((b & 0x80) == 0)
                return true;
        }

        return false;
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
            StatusText = "Nem található hálózati adapter";

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
            StatusText = "Nincs kijelölt hálózati adapter.";
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
            StatusText = "Capture running";
            StatusColor = "#34D399";
        }
        catch (Exception ex)
        {
            _tcpStreamDecoder = null;
            IsCapturing = false;
            StatusText = $"Capture hiba: {ex.Message}";
            StatusColor = "#F87171";
        }
    }
    
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
            StatusText = $"Leállítási hiba: {ex.Message}";
            StatusColor = "#F87171";
            return;
        }

        IsCapturing = false;
        StatusText = "Capture stopped";
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

        _lastDamageTimestamp = Stopwatch.GetTimestamp();

        bool fightJustStarted = false;

        // Ha már lezárásra várunk, de új célpontot
        // támadunk, az új fight legyen.
        if (_fightActive &&
            _fightEndPending &&
            !_fightTargets.Contains(hit.Target))
        {
            FinalizeCombat();
        }

        if (!_fightActive)
        {
            _damageByPlayer.Clear();
            _fightTargets.Clear();

            _fightStartTimestamp = hitTimestamp;
            _fightLastHitTimestamp = hitTimestamp;
            _fightEndTimestamp = 0;

            _fightActive = true;
            _fightEndPending = false;
            fightJustStarted = true;

            DebugConsole.Log("dpstime",
                "[DPS] Új fight indult.");
        }
        else if (hitTimestamp < _fightStartTimestamp)
        {
            _fightStartTimestamp = hitTimestamp;
        }

        _fightTargets.Add(hit.Target);

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
            });
        }

        if (fightJustStarted)
            ShowDpsOverlayForCombat();
    }

    private void ShowDpsOverlayForCombat()
    {
        if (!CompanionSettings.AutoManageDpsOverlay)
            return;

        _dpsOverlayHideTimer.Stop();

        OverlayWindowManager.ShowDps(BuildSnapshot());
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
    
public MeterSnapshot BuildSnapshot()
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
            duration,
            _fightStartTimestamp,
            db));
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
            Duration = duration,
            Damage = 0,
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
            Duration = duration,
            Damage = 0,
            Healing = 0
        });
    }

    return new MeterSnapshot
    {
        Players = players,
        Duration = duration,
        MobMaxHp = 0,
        MobTemplateName = mobName,
        IsDemo = false
    };
}
private string GetCurrentMobName()
{
    
    foreach (uint runtimeId in _MobsInCombat)
    {
        if (_KnowMobs.TryGetValue(runtimeId, out int templateId) &&
            _forgeDataLoader._mobsDatabase?.GetById(templateId) is { } mob)
        {
            _currentMobName = mob.Name;
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
            "Assasin" => _forgeDataLoader._asssasinDatabase,
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
        if (db is null)
            return null;

        var exact = db.GetById(skillId);

        if (exact is not null)
            return exact;

        string id = skillId.ToString(
            CultureInfo.InvariantCulture);

        if (id.Length < 4)
            return null;

        string prefix = id[..4];

        return db.Skills.FirstOrDefault(s =>
            s.Id.StartsWith(prefix, StringComparison.Ordinal));
    }
    private static PlayerStat ToPlayerStat(
        PlayerDamageStats st,
        double duration,
        long fightStart,
        SkillDatabase? skillDatabase)
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
            Damage = st.TotalDamage,
            Healing = st.TotalHealing,
            Events = st.SnapshotEvents,
        };
    }

    private void PushMeter()
    {
        if (OverlayWindowManager.DpsWindow is not { IsVisible: true })
            return;

        OverlayWindowManager.UpdateDps(BuildSnapshot());
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
        
        if (opcode == OpcodeId.DisengageCombat || opcode == OpcodeId.HP)
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
            CombatEndCheck(frame);
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
