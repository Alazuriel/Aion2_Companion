using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AION_2_Companion.Core.GameData;

public class PetsDatabase
{
    private readonly Dictionary<string, List<PetsDefiniton>> _byName =
        new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<PetsDefiniton> Pets { get; }

    private PetsDatabase(PetsDefiniton[] pets)
    {
        Pets = pets;

        foreach (var pet in pets)
        {
            if (pet.PetId <= 0)
                throw new InvalidDataException($"Érvénytelen pet ID: {pet.PetId}");

            if (string.IsNullOrWhiteSpace(pet.PetName))
                throw new InvalidDataException($"A pet neve üres: ID {pet.PetId}");
            

            if (!_byName.TryGetValue(pet.PetName, out var sameName))
            {
                sameName = [];
                _byName[pet.PetName] = sameName;
            }

            sameName.Add(pet);
        }
    }
    public static PetsDatabase Load(string path)
    {
        return new PetsDatabase(ForgeDataReader.LoadPets(path));
    }

    public IReadOnlyList<PetsDefiniton> GetByPetName(string petName)
    {
        if (string.IsNullOrWhiteSpace(petName))
            return [];

        return _byName.TryGetValue(petName, out var pets)
            ? pets
            : [];
    }

    public IReadOnlyList<string> GetPetCategory()
    {
        return Pets
            .Select(x => x.Category)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<PetsDefiniton> Search(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<PetsDefiniton>();

        return Pets
            .Where(x => x.PetName.Contains(text, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }
}