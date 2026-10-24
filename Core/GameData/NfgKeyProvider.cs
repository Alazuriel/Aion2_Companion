namespace AION_2_Companion.Core.GameData;

using System;
using System.IO;
using System.Reflection;

internal static class NfgKeyProvider
{
    private const int KeySize = 32;
    private const string EnvironmentVariable = "NEXFORGE_NFG_MASTER_KEY";
    private const string ResourceName = "Nexforge.NfgMasterKey";

    public static byte[] LoadMasterKey()
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
        if (stream is not null)
        {
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return DecodeKeyMaterial(buffer.ToArray(), ResourceName);
        }

        string? configured = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
            return DecodeKeyMaterial(System.Text.Encoding.ASCII.GetBytes(configured), EnvironmentVariable);

        throw new InvalidDataException(
            "A build nem tartalmaz Nexforge NFG kulcsot. Fejlesztéshez állítsd be a " +
            $"{EnvironmentVariable} környezeti változót; hivatalos buildhez add hozzá a privát kulcs-erőforrást.");
    }

    private static byte[] DecodeKeyMaterial(byte[] raw, string source)
    {
        if (raw.Length == KeySize)
            return raw;

        string text = System.Text.Encoding.ASCII.GetString(raw).Trim();
        try
        {
            byte[] base64 = Convert.FromBase64String(text);
            if (base64.Length == KeySize)
                return base64;
        }
        catch (FormatException)
        {
            // A hex formátumot még megpróbáljuk.
        }

        try
        {
            byte[] hex = Convert.FromHexString(text);
            if (hex.Length == KeySize)
                return hex;
        }
        catch (FormatException)
        {
            // Az egységes hibaüzenet lent készül.
        }

        throw new InvalidDataException($"Az NFG mesterkulcs nem 32 bájtos: {source}.");
    }
}
