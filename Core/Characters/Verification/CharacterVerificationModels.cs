using System;
using System.Text.Json.Serialization;

namespace AION_2_Companion.Core.Characters.Verification;

public sealed class CharacterVerificationChallenge
{
    [JsonPropertyName("challenge_id")] public string ChallengeId { get; set; } = string.Empty;
    [JsonPropertyName("status")] public string Status { get; set; } = "pending";
    [JsonPropertyName("character")] public VerificationCharacter Character { get; set; } = new();
    [JsonPropertyName("instruction")] public VerificationInstruction Instruction { get; set; } = new();
    [JsonPropertyName("created_at")] public string? CreatedAt { get; set; }
    [JsonPropertyName("expires_at")] public string? ExpiresAt { get; set; }
    [JsonPropertyName("expires_in_seconds")] public int ExpiresInSeconds { get; set; }
    [JsonPropertyName("check_after_seconds")] public int CheckAfterSeconds { get; set; }
    [JsonPropertyName("check_interval_seconds")] public int CheckIntervalSeconds { get; set; } = 30;
    [JsonPropertyName("attempts")] public int Attempts { get; set; }
    [JsonPropertyName("verified")] public bool Verified { get; set; }
    [JsonPropertyName("verification_type")] public string VerificationType { get; set; } = string.Empty;
    [JsonPropertyName("official_ncsoft_account_verified")] public bool OfficialNcsoftAccountVerified { get; set; }

    [JsonIgnore] public CharacterIdentity Identity => new(Character.Region, Character.ServerId, Character.Name);
    [JsonIgnore] public bool IsActive => Status is "pending" or "checking";
}

public sealed class VerificationCharacter
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("region")] public string Region { get; set; } = "eu";
    [JsonPropertyName("server_id")] public ushort ServerId { get; set; }
}

public sealed class VerificationInstruction
{
    [JsonPropertyName("action")] public string Action { get; set; } = string.Empty;
    [JsonPropertyName("slot_key")] public string SlotKey { get; set; } = "unknown";
    [JsonPropertyName("slot_pos")] public int SlotPosition { get; set; }
}

public sealed class VerificationStartResponse
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("message_code")] public string MessageCode { get; set; } = string.Empty;
    [JsonPropertyName("challenge")] public CharacterVerificationChallenge Challenge { get; set; } = new();
}

public sealed class VerificationStatusResponse
{
    [JsonPropertyName("challenge")] public CharacterVerificationChallenge Challenge { get; set; } = new();
}

public sealed class VerificationCheckResponse
{
    [JsonPropertyName("ok")] public bool Ok { get; set; }
    [JsonPropertyName("verified")] public bool Verified { get; set; }
    [JsonPropertyName("result")] public string Result { get; set; } = string.Empty;
    [JsonPropertyName("challenge")] public CharacterVerificationChallenge Challenge { get; set; } = new();
}

public sealed class CharacterVerificationApiException : Exception
{
    public CharacterVerificationApiException(string message, string? code, int statusCode, TimeSpan? retryAfter = null)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
        RetryAfter = retryAfter;
    }

    public string? Code { get; }
    public int StatusCode { get; }
    public TimeSpan? RetryAfter { get; }
}

public sealed class VerificationSignInRequiredException : Exception
{
    public VerificationSignInRequiredException() : base("A Nexforge Account connection is required.") { }
}

public sealed record StoredVerificationState(
    string AccountId,
    CharacterVerificationChallenge Challenge,
    DateTimeOffset SavedAtUtc);
