using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using AION_2_Companion.Core.GameData;
using AION_2_Companion.Core.Wire;
using AION_2_Companion.Core.Phaseward;

namespace AION_2_Companion.Core.GameData;

public class ForgeDataLoader
{
    public ClassDatabase? _classDatabase;
    public MobsDatabase? _mobsDatabase;
    public BossesDatabase? _bossesDatabase;
    public PetsDatabase? _elyosPetsDatabase;
    public SkillDatabase? _asssasinDatabase;
    public SkillDatabase? _chanterDatabase;
    public SkillDatabase? _clericDatabase;
    public SkillDatabase? _elementalistDatabse;
    public SkillDatabase? _gladiatorDatabase;
    public SkillDatabase? _rangerDatabase;
    public SkillDatabase? _sorcererDatabase;
    public SkillDatabase? _templarDatabase;
    public OpCodesDatabase? _opcodesDatabase;
    public EventTimersDatabase? _eventTimersDatabase;
    public ServersDatabase? _serversDatabase;
    public PhasewardPack? _phasewardPack;
    public PhasewardPack? _researchPreviewPack;
    public string PhasewardResearchPreviewStatus { get; private set; } = "Disabled";
    public string PhasewardLoadStatus { get; private set; } = "Phaseward pack not loaded";
    
