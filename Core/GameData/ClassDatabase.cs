namespace AION_2_Companion.Core.GameData;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public sealed class ClassDatabase
{
    private readonly Dictionary<byte, ClassDefinition> _byJobValue = [];

    public IReadOnlyList<ClassDefinition> Classes { get; }

    private ClassDatabase(ClassDefinition[] classes)
    {
        Classes = classes;

        foreach (var definition in classes)
        {
            if (string.IsNullOrWhiteSpace(definition.Key))
                throw new InvalidDataException("Egy class kulcsa üres.");

            if (definition.JobValues.Count == 0)
                throw new InvalidDataException(
                    $"Nincs job érték megadva: {definition.Key}");

            foreach (byte value in definition.JobValues)
            {
                if (!_byJobValue.TryAdd(value, definition))
                {
                    throw new InvalidDataException(
                        $"Duplikált job érték az NFG-ben: {value}");
                }
            }
        }
    }

    public static ClassDatabase Load(string path)
    {
        var document = ForgeDataReader.Load(path);
        return new ClassDatabase(document.Classes);
    }

    public ClassDefinition? GetByJobValue(byte jobValue)
    {
        return _byJobValue.TryGetValue(jobValue, out var definition)
            ? definition
            : null;
    }

    public ClassDefinition? GetByKey(string key)
    {
        return Classes.FirstOrDefault(
            x => string.Equals(
                x.Key, key, StringComparison.OrdinalIgnoreCase));
    }
}