using System.Text.Json;
using AION_2_Companion.Core;
using AION_2_Companion.Core.Combat;
using AION_2_Companion.Core.GameData;
using AION_2_Companion.Core.Parsers;
using AION_2_Companion.Views.Overlays;

var tests = new (string Name, Action Run)[]
{
    ("real self-heal frame", RealSelfHealFrame),
    ("real party-heal frame", RealPartyHealFrame),
    ("damage is not healing", DamageIsNotHealing),
    ("MP and buff effects are not healing", NonHpEffectsAreNotHealing),
    ("multiple healing events", MultipleHealingEvents),
    ("damage and healing stay separate", DamageAndHealingStaySeparate),
    ("healing-only snapshot", HealingOnlySnapshot),
    ("multiple healers", MultipleHealers),
    ("summon healing source", SummonHealingSource),
    ("short fight rate guard", ShortFightRateGuard),
    ("duplicate frame guard", DuplicateFrameGuard),
    ("direct damage frame is counted once", DirectDamageFrameIsCountedOnce),
    ("nested damage requires an opcode marker", NestedDamageRequiresOpcodeMarker),
    ("runtime skill variants are matched narrowly", RuntimeSkillVariantsAreMatchedNarrowly),
    ("missing actor or skill rejected", MissingFieldsRejected),
    ("legacy combat log loads", LegacyCombatLogLoads),
    ("combat log keeps encounter mobs", CombatLogKeepsEncounterMobs),
    ("server ForgeData loads", ServerForgeDataLoads),
    ("NSL roundtrip and tamper detection", NslRoundTripAndTamperDetection),
    ("verified HP packet parsing", VerifiedHpPacketParsing),
    ("invalid HP cannot replace the last verified value", InvalidHpDoesNotOverwrite),
    ("boss death and inactivity close fights", CombatEndRulesWork),
    ("boss death prediction uses stable average DPS", BossDeathPredictionWorks),
};

int failures = 0;
foreach ((string name, Action run) in tests)
{
    try
    {
        run();
        Console.WriteLine($"PASS  {name}");
    }
    catch (Exception ex)
    {
        failures++;
        Console.Error.WriteLine($"FAIL  {name}: {ex.Message}");
    }
}

Console.WriteLine($"{tests.Length - failures}/{tests.Length} tests passed");
return failures == 0 ? 0 : 1;

static void RealSelfHealFrame()
{
    DamageHit hit = ParseSingle("DE640400DE646CED04010B022B97EC6502000000DC56FA080200");
    Equal(12894u, hit.Actor);
    Equal(hit.Actor, hit.Target);
    True(HealingSemantics.IsHealingHit(hit, HealingSkill("Restores 192-211 HP to the caster.")));
}

static void RealPartyHealFrame()
{
    DamageHit hit = ParseSingle("936B0400E00F419B0F0192026FA5186A01000000F056B7080100");
    True(hit.Actor != hit.Target);
    Equal(1079u, hit.Damage);
    True(HealingSemantics.IsHealingHit(hit, HealingSkill("Restores HP to the caster and party members.")));
}

static void DamageIsNotHealing()
{
    DamageHit hit = ParseSingle("88A2032600DA255A8D030174020000014B33636501000000DC56800701130100");
    False(HealingSemantics.IsHealingHit(hit, HealingSkill("Restores HP.")));
}

static void NonHpEffectsAreNotHealing()
{
    False(HealingSemantics.ProvidesHpHealing("Restores 110 MP and Stamina once on Parry."));
    False(HealingSemantics.ProvidesHpHealing("Removes Stun and grants Status Effect Immunity."));
    False(HealingSemantics.ProvidesHpHealing("Increases Incoming Heal and HP Potion Recovery."));
    True(HealingSemantics.ProvidesHpHealing("Absorbs 10% HP on hit."));
    True(HealingSemantics.ProvidesHpHealing("Has a chance to restore 5% Max HP on hit."));
}

static void MultipleHealingEvents()
{
    var player = new PlayerStat
    {
        Duration = 10,
        Healing = 300,
        Events =
        [
            new SkillEvent { Amount = 100, IsHeal = true },
            new SkillEvent { Amount = 200, IsHeal = true },
        ],
    };
    Equal(2, player.HealingEventCount);
    Equal(30d, player.Hps);
}

static void HealingOnlySnapshot()
{
    var snapshot = new MeterSnapshot
    {
        Duration = 12,
        Players =
        [
            new PlayerStat { Name = "Cleric", Duration = 12, Healing = 2400 },
        ],
    };
    Equal(0L, snapshot.TotalDamage);
    Equal(2400L, snapshot.TotalHealing);
    Equal(200d, snapshot.Players[0].Hps);
}

