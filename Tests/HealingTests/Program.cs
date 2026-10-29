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
    ("missing actor or skill rejected", MissingFieldsRejected),
    ("legacy combat log loads", LegacyCombatLogLoads),
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
