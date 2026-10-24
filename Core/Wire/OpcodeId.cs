namespace AION_2_Companion.Core.Wire;

// Logikai azonosítók. A wire-értékeket kizárólag a titkosított AION2_Opcodes.nfg
// tartalmazza; az enum sorrendje nem hálózati protokolladat.
public enum OpcodeId
{
    Self,
    PlayerLegacy,
    Player,
    Owner,
    Party,
    PartyMemberUpdate,
    PartyKick,
    PartyLeave,
    PartyLeaveRemoved1,
    PartyLeaveRemoved2,
    Cast,
    HitNotice,
    Hit,
    DoT,
    EnterCombat,
    LeaveCombat,
    DisengageCombat,
    CastEnd,
    Spawn,
    Characters,
    TheFuck,
    TheFuck2,
    Death,
    HP,
    PetList,
    PetProgress,
    PetLevel,
    PetAdded,
    InventoryChange,
    Tick,
    Ping
}
