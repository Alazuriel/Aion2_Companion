using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace AION_2_Companion.Core.GameData;

/// <summary>
/// Rebuilds class icons for saved combat encounters. Bitmap references are
/// deliberately not serialized with NSL files; ClassId/ClassName are retained.
/// No network requests, NSL rewrites or changes to live DPS icon handling.
/// </summary>
public static class SavedClassIconResolver
{
    private static readonly HashSet<string> BundledClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "assassin", "chanter", "cleric", "elementalist",
        "gladiator", "ranger", "sorcerer", "templar",
    };

    private static readonly Lazy<ClassDatabase?> Database = new(() =>
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Data", "ForgeData", "AION2_Class.nfg");
            return File.Exists(path) ? ClassDatabase.Load(path) : null;
        }
        catch
        {
            // When ForgeData is absent, the shipped class images can still be
            // resolved from the saved class name.
            return null;
        }
    });

    private static readonly ConcurrentDictionary<string, Bitmap> Images =
        new(StringComparer.OrdinalIgnoreCase);

    public static Bitmap? Resolve(int classId, string? className)
    {
        ClassDatabase? db = Database.Value;
        ClassDefinition? definition = classId is >= 0 and <= byte.MaxValue
            ? db?.GetByJobValue((byte)classId)
            : null;

        if (definition is null && !string.IsNullOrWhiteSpace(className))
        {
            definition = db?.GetByKey(className.Trim())
                ?? db?.Classes.FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, className.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        // Prefer the NFG-defined embedded resource, as in the live meter.
        if (TryBundledImage(definition?.Icon) is { } configuredIcon)
            return configuredIcon;

        // Historical logs may have a class name but lack a useful numeric ID.
        string key = (definition?.Key ?? className ?? string.Empty).Trim();
        if (BundledClasses.Contains(key))
            return TryBundledImage($"Data/Assets/Classes/{key.ToLowerInvariant()}.png");

        return null;
    }

    private static Bitmap? TryBundledImage(string? iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath))
            return null;

        string relative = iconPath.Trim().Replace('\\', '/').TrimStart('/');
        if (relative.Contains("://", StringComparison.Ordinal) ||
            relative.Contains("..", StringComparison.Ordinal))
            return null;

        if (Images.TryGetValue(relative, out Bitmap? cached))
            return cached;

        try
        {
            string? assemblyName = Assembly.GetExecutingAssembly().GetName().Name;
            var uri = new Uri($"avares://{assemblyName}/{relative}");
            using Stream stream = AssetLoader.Open(uri);
            var bitmap = new Bitmap(stream);
            return Images.GetOrAdd(relative, bitmap);
        }
        catch
        {
            // The caller renders the class letter if the icon does not exist.
            return null;
        }
    }
}
