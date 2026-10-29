using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Drawing;
using AION_2_Companion.Core.GameData;

namespace AION_2_Companion.Core.Map.Pets;

public sealed record PetFarmMapPoint(
    int PetId,
    string PetName,
    string Category,
    string MapId,
    string MapName,
    long MonsterId,
    double WorldX,
    double WorldY,
    double WorldZ,
    double ImageX,
    double ImageY);

public sealed record PetFarmImportResult(
    PetFarmMapPoint[] Points,
    int Seen,
    int OtherMap,
    int Invalid,
    int Duplicate);

public sealed record PetFarmMapDefinition(
    string Id,
    string DisplayName,
    string AssetName);

public static class PetFarmMapCatalog
{
    public const string VerteronId = "World/World_L/World_L_A";
    public const string AltgardId = "World/World_D/World_D_A";
    public const string ReshantaId = "Intersever/Abyss/Abyss_Reshanta_A";

    public static readonly IReadOnlyList<PetFarmMapDefinition> Supported =
    [
        new(VerteronId, "Verteron", "World_L_A"),
        new(AltgardId, "Altgard", "World_D_A"),
        new(ReshantaId, "Reshanta", "Abyss_Reshanta_A")
    ];

    public static PetFarmMapDefinition? Find(string? mapId) =>
        Supported.FirstOrDefault(map =>
            string.Equals(map.Id, Normalize(mapId), StringComparison.OrdinalIgnoreCase));

    public static string Normalize(string? mapId) =>
        (mapId ?? string.Empty).Replace('\\', '/').Trim('/');
}

/// <summary>
/// Shared Aion world-map calibration: native 16384px image coordinate
/// ~= offset + scale * world coordinate, with X right and Y DOWN.
/// These numbers describe the source project's tile composite, not
/// necessarily the exact art crop of hnomkeng/World_L_A.jpg.
/// This is therefore a starting calibration, NOT a verified pixel-perfect fit.
/// </summary>
public static class PetFarmMapCalibration
{
    public const double NativeMapSize = 16384.0;
    public const double PixelsPerWorldUnit = 0.019583509634873143;
    public const double OffsetXNative = 8218.937975984913;
    public const double OffsetYNative = 8147.450753846877;

    // Keep these at zero until checked with known game positions!
    // The source calibration notes an additional (+148,-160) native-px
    // correction, measured on its own map tiles (not our JPG).
    public static double CorrectionXNative { get; set; } = 0;
    public static double CorrectionYNative { get; set; } = 0;

    public static (double X, double Y) WorldToReference(
        double worldX, double worldY, int referenceWidth, int referenceHeight)
    {
        double pxNative = OffsetXNative + CorrectionXNative +
                          PixelsPerWorldUnit * worldX;
        double pyNative = OffsetYNative + CorrectionYNative +
                          PixelsPerWorldUnit * worldY;
        return (pxNative * referenceWidth / NativeMapSize,
                pyNative * referenceHeight / NativeMapSize);
    }
}

public static class PetFarmMapImporter
{
    public static PetFarmImportResult FromRecords(
        IEnumerable<PetsDefiniton> records,
        string mapsDirectory)
    {
        if (!Directory.Exists(mapsDirectory))
            throw new DirectoryNotFoundException($"Pet Farm map folder not found: {mapsDirectory}");

        var references = new Dictionary<string, (PetFarmMapDefinition Map, int Width, int Height)>(
            StringComparer.OrdinalIgnoreCase);
        foreach (PetFarmMapDefinition map in PetFarmMapCatalog.Supported)
        {
            string path = Path.Combine(mapsDirectory, $"{map.AssetName}.jpg");
            if (!File.Exists(path))
                continue;
            using var image = Image.FromFile(path);
            references[map.Id] = (map, image.Width, image.Height);
        }

        if (references.Count == 0)
            throw new FileNotFoundException("No supported Pet Farm reference JPG was found.", mapsDirectory);

        var result = new List<PetFarmMapPoint>();
        var unique = new HashSet<(string Map, int Pet, long Monster, double X, double Y, double Z)>();
        int seen = 0, otherMap = 0, invalid = 0, duplicates = 0;
        foreach (PetsDefiniton? record in records)
        {
            seen++;
            if (record is null) { invalid++; continue; }

            string mapPath = PetFarmMapCatalog.Normalize(record.Map);
            if (!references.TryGetValue(mapPath, out var reference))
            {
                otherMap++;
                continue;
            }

            if (record.PetId < 0 || record.MonsterId < 0)
            {
                invalid++;
                continue;
            }

            int pid = record.PetId;
            long mid = record.MonsterId;
            double x = record.X;
            double y = record.Y;
            double z = record.Z;
            if (!unique.Add((mapPath, pid, mid, x, y, z)))
            {
                duplicates++;
                continue;
            }

            var (px, py) = PetFarmMapCalibration.WorldToReference(
                x, y, reference.Width, reference.Height);
            result.Add(new PetFarmMapPoint(
                pid,
                string.IsNullOrWhiteSpace(record.PetName) ? $"Pet #{pid}" : record.PetName,
                string.IsNullOrWhiteSpace(record.Category) ? "Unknown" : record.Category,
                reference.Map.Id, reference.Map.DisplayName,
                mid, x, y, z, px, py));
        }

        string mapStats = string.Join(" | ", result
            .GroupBy(point => point.MapName)
            .OrderBy(group => group.Key)
            .Select(group => $"{group.Key}={group.Count()}"));
        Console.WriteLine($"[PET FARM] Source={seen} | MainMaps={result.Count} | {mapStats} | OtherMap={otherMap} | Invalid={invalid} | Duplicate={duplicates}");
        if (result.Count == 0)
            Console.WriteLine("[PET FARM WARN] No supported main-map spawns. Check property names and MapId values in PetsDefiniton.");
        return new PetFarmImportResult(result.ToArray(), seen, otherMap, invalid, duplicates);
    }

}
