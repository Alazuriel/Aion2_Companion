namespace AION_2_Companion.Core.Phaseward;

// ForgeDataLoader is the single owner of the authenticated Phaseward NXFG pack.
// This catalog is read-only; it never creates a second game-packet pipeline.
public static class PhasewardCatalog
{
    private static PhasewardPack? _pack;

    public static void Initialize(PhasewardPack? pack) => _pack = pack;

    // NPC-ID matching must be unambiguous. Never infer from boss display names.
    public static PhasewardEncounter? Find(int bossNpcId) => _pack?.Find(bossNpcId);
}
