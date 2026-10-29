using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AION_2_Companion.Core.Accounts;

internal sealed class WindowsAccountTokenStore
{
    internal sealed record StoredAccountCredential(
        string RefreshToken,
        NexforgeAccountProfile Profile);

    private static readonly byte[] Entropy =
        SHA256.HashData(Encoding.UTF8.GetBytes("Nexforge.AION2Companion.Account.v1"));

    private static string TokenPath => Path.Combine(
        CompanionSettings.SettingsDirectory,
        "account.session");

    public async Task<StoredAccountCredential?> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(TokenPath))
            return null;

        try
        {
            byte[] protectedBytes = await File.ReadAllBytesAsync(TokenPath, cancellationToken);
            byte[] plaintext = ProtectedData.Unprotect(
                protectedBytes,
                Entropy,
                DataProtectionScope.CurrentUser);

            try
            {
                return JsonSerializer.Deserialize<StoredAccountCredential>(plaintext);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   CryptographicException or JsonException)
        {
            await DeleteAsync();
            return null;
        }
    }

    public async Task SaveAsync(AccountSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (string.IsNullOrWhiteSpace(session.Tokens.RefreshToken))
            throw new InvalidOperationException("A refresh token is required for persistent sign-in.");

        Directory.CreateDirectory(CompanionSettings.SettingsDirectory);

        // Access tokens deliberately remain memory-only. Only the rotating refresh
        // credential and harmless cached identity are protected with Windows DPAPI.
        var stored = new StoredAccountCredential(session.Tokens.RefreshToken, session.Profile);
        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(stored);
        try
        {
            byte[] protectedBytes = ProtectedData.Protect(
                plaintext,
                Entropy,
                DataProtectionScope.CurrentUser);

            string temporaryPath = TokenPath + ".tmp";
            await File.WriteAllBytesAsync(temporaryPath, protectedBytes, cancellationToken);
            File.Move(temporaryPath, TokenPath, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public Task DeleteAsync()
    {
        try
        {
            File.Delete(TokenPath);
            File.Delete(TokenPath + ".tmp");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DebugConsole.Log("account", "The protected local account session could not be removed.");
        }

        return Task.CompletedTask;
    }
}
