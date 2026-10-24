namespace AION_2_Companion.Core.Wire;

using System.Threading;
using AION_2_Companion.Core.GameData;

public static class OpcodeCatalog
{
    private static OpCodesDatabase? _database;

    public static void Initialize(OpCodesDatabase? database)
        => Volatile.Write(ref _database, database);

    public static bool Matches(Opcode opcode, OpcodeId id)
    {
        OpCodesDatabase? database = Volatile.Read(ref _database);
        return database is not null &&
               database.TryGetValue(id.ToString(), out ushort value) &&
               opcode.Value == value;
    }

    public static bool IsKnown(Opcode opcode)
        => Volatile.Read(ref _database)?.Contains(opcode.Value) == true;

    public static string GetName(Opcode opcode)
        => Volatile.Read(ref _database)?.GetName(opcode.Value) ?? "Unknown";

    public static bool TryGetInfo(
        Opcode opcode,
        out OpCodesDefinition.OpCodes? definition)
    {
        OpCodesDatabase? database = Volatile.Read(ref _database);
        if (database is not null && database.TryGet(opcode.Value, out definition))
            return true;

        definition = null;
        return false;
    }
}
