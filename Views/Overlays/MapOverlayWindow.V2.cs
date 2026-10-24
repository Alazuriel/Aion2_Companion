using System;
using AION_2_Companion.Core.Map.V2;

namespace AION_2_Companion.Views.Overlays;

// The V2 matcher only supplies the reference-map transform to active overlay modules.
// Legacy YAML and prototype/test markers are intentionally not rendered.
public partial class MapOverlayWindow
{
    private int _matchedFrames;

    public void HideTrackedMap()
    {
        HidePetFarm();
    }

    public void SetTrackedMap(MapV2Result? frame)
    {
        if (frame is null)
        {
            HideTrackedMap();
            return;
        }

        SetPetFarmFrame(frame);
        if (++_matchedFrames % 8 == 1)
        {
            Console.WriteLine(
                $"[MAP V2] MATCH | inliers={frame.Transform.Inliers} | " +
                $"err={frame.Transform.MedianErrorPx:F1}px | " +
                $"scale={frame.Transform.Scale:F4}");
        }
    }
}
