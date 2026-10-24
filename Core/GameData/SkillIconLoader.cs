using System;
using System.Collections.Concurrent;
using System.IO;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace AION_2_Companion.Core; // vagy ahova tetted

public static class SkillIconLoader
{
    private static readonly ConcurrentDictionary<string, Bitmap?> Cache = new();

    public static Bitmap? Load(string? iconPathOrUrl)
    {
        if (string.IsNullOrWhiteSpace(iconPathOrUrl))
            return null;

        return Cache.GetOrAdd(iconPathOrUrl, LoadCore);
    }

    private static Bitmap? LoadCore(string pathOrUrl)
    {
        try
        {
            // 1) Távoli URL (icon_url az nfg-ből)
            if (pathOrUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                // Opcionális: letöltés + cache. Egyelőre skip, vagy HttpClient-tel.
                // Ha nem akarod hálózatról, térj vissza null-lal.
                return null;
            }

            // 2) Relatív path az nfg-ből: "assets/icons/ICON_EL_SKILL_001.webp"
            string relative = pathOrUrl.TrimStart('/');
            relative = relative
                .Replace("assets/", "Assets/", StringComparison.OrdinalIgnoreCase)
                .Replace('\\', '/');
            // Avalonia resource
            string assemblyName = System.Reflection.Assembly
                .GetExecutingAssembly()
                .GetName()
                .Name!;

            var uri = new Uri($"avares://{assemblyName}/Data/{relative}");
            using var stream = AssetLoader.Open(uri);
            return new Bitmap(stream);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[SKILL ICON] failed '{pathOrUrl}': {ex.Message}");
            return null;
        }
    }
}