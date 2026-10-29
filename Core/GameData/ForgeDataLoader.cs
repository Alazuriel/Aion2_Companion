using System;
using System.IO;
using System.Linq;
using AION_2_Companion.Core.GameData;
using AION_2_Companion.Core.Wire;

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

        OpcodeCatalog.Initialize(_opcodesDatabase);
        BossCatalog.Initialize(_bossesDatabase);

    }
    
}
