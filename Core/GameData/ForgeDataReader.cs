namespace AION_2_Companion.Core.GameData;

using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

public sealed class ForgeDataDocument
{
    [JsonPropertyName("metadata")]
    public JsonElement Metadata { get; init; }

    [JsonPropertyName("classes")]
    public ClassDefinition[] Classes { get; init; } = [];

    [JsonPropertyName("skills")]
    public SkillDefinition[] Skills { get; init; } = [];

    [JsonPropertyName("mobs")]
    public MobDefinition[] Mobs { get; init; } = [];
    
    [JsonPropertyName("spawns")]
    public PetsDefiniton[] Pets { get; init; } = [];

    [JsonPropertyName("opcodes")]
    public OpCodesDefinition.OpCodes[] Opcodes { get; init; } = [];
}

public static class ForgeDataReader
{
    private static readonly byte[] Magic = "NXFG"u8.ToArray();
    private static readonly byte[] HkdfInfo = Encoding.ASCII.GetBytes("Nexforge/NXFG/v2/AES-256-GCM");
    private static readonly byte[] KeyIdContext = Encoding.ASCII.GetBytes("Nexforge/NXFG/v2/key-id");

    private const ushort LegacyFormatVersion = 1;
    private const ushort CurrentFormatVersion = 2;
    private const ushort FlagZlib = 0x0001;
    private const ushort FlagAesGcm = 0x0002;
    private const int LegacyHeaderSize = 48;
    private const int V2AuthenticatedHeaderSize = 52;
    private const int V2HeaderSize = 68;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeyIdSize = 8;

    private const int MaxStoredLength = 32 * 1024 * 1024;
    private const int MaxUncompressedLength = 64 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public static ForgeDataDocument Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        byte[] file = File.ReadAllBytes(path);
        if (file.Length < 8)
            throw new InvalidDataException("Az NFG fájl túl rövid.");
        if (!file.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new InvalidDataException("Nem érvényes NXFG fájl.");

        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(4, 2));
        byte[] raw = version switch
        {
            LegacyFormatVersion => DecodeLegacy(file),
            CurrentFormatVersion => DecodeEncrypted(file),
            _ => throw new InvalidDataException($"Nem támogatott NFG verzió: {version}.")
        };

