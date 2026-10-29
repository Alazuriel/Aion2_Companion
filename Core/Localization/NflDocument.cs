using System;
using System.Collections.Generic;

namespace AION_2_Companion.Core.Localization;

public sealed class NflDocument
{
    public string Version { get; init; } = "1.0";
    public Dictionary<string, string> Metadata { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, Dictionary<string, string>> Sections { get; } = new(StringComparer.Ordinal);

    public string Language => Metadata.GetValueOrDefault("language", string.Empty);
    public string Name => Metadata.GetValueOrDefault("name", Language);
    public string Author => Metadata.GetValueOrDefault("author", string.Empty);
    public string Fallback => Metadata.GetValueOrDefault("fallback", "en-US");

    public Dictionary<string, string> Flatten()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach ((string section, Dictionary<string, string> entries) in Sections)
        foreach ((string key, string value) in entries)
            result[$"{section}.{key}"] = value;
        return result;
    }
}

public sealed class NflParseException(string message, int line = 0, int column = 0) : FormatException(message)
{
    public int Line { get; } = line;
    public int Column { get; } = column;

    public override string Message => Line > 0
        ? $"{base.Message} (line {Line}, column {Column})"
        : base.Message;
}

public sealed record NflValidationIssue(string Severity, string Message, string Key = "");