    private static T? TryLoad<T>(string path, Func<string, T> load, Func<T, string> info)
        where T : class
    {
        string file = Path.GetFileName(path);

        try
        {
            T db = load(path);
            DebugConsole.Log("forgedata", $"[ForgeData] {file,-22} betöltve | {info(db)}");
            return db;
        }
        catch (Exception ex)
        {
            DebugConsole.Log("error", $"[ForgeData] {file,-22} HIBA | {ex.Message}");
            return null;
        }
    }
    public void DataLoader()
    {
        string dataDir = Path.Combine(AppContext.BaseDirectory, "Data", "ForgeData");
        
        _classDatabase = TryLoad<ClassDatabase>(
            Path.Combine(dataDir, "AION2_Class.nfg"),
            ClassDatabase.Load,
            db => $"Classok: {db.Classes.Count}");

        _asssasinDatabase = TryLoad<SkillDatabase>(
            Path.Combine(dataDir, "AION2_Assassin.nfg"),
            SkillDatabase.Load,
            db => $"Skillek: {db.Skills.Count}");

        _chanterDatabase = TryLoad<SkillDatabase>(
            Path.Combine(dataDir, "AION2_Chanter.nfg"),
            SkillDatabase.Load,
            db => $"Skillek: {db.Skills.Count}");

        _clericDatabase = TryLoad<SkillDatabase>(
            Path.Combine(dataDir, "AION2_Cleric.nfg"),
            SkillDatabase.Load,
            db => $"Skillek: {db.Skills.Count}");

        _elementalistDatabse = TryLoad<SkillDatabase>(
            Path.Combine(dataDir, "AION2_Elementalist.nfg"),
            SkillDatabase.Load,
            db => $"Skillek: {db.Skills.Count}");

        _gladiatorDatabase = TryLoad<SkillDatabase>(
            Path.Combine(dataDir, "AION2_Gladiator.nfg"),
            SkillDatabase.Load,
            db => $"Skillek: {db.Skills.Count}");

        _rangerDatabase = TryLoad<SkillDatabase>(
            Path.Combine(dataDir, "AION2_Ranger.nfg"),
            SkillDatabase.Load,
            db => $"Skillek: {db.Skills.Count}");

        _sorcererDatabase = TryLoad<SkillDatabase>(
            Path.Combine(dataDir, "AION2_Sorcerer.nfg"),
            SkillDatabase.Load,
            db => $"Skillek: {db.Skills.Count}");

        _templarDatabase = TryLoad<SkillDatabase>(
            Path.Combine(dataDir, "AION2_Templar.nfg"),
            SkillDatabase.Load,
            db => $"Skillek: {db.Skills.Count}");

        _mobsDatabase = TryLoad<MobsDatabase>(
            Path.Combine(dataDir, "AION2_Mobs.nfg"),
            MobsDatabase.Load,
            db => $"Mobok: {db.Mobs.Count}");

        string bossesPath = Path.Combine(dataDir, "AION2_Bosses.nfg");
        _bossesDatabase = File.Exists(bossesPath)
            ? TryLoad<BossesDatabase>(
                bossesPath,
                BossesDatabase.Load,
                db => $"Bossok: {db.Bosses.Count}")
            : null;

        _elyosPetsDatabase = TryLoad<PetsDatabase>(
            Path.Combine(dataDir, "AION2_Pets.nfg"),
            PetsDatabase.Load,
            db => $"Petek: {db.Pets.Select(x => x.PetId).Distinct().Count()} | Spawnok: {db.Pets.Count}");

        _opcodesDatabase = TryLoad<OpCodesDatabase>(
            Path.Combine(dataDir, "AION2_Opcodes.nfg"),
            OpCodesDatabase.Load,
            db => $"Opcode-ok: {db.Opcodes.Count}");

        _eventTimersDatabase = TryLoad<EventTimersDatabase>(
            Path.Combine(dataDir, "AION2_EventTimers.nfg"),
            EventTimersDatabase.Load,
            db => $"Események: {db.Events.Count} | Régiók: {db.Regions.Count}");

        _serversDatabase = TryLoad<ServersDatabase>(
            Path.Combine(dataDir, "AION2_Servers.nfg"),
            ServersDatabase.Load,
            db => $"Szerverek: {db.Servers.Count}");

        // Same ForgeData directory and TryLoad logging as the other NXFG packs.
        // PhasewardPack.Load uses ForgeDataReader.LoadPayloadJson, which authenticates
        // NXFG v1 (SHA-256) and v2 (AES-GCM) through the existing decoder.
        string[] phasewardNames = { "AION2_Phaseward.nfg", "AION2_DBM.nfg" };
        string? phasewardPath = phasewardNames
            .Select(name => Path.Combine(dataDir, name)).FirstOrDefault(File.Exists);
        _phasewardPack = phasewardPath is null ? null : TryLoad<PhasewardPack>(
            phasewardPath, PhasewardPack.Load,
            pack => $"Phaseward bosses: {pack.Encounters.Count} | ambiguous NPC IDs: {pack.AmbiguousBossNpcIds.Count}");
        PhasewardLoadStatus = phasewardPath is null
            ? "AION2_Phaseward.nfg not installed"
            : _phasewardPack is null
                ? $"Phaseward NFG failed to load ({Path.GetFileName(phasewardPath)}); see ForgeData error log"
                : $"Phaseward loaded: {_phasewardPack.Encounters.Count} bosses; " +
                  $"{_phasewardPack.AmbiguousBossNpcIds.Count} ambiguous NPC IDs excluded ({Path.GetFileName(phasewardPath)})";
        // Optional local research preview: a deliberately named JSON sidecar.
        // Original authenticated Phaseward NFG remains the authoritative default.
        _researchPreviewPack = null;
        string previewPath = Path.Combine(dataDir, "AION2_Phaseward.research-test.json");
        if (File.Exists(previewPath))
        {
            try
            {
                var previewInfo = new FileInfo(previewPath);
                if (previewInfo.Length is <= 0 or > 2_000_000)
                    throw new InvalidDataException("Research preview JSON size is invalid.");
                using JsonDocument previewJson = JsonDocument.Parse(File.ReadAllText(previewPath));
                if (previewJson.RootElement.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("Research preview requires an array of boss candidates.");
                int candidates = 0;
                foreach (JsonElement boss in previewJson.RootElement.EnumerateArray())
                {
                    if (boss.ValueKind != JsonValueKind.Object ||
                        !boss.TryGetProperty("source_kind", out JsonElement kind) ||
                        kind.ValueKind != JsonValueKind.String ||
                        kind.GetString() != "research_candidate")
                        throw new InvalidDataException("Only research_candidate records are allowed in preview.");
                    candidates++;
                }
                if (candidates is < 1 or > 100)
                    throw new InvalidDataException("Research preview candidate count is invalid.");
                _researchPreviewPack = PhasewardPack.FromJson(previewJson.RootElement);
                PhasewardResearchPreviewStatus = $"Research loaded: {_researchPreviewPack.Encounters.Count} boss candidates (experimental)";
                // Research load diagnostics are developer-only; loading is unchanged.
                if (AION_2_Companion.Program.DeveloperMode)
                    DebugConsole.Log("forgedata", $"[Phaseward] {PhasewardResearchPreviewStatus}");
            }
            catch (Exception ex)
            {
                PhasewardResearchPreviewStatus = $"Research preview disabled: {ex.Message}";
                DebugConsole.Log("error", $"[Phaseward] {PhasewardResearchPreviewStatus}");
            }
        }
        PhasewardCatalog.Initialize(_phasewardPack);

        OpcodeCatalog.Initialize(_opcodesDatabase);
        BossCatalog.Initialize(_bossesDatabase);

    }
    
}