        return DeserializeDocument(raw);
    }

    private static byte[] DecodeLegacy(byte[] file)
    {
        if (file.Length < LegacyHeaderSize)
            throw new InvalidDataException("Az örökölt NXFG v1 fejléc hiányos.");

        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(6, 2));
        int storedLength = CheckedLength(file.AsSpan(8, 4), MaxStoredLength, "tömörített");
        int rawLength = CheckedLength(file.AsSpan(12, 4), MaxUncompressedLength, "kitömörített");

        if (flags != FlagZlib)
            throw new InvalidDataException($"Nem támogatott örökölt NFG flag: 0x{flags:X4}.");
        EnsureExactFileLength(file.Length, LegacyHeaderSize, storedLength);

        byte[] raw = Decompress(file.AsSpan(LegacyHeaderSize, storedLength), rawLength);
        byte[] actualHash = SHA256.HashData(raw);
        if (!CryptographicOperations.FixedTimeEquals(file.AsSpan(16, 32), actualHash))
            throw new InvalidDataException("Az örökölt NFG SHA-256 ellenőrzése sikertelen.");
        return raw;
    }

    private static byte[] DecodeEncrypted(byte[] file)
    {
        if (file.Length < V2HeaderSize)
            throw new InvalidDataException("A titkosított NXFG v2 fejléc hiányos.");

        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(6, 2));
        int storedLength = CheckedLength(file.AsSpan(8, 4), MaxStoredLength, "titkosított");
        int rawLength = CheckedLength(file.AsSpan(12, 4), MaxUncompressedLength, "kitömörített");

        if (flags != (FlagZlib | FlagAesGcm))
            throw new InvalidDataException($"Nem támogatott titkosított NFG flag: 0x{flags:X4}.");
        EnsureExactFileLength(file.Length, V2HeaderSize, storedLength);

        byte[] masterKey = NfgKeyProvider.LoadMasterKey();
        byte[] fileKey = [];
        try
        {
            byte[] actualKeyId = ComputeKeyId(masterKey);
            if (!CryptographicOperations.FixedTimeEquals(file.AsSpan(16, KeyIdSize), actualKeyId))
                throw new InvalidDataException("Az NFG másik Nexforge kulccsal készült.");

            ReadOnlySpan<byte> salt = file.AsSpan(24, SaltSize);
            ReadOnlySpan<byte> nonce = file.AsSpan(40, NonceSize);
            ReadOnlySpan<byte> tag = file.AsSpan(52, TagSize);
            ReadOnlySpan<byte> ciphertext = file.AsSpan(V2HeaderSize, storedLength);
            ReadOnlySpan<byte> authenticatedHeader = file.AsSpan(0, V2AuthenticatedHeaderSize);

            fileKey = DeriveFileKey(masterKey, salt);
            byte[] compressed = new byte[storedLength];
            try
            {
                using var aes = new AesGcm(fileKey, TagSize);
                aes.Decrypt(nonce, ciphertext, tag, compressed, authenticatedHeader);
            }
            catch (CryptographicException ex)
            {
                throw new InvalidDataException(
                    "Az NFG hitelesítése sikertelen: hibás kulcs vagy módosított fájl.", ex);
            }

            return Decompress(compressed, rawLength);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(masterKey);
            if (fileKey.Length > 0)
                CryptographicOperations.ZeroMemory(fileKey);
        }
    }

    private static int CheckedLength(ReadOnlySpan<byte> bytes, int maximum, string description)
    {
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        if (value > maximum)
            throw new InvalidDataException($"Az NFG {description} payload túllépi a méretkorlátot.");
        return checked((int)value);
    }

    private static void EnsureExactFileLength(int fileLength, int headerLength, int storedLength)
    {
        if (fileLength != headerLength + (long)storedLength)
            throw new InvalidDataException("A fejlécben megadott payloadméret nem egyezik a fájlmérettel.");
    }

    private static byte[] ComputeKeyId(byte[] masterKey)
    {
        byte[] input = new byte[masterKey.Length + KeyIdContext.Length];
        try
        {
            masterKey.CopyTo(input, 0);
            KeyIdContext.CopyTo(input, masterKey.Length);
            byte[] hash = SHA256.HashData(input);
            return hash.AsSpan(0, KeyIdSize).ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
        }
    }

    private static byte[] DeriveFileKey(byte[] masterKey, ReadOnlySpan<byte> salt)
    {
        using var extract = new HMACSHA256(salt.ToArray());
        byte[] pseudoRandomKey = extract.ComputeHash(masterKey);
        try
        {
            byte[] expandInput = new byte[HkdfInfo.Length + 1];
            HkdfInfo.CopyTo(expandInput, 0);
            expandInput[^1] = 0x01;
            using var expand = new HMACSHA256(pseudoRandomKey);
            return expand.ComputeHash(expandInput);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pseudoRandomKey);
        }
    }

    private static byte[] Decompress(ReadOnlySpan<byte> compressed, int rawLength)
    {
        using var input = new MemoryStream(compressed.ToArray(), writable: false);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream(rawLength);

        byte[] buffer = new byte[8192];
        int read;
        while ((read = zlib.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (output.Length + read > MaxUncompressedLength || output.Length + read > rawLength)
                throw new InvalidDataException("A kitömörített payload nagyobb a megadott méretnél.");
            output.Write(buffer, 0, read);
        }

        if (output.Length != rawLength)
            throw new InvalidDataException("A kitömörített payload mérete hibás.");
        return output.ToArray();
    }

    private static ForgeDataDocument DeserializeDocument(byte[] raw)
    {
        ForgeDataDocument? document;
        try
        {
            using JsonDocument json = JsonDocument.Parse(raw);
            if (json.RootElement.ValueKind == JsonValueKind.Array)
            {
                SkillDefinition[] skills = JsonSerializer.Deserialize<SkillDefinition[]>(raw, JsonOptions) ?? [];
                document = new ForgeDataDocument { Skills = skills };
            }
            else if (json.RootElement.ValueKind == JsonValueKind.Object)
            {
                document = JsonSerializer.Deserialize<ForgeDataDocument>(raw, JsonOptions);
            }
            else
            {
                throw new InvalidDataException("Az NFG JSON gyökéreleme csak objektum vagy tömb lehet.");
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Az NFG payload JSON formátuma hibás.", ex);
        }

        if (document is null)
            throw new InvalidDataException("Üres vagy hibás NFG JSON.");

        bool hasClasses = document.Classes is { Length: > 0 };
        bool hasSkills = document.Skills is { Length: > 0 };
        bool hasMobs = document.Mobs is { Length: > 0 };
        bool hasSpawns = document.Pets is { Length: > 0 };
        bool hasOpcodes = document.Opcodes is { Length: > 0 };
        if (!hasClasses && !hasSkills && !hasMobs && !hasSpawns && !hasOpcodes)
            throw new InvalidDataException("Az NFG nem tartalmaz támogatott Nexforge adatot.");
        return document;
    }

    public static ClassDefinition[] LoadClasses(string path)
    {
        ForgeDataDocument document = Load(path);
        return document.Classes is { Length: > 0 } classes
            ? classes
            : throw new InvalidDataException("Az NFG nem tartalmaz classokat.");
    }

    public static SkillDefinition[] LoadSkills(string path)
    {
        ForgeDataDocument document = Load(path);
        return document.Skills is { Length: > 0 } skills
            ? skills
            : throw new InvalidDataException("Az NFG nem tartalmaz skilleket.");
    }

    public static MobDefinition[] LoadMobs(string path)
    {
        ForgeDataDocument document = Load(path);
        return document.Mobs is { Length: > 0 } mobs
            ? mobs
            : throw new InvalidDataException("Az NFG nem tartalmaz mobokat.");
    }
    public static PetsDefiniton[] LoadPets(string path)
    {
        ForgeDataDocument document = Load(path);
        return document.Pets is { Length: > 0 } pets
            ? pets
            : throw new InvalidDataException("Az NFG nem tartalmaz peteket.");
    }

    public static OpCodesDefinition.OpCodes[] LoadOpcodes(string path)
    {
        ForgeDataDocument document = Load(path);
        return document.Opcodes is { Length: > 0 } opcodes
            ? opcodes
            : throw new InvalidDataException("Az NFG nem tartalmaz opcode-okat.");
    }
}
