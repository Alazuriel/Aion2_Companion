using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AION_2_Companion.Core.Characters.Verification;

public interface ICharacterVerificationStateStore
{
    Task<StoredVerificationState?> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(StoredVerificationState state, CancellationToken cancellationToken = default);
    Task DeleteAsync();
}

internal sealed class CharacterVerificationStateStore : ICharacterVerificationStateStore
{
    private static readonly byte[] Entropy = SHA256.HashData(Encoding.UTF8.GetBytes("Nexforge.AION2Companion.CharacterVerification.v1"));
    private static string StatePath => Path.Combine(CompanionSettings.SettingsDirectory, "character.verification");

    public async Task<StoredVerificationState?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(StatePath)) return null;
        try
        {
            byte[] protectedBytes = await File.ReadAllBytesAsync(StatePath, cancellationToken);
            byte[] plaintext = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            try { return JsonSerializer.Deserialize<StoredVerificationState>(plaintext); }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException)
        {
            await DeleteAsync();
            return null;
        }
    }

    public async Task SaveAsync(StoredVerificationState state, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(CompanionSettings.SettingsDirectory);
        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(state);
        try
        {
            byte[] protectedBytes = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);
            string temporary = StatePath + ".tmp";
            await File.WriteAllBytesAsync(temporary, protectedBytes, cancellationToken);
            File.Move(temporary, StatePath, true);
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    public Task DeleteAsync()
    {
        try { File.Delete(StatePath); File.Delete(StatePath + ".tmp"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return Task.CompletedTask;
    }
}
