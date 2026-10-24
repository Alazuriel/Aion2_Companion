namespace AION_2_Companion.Core.GameData;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

public sealed class OpCodesDatabase
{
    private readonly Dictionary<ushort, OpCodesDefinition.OpCodes> _byValue = [];
    private readonly Dictionary<string, ushort> _byName =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<OpCodesDefinition.OpCodes> Opcodes => _byValue.Values;

    private OpCodesDatabase(OpCodesDefinition.OpCodes[] opcodes)
    {
        foreach (OpCodesDefinition.OpCodes entry in opcodes)
        {
            if (!TryParseValue(entry.Key, out ushort value) ||
                string.IsNullOrWhiteSpace(entry.Name))
            {
                throw new InvalidDataException("Hibás opcode-bejegyzés az NFG-ben.");
            }

            string name = entry.Name.Trim();
            if (!_byValue.TryAdd(value, entry))
                throw new InvalidDataException($"Duplikált opcode-érték az NFG-ben: {entry.Key}");
            if (!_byName.TryAdd(name, value))
                throw new InvalidDataException($"Duplikált opcode-név az NFG-ben: {name}");
        }
    }

    public static OpCodesDatabase Load(string path)
        => new(ForgeDataReader.LoadOpcodes(path));

    public bool TryGetValue(string name, out ushort value)
        => _byName.TryGetValue(name, out value);

    public bool Contains(ushort value)
        => _byValue.ContainsKey(value);

    public string GetName(ushort value)
        => _byValue.TryGetValue(value, out OpCodesDefinition.OpCodes? entry)
            ? entry.Name
            : "Unknown";

    public bool TryGet(ushort value, out OpCodesDefinition.OpCodes? entry)
        => _byValue.TryGetValue(value, out entry);

    private static bool TryParseValue(string text, out ushort value)
    {
        string normalized = text.Trim();
        if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[2..];
        normalized = normalized.Replace(" ", string.Empty, StringComparison.Ordinal);
        return ushort.TryParse(normalized, NumberStyles.HexNumber,
            CultureInfo.InvariantCulture, out value);
    }
}