static void DamageAndHealingStaySeparate()
{
    var player = new PlayerStat
    {
        Duration = 10,
        Damage = 500,
        Healing = 200,
        Events =
        [
            new SkillEvent { Amount = 500 },
            new SkillEvent { Amount = 200, IsHeal = true },
        ],
    };
    Equal(1, player.DamageEventCount);
    Equal(1, player.HealingEventCount);
    Equal(50d, player.Dps);
    Equal(20d, player.Hps);
}

static void MultipleHealers()
{
    var snapshot = new MeterSnapshot
    {
        Duration = 20,
        Players =
        [
            new PlayerStat { Name = "A", Duration = 20, Healing = 500 },
            new PlayerStat { Name = "B", Duration = 20, Healing = 700 },
        ],
    };
    Equal(1200L, snapshot.TotalHealing);
}

static void SummonHealingSource()
{
    var hit = new DamageHit
    {
        Actor = 9001,
        Target = 42,
        Skill = 16770001,
        Damage = 650,
        Type = 2,
        Switch = 4,
    };
    True(HealingSemantics.IsHealingHit(hit, HealingSkill("Immediately heals the Spirit for 12% of its Max HP.")));
}

static void ShortFightRateGuard()
{
    var shortFight = new PlayerStat { Duration = 0.05, Damage = 1_000_000, Healing = 1_000_000 };
    Equal(0d, shortFight.Dps);
    Equal(0d, shortFight.Hps);
    var zeroFight = new PlayerStat { Duration = 0, Healing = 500 };
    Equal(0d, zeroFight.Hps);
}

static void DuplicateFrameGuard()
{
    DamageHit hit = ParseSingle("DE640400DE646CED04010B022B97EC6502000000DC56FA080200");
    var guard = new CombatEventDeduplicator();
    DateTimeOffset now = DateTimeOffset.UtcNow;
    True(guard.ShouldAccept(hit, now));
    False(guard.ShouldAccept(hit, now.AddMilliseconds(20)));
    True(guard.ShouldAccept(hit, now.AddMilliseconds(200)));
}

static void DirectDamageFrameIsCountedOnce()
{
    const string payload = "D9E00116008611644CF4009C028000021BD76D5F01000000FC5C800B0100";
    List<DamageHit> hits = DamageScanner.ScanFrame(
        0x04, 0x38, Convert.FromHexString(payload), 1);

    Equal(1, hits.Count);
    Equal(28761u, hits[0].Target);
    Equal(2182u, hits[0].Actor);
    Equal(16010340u, hits[0].Skill);
    Equal(1408u, hits[0].Damage);
}

static void NestedDamageRequiresOpcodeMarker()
{
    byte[] record = Convert.FromHexString(
        "D9E00116008611644CF4009C028000021BD76D5F01000000FC5C800B0100");
    byte[] wrapped = [0xAA, 0x04, 0x38, .. record, 0xBB];

    Equal(1, DamageScanner.ScanFrame(0x0B, 0x39, wrapped, 1).Count);
    Equal(0, DamageScanner.ScanFrame(0x0B, 0x39, record, 1).Count);
}

static void RuntimeSkillVariantsAreMatchedNarrowly()
{
    var constructor = typeof(SkillDatabase).GetConstructor(
        System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.NonPublic,
        binder: null,
        [typeof(SkillDefinition[])],
        modifiers: null) ?? throw new InvalidOperationException("SkillDatabase constructor missing.");

    var database = (SkillDatabase)constructor.Invoke(
    [
        new SkillDefinition[]
        {
            new() { Id = "16010000", Name = "Cold Shock" },
            new() { Id = "16150000", Name = "Jointstrike" },
            new() { Id = "16155100", Name = "Spirit Break" },
        },
    ]);

    Equal("Cold Shock", database.FindVariant(16010340)!.Name);
    Equal("Jointstrike", database.FindVariant(16150010)!.Name);
    Equal("Spirit Break", database.FindVariant(16155123)!.Name);
    True(database.FindVariant(16090000) is null);
}

static void MissingFieldsRejected()
{
    Equal(0, DamageScanner.ScanFrame(0x04, 0x38, [1, 4, 0, 0, 0, 0], 1).Count);
    var missingSkill = new DamageHit { Actor = 1, Target = 1, Damage = 10, Type = 2, Switch = 4 };
    False(HealingSemantics.IsHealingHit(missingSkill, null));
}

static void LegacyCombatLogLoads()
{
    const string json = """
        {
          "SchemaVersion": 1,
          "Duration": 10,
          "Players": [ { "Name": "Legacy", "Damage": 1234, "Events": [] } ]
        }
        """;
    CombatLogEntry? entry = JsonSerializer.Deserialize<CombatLogEntry>(json);
    True(entry is not null);
    MeterSnapshot snapshot = entry!.ToSnapshot();
    Equal(1234L, snapshot.TotalDamage);
    Equal(0L, snapshot.TotalHealing);
}

