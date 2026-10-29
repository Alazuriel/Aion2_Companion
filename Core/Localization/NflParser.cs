using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AION_2_Companion.Core.Localization;

public static partial class NflParser
{
    private static readonly byte[] ContainerMagic = "NXFL"u8.ToArray();
    private const int ContainerHeaderSize = 48;
    private const ushort ContainerVersion = 1;
    private const ushort ContainerFlagZlib = 1;
    public const int MaxFileBytes = 1_048_576;
    public const int MaxLineLength = 16_384;
    public const int MaxEntries = 10_000;

    public static NflDocument ParseFile(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.Length > MaxFileBytes)
            throw new NflParseException("The language file is larger than 1 MB.");
        if (bytes.AsSpan().StartsWith(ContainerMagic))
            return Parse(DecodeContainer(bytes));
        try
        {
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            string text = utf8.GetString(bytes);
            return Parse(text.TrimStart('\uFEFF'));
        }
        catch (DecoderFallbackException exception)
        {
            throw new NflParseException("The language file is not valid UTF-8.", 0, exception.Index + 1);
        }
    }

    public static NflDocument Parse(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaxFileBytes)
            throw new NflParseException("The language file is larger than 1 MB.");

        var document = new NflDocument();
        Dictionary<string, string>? current = null;
        bool headerSeen = false;
        int entryCount = 0;
        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

        for (int index = 0; index < lines.Length; index++)
        {
            int lineNumber = index + 1;
            string raw = lines[index];
            if (raw.Length > MaxLineLength)
                throw new NflParseException("The line is too long.", lineNumber, MaxLineLength + 1);
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
                continue;

            if (!headerSeen)
            {
                Match header = HeaderPattern().Match(line);
                if (!header.Success)
                    throw new NflParseException("The file must begin with !NFL 1.0.", lineNumber, 1);
                if (!string.Equals(header.Groups[1].Value, "1.0", StringComparison.Ordinal))
                    throw new NflParseException($"Unsupported NFL version: {header.Groups[1].Value}.", lineNumber, 6);
                headerSeen = true;
                continue;
            }

            if (line.StartsWith('@'))
            {
                if (current is not null)
                    throw new NflParseException("Metadata must appear before the first section.", lineNumber, 1);
                Match metadata = MetadataPattern().Match(line);
                if (!metadata.Success)
                    throw new NflParseException("Invalid metadata line.", lineNumber, 1);
                string key = metadata.Groups[1].Value.ToLowerInvariant();
                if (document.Metadata.ContainsKey(key))
                    throw new NflParseException($"Duplicate metadata: {key}.", lineNumber, 2);
                string value = metadata.Groups[2].Value;
                document.Metadata[key] = value.StartsWith('"')
                    ? ParseQuoted(value, lineNumber, raw.IndexOf(value, StringComparison.Ordinal) + 1)
                    : value.Trim();
                continue;
            }

            if (line.StartsWith('['))
            {
                if (!line.EndsWith(']'))
                    throw new NflParseException("The section header is missing a closing bracket.", lineNumber, raw.Length);
                string section = line[1..^1].Trim();
                if (!SectionPattern().IsMatch(section))
                    throw new NflParseException("Invalid section name.", lineNumber, raw.IndexOf('[', StringComparison.Ordinal) + 2);
                if (document.Sections.ContainsKey(section))
                    throw new NflParseException($"Duplicate section: {section}.", lineNumber, 2);
                current = new Dictionary<string, string>(StringComparer.Ordinal);
                document.Sections[section] = current;
                continue;
            }

            if (current is null)
                throw new NflParseException("Translation entries must be inside a section.", lineNumber, 1);
            int equals = FindUnquotedEquals(raw);
            if (equals < 0)
                throw new NflParseException("Translation entry is missing '='.", lineNumber, 1);
            string entryKey = raw[..equals].Trim();
            if (!KeyPattern().IsMatch(entryKey))
                throw new NflParseException("Invalid translation key.", lineNumber, 1);
            if (current.ContainsKey(entryKey))
                throw new NflParseException($"Duplicate translation key: {entryKey}.", lineNumber, 1);
            current[entryKey] = ParseQuoted(raw[(equals + 1)..].Trim(), lineNumber, equals + 2);
            if (++entryCount > MaxEntries)
                throw new NflParseException($"The file contains more than {MaxEntries} translations.", lineNumber, 1);
        }

        if (!headerSeen)
            throw new NflParseException("The file is empty or has no NFL header.");
        return document;
    }

    public static string Serialize(NflDocument document)
    {
        var lines = new List<string> { "!NFL 1.0", string.Empty };
        string[] preferred = ["language", "name", "author", "fallback"];
        IEnumerable<string> metadata = preferred.Where(document.Metadata.ContainsKey)
            .Concat(document.Metadata.Keys.Where(key => !preferred.Contains(key, StringComparer.OrdinalIgnoreCase))
                .OrderBy(key => key, StringComparer.OrdinalIgnoreCase));
        foreach (string key in metadata)
            lines.Add($"@{key} {Quote(document.Metadata[key])}");
        foreach ((string section, Dictionary<string, string> values) in document.Sections.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add(string.Empty);
            lines.Add($"[{section}]");
            foreach ((string key, string value) in values.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
                lines.Add($"{key} = {Quote(value)}");
        }
        return string.Join('\n', lines).TrimEnd() + "\n";
    }

    public static byte[] SerializeFile(NflDocument document)
    {
        byte[] raw = Encoding.UTF8.GetBytes(Serialize(document));
        using var compressedStream = new MemoryStream();
        using (var zlib = new ZLibStream(compressedStream, CompressionLevel.SmallestSize, leaveOpen: true))
            zlib.Write(raw);
        byte[] compressed = compressedStream.ToArray();
        byte[] output = new byte[ContainerHeaderSize + compressed.Length];
        ContainerMagic.CopyTo(output, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(4, 2), ContainerVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(output.AsSpan(6, 2), ContainerFlagZlib);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(8, 4), checked((uint)raw.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(12, 4), checked((uint)compressed.Length));
        SHA256.HashData(raw).CopyTo(output, 16);
        compressed.CopyTo(output, ContainerHeaderSize);
        return output;
    }

    private static string DecodeContainer(byte[] file)
    {
        if (file.Length < ContainerHeaderSize)
            throw new NflParseException("The NFL file is incomplete.");
        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(4, 2));
        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(6, 2));
        uint rawLength = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(8, 4));
        uint storedLength = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(12, 4));
        if (version != ContainerVersion)
            throw new NflParseException($"Unsupported NFL container version: {version}.");
        if (flags != ContainerFlagZlib)
            throw new NflParseException("The NFL file uses unsupported features.");
        if (rawLength > MaxFileBytes || storedLength > MaxFileBytes ||
            file.Length != ContainerHeaderSize + storedLength)
            throw new NflParseException("The NFL file length is invalid.");

        byte[] raw;
        try
        {
            using var input = new MemoryStream(file, ContainerHeaderSize, checked((int)storedLength), writable: false);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream(checked((int)rawLength));
            zlib.CopyTo(output);
            raw = output.ToArray();
        }
        catch (InvalidDataException exception)
        {
            throw new NflParseException("The NFL file is damaged.", 0, 0) { Source = exception.Source };
        }
        if (raw.Length != rawLength ||
            !CryptographicOperations.FixedTimeEquals(file.AsSpan(16, 32), SHA256.HashData(raw)))
            throw new NflParseException("The NFL file failed its integrity check.");
        try
        {
            return new UTF8Encoding(false, true).GetString(raw);
        }
        catch (DecoderFallbackException exception)
        {
            throw new NflParseException("The NFL content is not valid UTF-8.", 0, exception.Index + 1);
        }
    }

    private static int FindUnquotedEquals(string line)
    {
        bool quoted = false;
        bool escaped = false;
        for (int index = 0; index < line.Length; index++)
        {
            char character = line[index];
            if (escaped) escaped = false;
            else if (character == '\\') escaped = true;
            else if (character == '"') quoted = !quoted;
            else if (character == '=' && !quoted) return index;
        }
        return -1;
    }

    private static string ParseQuoted(string value, int line, int column)
    {
        if (value.Length < 2 || value[0] != '"' || value[^1] != '"')
            throw new NflParseException("Translation values must be enclosed in double quotes.", line, column);
        var output = new StringBuilder();
        for (int index = 1; index < value.Length - 1; index++)
        {
            char character = value[index];
            if (character != '\\')
            {
                output.Append(character);
                continue;
            }
            if (++index >= value.Length - 1)
                throw new NflParseException("Incomplete escape sequence.", line, column + index);
            char escape = value[index];
            switch (escape)
            {
                case '\\': output.Append('\\'); break;
                case '"': output.Append('"'); break;
                case 'n': output.Append('\n'); break;
                case 'r': output.Append('\r'); break;
                case 't': output.Append('\t'); break;
                case 'u':
                    if (index + 4 >= value.Length)
                        throw new NflParseException("Invalid Unicode escape sequence.", line, column + index);
                    string digits = value.Substring(index + 1, 4);
                    if (!ushort.TryParse(digits, System.Globalization.NumberStyles.HexNumber,
                            System.Globalization.CultureInfo.InvariantCulture, out ushort code))
                        throw new NflParseException("Invalid Unicode escape sequence.", line, column + index);
                    output.Append((char)code);
                    index += 4;
                    break;
                default:
                    throw new NflParseException($"Unknown escape sequence: \\{escape}.", line, column + index);
            }
        }
        return output.ToString();
    }

    private static string Quote(string value) => '"' + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal).Replace("\t", "\\t", StringComparison.Ordinal) + '"';

    [GeneratedRegex(@"^!NFL\s+(\d+\.\d+)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex HeaderPattern();
    [GeneratedRegex(@"^@([A-Za-z][A-Za-z0-9_-]{0,63})\s+(.+?)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex MetadataPattern();
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex SectionPattern();
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_.-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}
