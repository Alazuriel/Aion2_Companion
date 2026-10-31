namespace AION_2_Companion.Core;

using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class NslCombatLogCodec
{
    private static readonly byte[] Magic = "NXSL"u8.ToArray();
    private static readonly byte[] Entropy =
        SHA256.HashData(Encoding.UTF8.GetBytes("Nexforge.AION2Companion.CombatLog.v1"));
    private static readonly byte[] KeyInfo =
        Encoding.ASCII.GetBytes("Nexforge/NXSL/v1/AES-256-GCM");

    private const ushort Version = 1;
    private const ushort Flags = 0x0003;
    private const int HeaderWithoutTagSize = 44;
    private const int HeaderSize = 60;
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int MaxStoredLength = 32 * 1024 * 1024;
    private const int MaxRawLength = 64 * 1024 * 1024;

    private static readonly object KeySync = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
    };

    private static string KeyPath => Path.Combine(
        CompanionSettings.SettingsDirectory,
        "combat-log.key");

    public static void Write(string path, CombatLogEntry entry)
    {
        byte[] key = LoadOrCreateDeviceKey();
        try
        {
            WriteUsingKey(path, entry, key);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    internal static void WriteForTest(
        string path,
        CombatLogEntry entry,
        byte[] deviceKey) =>
        WriteUsingKey(path, entry, (byte[])deviceKey.Clone());

    private static void WriteUsingKey(
        string path,
        CombatLogEntry entry,
        byte[] deviceKey)
    {
        byte[] raw = JsonSerializer.SerializeToUtf8Bytes(entry, JsonOptions);
        if (raw.Length > MaxRawLength)
            throw new InvalidDataException("A harcnapló túl nagy.");

        byte[] compressed = Compress(raw);
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] fileKey = DeriveFileKey(deviceKey, salt);
        byte[] header = new byte[HeaderSize];
        byte[] ciphertext = new byte[compressed.Length];
        try
        {
            Magic.CopyTo(header, 0);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4, 2), Version);
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6, 2), Flags);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8, 4), checked((uint)ciphertext.Length));
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12, 4), checked((uint)raw.Length));
            salt.CopyTo(header, 16);
            nonce.CopyTo(header, 32);

            using var aes = new AesGcm(fileKey, TagSize);
            aes.Encrypt(
                nonce,
                compressed,
                ciphertext,
                header.AsSpan(HeaderWithoutTagSize, TagSize),
                header.AsSpan(0, HeaderWithoutTagSize));

            byte[] output = new byte[HeaderSize + ciphertext.Length];
            header.CopyTo(output, 0);
            ciphertext.CopyTo(output, HeaderSize);
            File.WriteAllBytes(path, output);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(raw);
            CryptographicOperations.ZeroMemory(compressed);
            CryptographicOperations.ZeroMemory(deviceKey);
            CryptographicOperations.ZeroMemory(fileKey);
        }
    }

    public static CombatLogEntry Read(string path)
    {
        byte[] key = LoadOrCreateDeviceKey();
        try
        {
            return ReadUsingKey(path, key);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    internal static CombatLogEntry ReadForTest(string path, byte[] deviceKey) =>
        ReadUsingKey(path, (byte[])deviceKey.Clone());

    private static CombatLogEntry ReadUsingKey(string path, byte[] deviceKey)
    {
        byte[] file = File.ReadAllBytes(path);
        if (file.Length < HeaderSize || !file.AsSpan(0, 4).SequenceEqual(Magic))
            throw new InvalidDataException("Nem érvényes Nexforge harcnapló.");

        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(4, 2));
        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(6, 2));
        if (version != Version || flags != Flags)
            throw new InvalidDataException("Nem támogatott Nexforge harcnapló-verzió.");

        uint storedLengthValue = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(8, 4));
        uint rawLengthValue = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(12, 4));
        if (storedLengthValue > MaxStoredLength || rawLengthValue > MaxRawLength)
            throw new InvalidDataException("A Nexforge harcnapló mérete hibás.");

        int storedLength = (int)storedLengthValue;
        int rawLength = (int)rawLengthValue;
        if (file.Length != HeaderSize + storedLength)
        {
            throw new InvalidDataException("A Nexforge harcnapló mérete hibás.");
        }

        ReadOnlySpan<byte> salt = file.AsSpan(16, SaltSize);
        ReadOnlySpan<byte> nonce = file.AsSpan(32, NonceSize);
        ReadOnlySpan<byte> tag = file.AsSpan(HeaderWithoutTagSize, TagSize);
        ReadOnlySpan<byte> ciphertext = file.AsSpan(HeaderSize, storedLength);
        byte[] fileKey = DeriveFileKey(deviceKey, salt);
        byte[] compressed = new byte[storedLength];
        byte[] raw = [];
        try
        {
            using var aes = new AesGcm(fileKey, TagSize);
            aes.Decrypt(
                nonce,
                ciphertext,
                tag,
                compressed,
                file.AsSpan(0, HeaderWithoutTagSize));
            raw = Decompress(compressed, rawLength);
            return JsonSerializer.Deserialize<CombatLogEntry>(raw, JsonOptions)
                ?? throw new InvalidDataException("A Nexforge harcnapló üres.");
        }
        catch (CryptographicException ex)
        {
            throw new InvalidDataException(
                "A Nexforge harcnapló sérült vagy módosították.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(deviceKey);
            CryptographicOperations.ZeroMemory(fileKey);
            CryptographicOperations.ZeroMemory(compressed);
            if (raw.Length > 0)
                CryptographicOperations.ZeroMemory(raw);
        }
    }

    private static byte[] LoadOrCreateDeviceKey()
    {
        lock (KeySync)
        {
            Directory.CreateDirectory(CompanionSettings.SettingsDirectory);
            if (File.Exists(KeyPath))
            {
                byte[] protectedKey = File.ReadAllBytes(KeyPath);
                byte[] key = ProtectedData.Unprotect(
                    protectedKey,
                    Entropy,
                    DataProtectionScope.CurrentUser);
                if (key.Length != 32)
                    throw new InvalidDataException("A helyi harcnaplókulcs sérült.");
                return key;
            }

            byte[] newKey = RandomNumberGenerator.GetBytes(32);
            byte[] protectedNewKey = ProtectedData.Protect(
                newKey,
                Entropy,
                DataProtectionScope.CurrentUser);
            string temporaryPath = KeyPath + ".tmp";
            File.WriteAllBytes(temporaryPath, protectedNewKey);
            File.Move(temporaryPath, KeyPath, overwrite: true);
            return newKey;
        }
    }

    private static byte[] DeriveFileKey(byte[] deviceKey, ReadOnlySpan<byte> salt)
    {
        using var extract = new HMACSHA256(salt.ToArray());
        byte[] pseudoRandomKey = extract.ComputeHash(deviceKey);
        try
        {
            byte[] expandInput = new byte[KeyInfo.Length + 1];
            KeyInfo.CopyTo(expandInput, 0);
            expandInput[^1] = 1;
            using var expand = new HMACSHA256(pseudoRandomKey);
            return expand.ComputeHash(expandInput);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pseudoRandomKey);
        }
    }

    private static byte[] Compress(byte[] raw)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            zlib.Write(raw);
        return output.ToArray();
    }

    private static byte[] Decompress(byte[] compressed, int expectedLength)
    {
        using var input = new MemoryStream(compressed, writable: false);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream(expectedLength);
        byte[] buffer = new byte[8192];
        int read;
        while ((read = zlib.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (output.Length + read > expectedLength)
                throw new InvalidDataException("A Nexforge harcnapló kitömörített mérete hibás.");
            output.Write(buffer, 0, read);
        }
        if (output.Length != expectedLength)
            throw new InvalidDataException("A Nexforge harcnapló kitömörített mérete hibás.");
        return output.ToArray();
    }
}
