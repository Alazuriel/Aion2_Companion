using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AION_2_Companion.Core.Leaderboards;

public sealed record LeaderboardBoss(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("submissions")] int Submissions,
    [property: JsonPropertyName("contributors")] int Contributors,
    [property: JsonPropertyName("best_dps")] double BestDps)
{
    public override string ToString() => Name;
}

public sealed class LeaderboardEntry
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
    [JsonPropertyName("username")] public string Username { get; init; } = string.Empty;
    [JsonPropertyName("character_name")] public string CharacterName { get; init; } = string.Empty;
    [JsonPropertyName("class_key")] public string ClassKey { get; init; } = string.Empty;
    [JsonPropertyName("server_name")] public string ServerName { get; init; } = string.Empty;
    [JsonPropertyName("boss_name")] public string BossName { get; init; } = string.Empty;
    [JsonPropertyName("boss_key")] public string BossKey { get; init; } = string.Empty;
    [JsonPropertyName("damage_total")] public long DamageTotal { get; init; }
    [JsonPropertyName("duration_ms")] public long DurationMs { get; init; }
    [JsonPropertyName("dps")] public double Dps { get; init; }
    [JsonPropertyName("fight_started_at")] public DateTimeOffset FightStartedAt { get; init; }
    [JsonPropertyName("submitted_at")] public DateTimeOffset SubmittedAt { get; init; }
    [JsonPropertyName("rank")] public int Rank { get; init; }
    [JsonPropertyName("status")] public string? Status { get; init; }
    [JsonPropertyName("client_submission_id")] public string? ClientSubmissionId { get; init; }
    [JsonPropertyName("reviewed_at")] public DateTimeOffset? ReviewedAt { get; init; }
    [JsonPropertyName("client_version")] public string? ClientVersion { get; init; }
    [JsonPropertyName("review_note")] public string? ReviewNote { get; init; }
}

public sealed record LeaderboardMeta(
    [property: JsonPropertyName("classes")] IReadOnlyList<string> Classes,
    [property: JsonPropertyName("metrics")] IReadOnlyList<string> Metrics,
    [property: JsonPropertyName("trust_note")] string TrustNote);

public sealed record LeaderboardPage(
    IReadOnlyList<LeaderboardEntry> Entries,
    int Total,
    int Page,
    int Pages,
    int Limit,
    string Metric,
    string Boss,
    string TrustNote);

public sealed record LeaderboardSubmitRequest(
    [property: JsonPropertyName("client_submission_id")] string ClientSubmissionId,
    [property: JsonPropertyName("boss_name")] string BossName,
    [property: JsonPropertyName("character_name")] string CharacterName,
    [property: JsonPropertyName("class_key")] string ClassKey,
    [property: JsonPropertyName("server_name")] string ServerName,
    [property: JsonPropertyName("damage_total")] long DamageTotal,
    [property: JsonPropertyName("duration_ms")] long DurationMs,
    [property: JsonPropertyName("client_version")] string ClientVersion,
    [property: JsonPropertyName("fight_started_at")] string FightStartedAt,
    [property: JsonPropertyName("public_consent")] bool PublicConsent,
    [property: JsonPropertyName("region")] string? Region = null,
    [property: JsonPropertyName("server_id")] int? ServerId = null,
    [property: JsonPropertyName("combat_evidence")] LeaderboardCombatEvidence? CombatEvidence = null);

/// <summary>Real, unsynthesized damage events from the saved encounter: [offset_ms, damage].</summary>
public sealed record LeaderboardCombatEvidence(
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("hits")] IReadOnlyList<long[]> Hits);

public sealed record LeaderboardSubmitResult(LeaderboardEntry Entry, bool Duplicate, string? Message);

public sealed class LeaderboardApiException : Exception
{
    public LeaderboardApiException(string code, string message, int statusCode)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }

    public string Code { get; }
    public int StatusCode { get; }
}
