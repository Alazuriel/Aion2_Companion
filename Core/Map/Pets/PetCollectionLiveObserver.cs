using System;
using AION_2_Companion.Core.Parsers;
using AION_2_Companion.Core.Wire;

namespace AION_2_Companion.Core.Map.Pets;

/// <summary>
/// Handles incremental pet-related frames. The full 00 90 snapshot remains
/// handled by MainWindowViewModel.UpdatePetCollection().
/// </summary>
public static class PetCollectionLiveObserver
{
    public static void Observe(WireFrame frame)
    {
        if (frame.Opcode == OpcodeId.PetAdded)
        {
            if (PetAddedFrameParser.TryParse(frame.Payload, out var added) && added is not null)
                PetCollectionBridge.NotePetAdded(added.PetId, added.Level);
            else
                Console.WriteLine($"[PET LIVE PARSE ERROR] PetAdded | Len={frame.Payload.Length}");
        }
        else if (frame.Opcode == OpcodeId.PetLevel)
        {
            if (PetLevelFrameParser.TryParse(frame.Payload, out var level) && level is not null)
                PetCollectionBridge.NotePetLevel(level.PetId, level.Level);
            else
                Console.WriteLine($"[PET LIVE PARSE ERROR] PetLevel | Len={frame.Payload.Length}");
        }
        else if (frame.Opcode == OpcodeId.PetProgress)
        {
            if (PetProgressFrameParser.TryParse(frame.Payload, out var progress) && progress is not null)
            {
                foreach (var entry in progress)
                    PetCollectionBridge.NotePetProgress(entry.PetId, entry.Gained);
            }
            else
                Console.WriteLine($"[PET LIVE PARSE ERROR] PetProgress | Len={frame.Payload.Length}");
        }
    }
}
