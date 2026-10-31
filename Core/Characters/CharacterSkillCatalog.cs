using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AION_2_Companion.Core.GameData;

namespace AION_2_Companion.Core.Characters;

/// <summary>
/// Class-specific NXFG v1/v2 catalogue. Never trusts a remotely supplied icon URL
/// for the skill UI: icon and specialty art must resolve to packaged/local files.
/// Character's learned levels still come exclusively from the character API.
/// </summary>
internal static class CharacterSkillCatalog
{
    private static readonly Dictionary<string, string> FileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["assassin"] = "Assassin", ["chanter"] = "Chanter", ["cleric"] = "Cleric",
        ["elementalist"] = "Elementalist", ["spiritmaster"] = "Elementalist",
        ["gladiator"] = "Gladiator", ["ranger"] = "Ranger",
        ["sorcerer"] = "Sorcerer", ["templar"] = "Templar"
    };
    private static readonly Dictionary<string, SkillDatabase?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Sync = new();

    public static IReadOnlyList<SkillDefinition> ForClass(string? className, out string status)
    {
        string key = new string((className ?? "").Where(char.IsLetter).ToArray()).ToLowerInvariant();
        if (!FileNames.TryGetValue(key, out string? basename))
        {
            status = string.IsNullOrWhiteSpace(className) ? "Válassz karaktert a skillekhez." : $"Nincs ismert NFG ehhez a classhoz: {className}";
            return [];
        }
        string path = Path.Combine(AppContext.BaseDirectory, "Data", "ForgeData", $"AION2_{basename}.nfg");
        lock (Sync)
        {
            if (!Cache.TryGetValue(basename, out SkillDatabase? database))
            {
                if (!File.Exists(path))
                {
                    status = $"Hiányzó adatfájl: AION2_{basename}.nfg";
                    return [];
                }
                try
                {
                    database = SkillDatabase.Load(path);
                    Cache[basename] = database;
                }
                catch (Exception ex)
                {
                    // Don't poison the cache: NFG key/package may become available later.
                    status = $"Az NFG nem tölthető be: {ex.Message}";
                    return [];
                }
            }
            if (database is null)
            {
                status = "Az NFG adatbázis nem érhető el.";
                return [];
            }
            IReadOnlyList<SkillDefinition> selected = database.GetByClass(basename);
            // Each AION2_<class>.nfg is an isolated class catalogue; imported
            // records can use a display class name differing from its filename.
            if (selected.Count == 0) selected = database.Skills;
            status = $"{basename} · {selected.Count} skill az NFG-ből · karakter-szintek: API";
            return selected;
        }
    }

    public static string? LocalIcon(string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon)) return null;
        // Reject any external URL and path traversal. Only basename matters.
        string filename = Path.GetFileName(icon.Replace('\\', '/'));
        if (filename.Length == 0 || filename.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return null;
        string extension = Path.GetExtension(filename);
        if (!extension.Equals(".webp", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".png", StringComparison.OrdinalIgnoreCase)) return null;
        string local = Path.Combine(AppContext.BaseDirectory, "Data", "Assets", "icons", filename);
        if (File.Exists(local)) return local;
        // AvaloniaResource is bundled in compiled output when the icon packer
        // was run before the app was built; never falls back to remote CDN.
        string assemblyName = Uri.EscapeDataString(typeof(CharacterSkillCatalog).Assembly.GetName().Name!);
        var assets = new Uri($"avares://{assemblyName}/Data/Assets/icons/{Uri.EscapeDataString(filename)}");
        return Avalonia.Platform.AssetLoader.Exists(assets) ? assets.ToString() : null;
    }
}
