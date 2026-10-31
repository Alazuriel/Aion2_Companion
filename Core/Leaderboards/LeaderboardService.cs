using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AION_2_Companion.Core.Accounts;

namespace AION_2_Companion.Core.Leaderboards;

public sealed class LeaderboardService : IDisposable
{
    private readonly LeaderboardApiClient _api = new();

    public static LeaderboardService Shared { get; } = new();

    private LeaderboardService()
    {
    }

    public Task<LeaderboardMeta> GetMetaAsync(CancellationToken cancellationToken = default) =>
        _api.GetMetaAsync(cancellationToken);

    public Task<IReadOnlyList<LeaderboardBoss>> GetBossesAsync(
        CancellationToken cancellationToken = default) =>
        _api.GetBossesAsync(cancellationToken);

    public Task<LeaderboardPage> GetLeaderboardAsync(
        string boss,
        string metric,
        string? classKey,
        string? search,
        int page,
        int limit,
        CancellationToken cancellationToken = default) =>
        _api.GetLeaderboardAsync(boss, metric, classKey, search, page, limit, cancellationToken);

    public Task<IReadOnlyList<LeaderboardEntry>> GetMineAsync(
        CancellationToken cancellationToken = default) =>
        WithAccountAsync((token, ct) => _api.GetMineAsync(token, ct), cancellationToken);

    public Task<LeaderboardSubmitResult> SubmitAsync(
        LeaderboardSubmitRequest request,
        CancellationToken cancellationToken = default) =>
        WithAccountAsync((token, ct) => _api.SubmitAsync(token, request, ct), cancellationToken);

    public Task WithdrawAsync(string entryId, CancellationToken cancellationToken = default) =>
        WithAccountAsync(async (token, ct) =>
        {
            await _api.WithdrawAsync(token, entryId, ct);
            return true;
        }, cancellationToken);

    private static async Task<T> WithAccountAsync<T>(
        Func<string, CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        string? token = await AccountService.Shared.GetAccessTokenAsync(false, cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
            throw new LeaderboardApiException("sign_in_required", "Connect your Nexforge Account to continue.", 401);

        try
        {
            return await action(token, cancellationToken);
        }
        catch (LeaderboardApiException ex) when (ex.StatusCode == 401)
        {
            token = await AccountService.Shared.GetAccessTokenAsync(true, cancellationToken);
            if (string.IsNullOrWhiteSpace(token))
                throw new LeaderboardApiException("sign_in_required", "Connect your Nexforge Account again.", 401);
            return await action(token, cancellationToken);
        }
    }

    public void Dispose() => _api.Dispose();
}