static void CombatLogKeepsEncounterMobs()
{
    var entry = new CombatLogEntry
    {
        Mobs =
        [
            new StoredEncounterMob { TemplateId = 10, Name = "Raider", Count = 2 },
            new StoredEncounterMob { TemplateId = 20, Name = "Guard", Count = 1 },
        ],
    };

    MeterSnapshot snapshot = entry.ToSnapshot();
    Equal(2, snapshot.Mobs.Count);
    Equal(3, snapshot.Mobs.Sum(mob => mob.Count));
}

static void ServerForgeDataLoads()
{
    ServersDatabase servers = ServersDatabase.Load(
        Path.Combine("Data", "ForgeData", "AION2_Servers.nfg"));
    Equal(716, servers.Servers.Count);
    Equal("Siel", servers.GetById(1101)!.Name);
}

static void NslRoundTripAndTamperDetection()
{
    string directory = Path.Combine(".tmp-build", "nsl-tests");
    Directory.CreateDirectory(directory);
    string path = Path.Combine(directory, $"{Guid.NewGuid():N}.nsl");
    byte[] key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
    try
    {
        var source = new CombatLogEntry
        {
            Id = "nsl-roundtrip",
            ServerId = 1101,
            ServerName = "Siel",
            MobName = "Test enemy",
            Duration = 12.5,
        };
        NslCombatLogCodec.WriteForTest(path, source, key);
        CombatLogEntry loaded = NslCombatLogCodec.ReadForTest(path, key);
        Equal(source.Id, loaded.Id);
        Equal(source.ServerName, loaded.ServerName);

        byte[] tampered = File.ReadAllBytes(path);
        tampered[^1] ^= 0x01;
        File.WriteAllBytes(path, tampered);
        bool rejected = false;
        try
        {
            _ = NslCombatLogCodec.ReadForTest(path, key);
        }
        catch (InvalidDataException)
        {
            rejected = true;
        }
        True(rejected);
    }
    finally
    {
        File.Delete(path);
    }
}

static void VerifiedHpPacketParsing()
{
    byte[] payload = [42, 1, 2, 3, .. BitConverter.GetBytes(160_000_000u)];
    True(HpFrameParser.TryParse(payload, out Hp? hp, out string failure));
    Equal(string.Empty, failure);
    True(hp is not null);
    Equal(42u, hp!.Target);
    Equal(160_000_000u, hp.Health);
}

static void InvalidHpDoesNotOverwrite()
{
    var tracker = new BossHpTracker();
    True(tracker.TryUpdate(42, 160_000_000, 100, 200_000_000, out BossHpSample accepted));
    Equal(160_000_000u, accepted.RawValue);
    False(tracker.TryUpdate(42, 210_000_000, 200, 200_000_000, out _));
    False(tracker.TryUpdate(42, 150_000_000, 99, 200_000_000, out _));
    True(tracker.TryGet(42, out BossHpSample retained));
    Equal(160_000_000u, retained.RawValue);

    True(tracker.TryUpdate(99, uint.MaxValue, 100, null, out BossHpSample worldBoss));
    Equal(uint.MaxValue, worldBoss.RawValue);

    byte[] invalidPayload = [42, 1, 2, 3, 0, 0, 0];
    False(HpFrameParser.TryParse(invalidPayload, out _, out string failure));
    Equal("PayloadTooShort", failure);
}

static void CombatEndRulesWork()
{
    uint[] targets = [100, 200];
    var onlyAddDead = new HashSet<uint> { 200 };
    False(CombatEndRules.ShouldRequestEndForDeath(200, 100, targets, onlyAddDead));

    var bossDead = new HashSet<uint> { 100 };
    True(CombatEndRules.ShouldRequestEndForDeath(100, 100, targets, bossDead));
    False(CombatEndRules.HasReachedInactivityTimeout(1_000, 9_499, 1_000, 8.5));
    True(CombatEndRules.HasReachedInactivityTimeout(1_000, 9_500, 1_000, 8.5));
}

static void BossDeathPredictionWorks()
{
    Equal(25d, BossDeathPrediction.EstimateSeconds(50_000, 20_000, 10, true)!.Value);
    True(BossDeathPrediction.EstimateSeconds(50_000, 20_000, 2, true) is null);
    True(BossDeathPrediction.EstimateSeconds(null, 20_000, 10, true) is null);
    True(BossDeathPrediction.EstimateSeconds(50_000, 20_000, 10, false) is null);
    True(BossDeathPrediction.EstimateSeconds(50_000, 0, 10, true) is null);
}

static DamageHit ParseSingle(string hex)
{
    List<DamageHit> hits = DamageScanner.ScanFrame(0x04, 0x38, Convert.FromHexString(hex), 1);
    Equal(1, hits.Count);
    return hits[0];
}

static SkillDefinition HealingSkill(string description) => new()
{
    Id = "17100000",
    Name = "Healing fixture",
    Description = description,
};

static void True(bool condition)
{
    if (!condition) throw new InvalidOperationException("Expected true.");
}

static void False(bool condition) => True(!condition);

static void Equal<T>(T expected, T actual) where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
}
