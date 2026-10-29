using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AION_2_Companion.Core.Localization;

public sealed record NflLanguagePack(
    string Code,
    string Name,
    string Author,
    string Path,
    IReadOnlyDictionary<string, string> Translations);

public static class NflLanguagePackService
{
    public static string CommunityDirectory { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Nexforge", "AION 2 Companion", "Data", "Language");

    public static IReadOnlyList<NflLanguagePack> Discover()
    {
        var byLanguage = new Dictionary<string, NflLanguagePack>(StringComparer.OrdinalIgnoreCase);
        string installed = System.IO.Path.Combine(AppContext.BaseDirectory, "Data", "Language");
        LoadDirectory(installed, byLanguage);
        LoadDirectory(CommunityDirectory, byLanguage);
        return byLanguage.Values.OrderBy(pack => pack.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public static NflLanguagePack? Find(string languageCode) => Discover().FirstOrDefault(pack =>
        string.Equals(pack.Code, languageCode, StringComparison.OrdinalIgnoreCase));

    public static NflLanguagePack Import(string sourcePath)
    {
        NflDocument document = NflParser.ParseFile(sourcePath);
        NflValidationIssue[] errors = NflValidator.Validate(document, CreateEnglishBasis())
            .Where(issue => issue.Severity == "error").ToArray();
        if (errors.Length > 0)
            throw new NflParseException(string.Join(" ", errors.Select(issue => issue.Message)));

        Directory.CreateDirectory(CommunityDirectory);
        string safeCode = string.Concat(document.Language.Select(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_'));
        string destination = System.IO.Path.Combine(CommunityDirectory, safeCode + ".nfl");
        File.Copy(sourcePath, destination, overwrite: true);
        return CreatePack(document, destination);
    }

    private static void LoadDirectory(string directory, Dictionary<string, NflLanguagePack> result)
    {
        if (!Directory.Exists(directory))
            return;
        foreach (string path in Directory.EnumerateFiles(directory, "*.nfl", SearchOption.TopDirectoryOnly))
        {
            try
            {
                NflDocument document = NflParser.ParseFile(path);
                if (NflValidator.Validate(document).Any(issue => issue.Severity == "error"))
                    continue;
                result[document.Language] = CreatePack(document, path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (NflParseException) { }
        }
    }

    private static NflLanguagePack CreatePack(NflDocument document, string path)
    {
        var translations = new Dictionary<string, string>(StringComparer.Ordinal);
        var duplicates = document.Sections.Values.SelectMany(section => section.Keys)
            .GroupBy(key => key, StringComparer.Ordinal).Where(group => group.Count() > 1)
            .Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        foreach ((string sectionName, Dictionary<string, string> section) in document.Sections)
        foreach ((string key, string value) in section)
        {
            translations[$"{sectionName}.{key}"] = value;
            if (!duplicates.Contains(key))
                translations[key] = value;
        }
        return new(document.Language, document.Name, document.Author, path, translations);
    }

    private static NflDocument CreateEnglishBasis()
    {
        var basis = new NflDocument();
        basis.Metadata["language"] = "en-US";
        basis.Metadata["name"] = "English";
        basis.Sections["Companion"] = LocalizationService.GetEnglishTemplate()
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        return basis;
    }
}
