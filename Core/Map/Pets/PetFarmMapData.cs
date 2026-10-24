using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Drawing;

namespace AION_2_Companion.Core.Map.Pets;

// Normalized, flat spawn record. Deliberately NOT grouped by pet.
// ImageX/Y are pixel coordinates in the same JPG used by MapV2Engine.
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

/// <summary>
/// Adapter for existing PetsDatabase.Pets without guessing the namespace
/// or C# spelling of PetsDefiniton. Uses reflection ONCE during import,
/// never during tracking or rendering.
/// </summary>
public static class PetFarmMapImporter
{
    public static PetFarmImportResult FromRecords(IEnumerable records, string mapsDirectory)
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
        Dictionary<string, Func<object, object?>>? getters = null;
        Type? currentType = null;

        foreach (object? record in records)
        {
            seen++;
            if (record is null) { invalid++; continue; }
            Type t = record.GetType();
            if (getters is null || currentType != t)
            {
                getters = BuildGetters(t);
                currentType = t;
            }

            string mapPath = PetFarmMapCatalog.Normalize(
                ReadText(record, getters, "MapId", "Map", "MapName", "WorldMap", "WorldId"));
            // Only exact main-map records belong on a main map. Instance layers and
            // dungeons have their own coordinate spaces and must never be mixed in.
            if (!references.TryGetValue(mapPath, out var reference))
            {
                otherMap++;
                continue;
            }

            if (!TryNumber(record, getters, out double x, "X", "WorldX", "SpawnX", "PosX", "PositionX", "CoordX") ||
                !TryNumber(record, getters, out double y, "Y", "WorldY", "SpawnY", "PosY", "PositionY", "CoordY"))
            {
                invalid++;
                continue;
            }
            TryNumber(record, getters, out double z, "Z", "WorldZ", "SpawnZ", "PosZ", "PositionZ", "CoordZ");
            TryNumber(record, getters, out double petId, "PetId", "PetID", "Pet_id");
            TryNumber(record, getters, out double monsterId, "MonsterId", "MonsterID", "Monster_id", "MobId", "NpcId");
            string name = ReadText(record, getters, "PetName", "Pet_name", "Name");
            string category = ReadText(record, getters, "Category", "PetCategory", "PetType", "Type");

            if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z) ||
                !double.IsFinite(petId) || !double.IsFinite(monsterId) ||
                petId < 0 || petId > int.MaxValue || monsterId < 0 || monsterId > long.MaxValue)
            {
                invalid++;
                continue;
            }

            int pid = (int)petId;
            long mid = (long)monsterId;
            if (!unique.Add((mapPath, pid, mid, x, y, z)))
            {
                duplicates++;
                continue;
            }

            var (px, py) = PetFarmMapCalibration.WorldToReference(
                x, y, reference.Width, reference.Height);
            result.Add(new PetFarmMapPoint(pid, string.IsNullOrWhiteSpace(name) ? $"Pet #{pid}" : name,
                string.IsNullOrWhiteSpace(category) ? "Unknown" : category,
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

    private static Dictionary<string, Func<object, object?>> BuildGetters(Type t)
    {
        var dict = new Dictionary<string, Func<object, object?>>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetIndexParameters().Length != 0 || prop.GetMethod is null) continue;
            dict[Normalize(prop.Name)] = obj => prop.GetValue(obj);
        }
        foreach (var field in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
            dict[Normalize(field.Name)] = obj => field.GetValue(obj);
        return dict;
    }

    private static string Normalize(string s) =>
        new string(s.Where(char.IsLetterOrDigit).ToArray());

    private static object? Get(object obj, Dictionary<string, Func<object, object?>> getters, params string[] aliases)
    {
        foreach (string alias in aliases)
            if (getters.TryGetValue(Normalize(alias), out var getter))
                return getter(obj);
        return null;
    }

    private static string ReadText(object obj, Dictionary<string, Func<object, object?>> getters, params string[] aliases) =>
        Convert.ToString(Get(obj, getters, aliases), CultureInfo.InvariantCulture) ?? "";

    private static bool TryNumber(object obj, Dictionary<string, Func<object, object?>> getters,
        out double value, params string[] aliases)
    {
        object? raw = Get(obj, getters, aliases);
        if (raw is null) { value = 0; return false; }
        return double.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture),
            NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
