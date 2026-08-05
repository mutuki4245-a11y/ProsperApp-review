using System.Text.Json;
using ProsperApp.Features.Shared;
using ProsperApp.Features.Synchronization;
using static ProsperApp.Infrastructure.Supabase.SupabaseJson;

namespace ProsperApp.Infrastructure.Supabase;

public sealed class SupabaseManagementMasterSynchronization(
    ISupabaseRpcClient rpcClient,
    ILocalSettingsProvider localSettingsProvider)
    : SupabaseRepositoryBase(rpcClient, localSettingsProvider), IManagementMasterSynchronization
{
    public async Task<Result<ManagementMasterSnapshot>> GetSnapshotAsync(
        string? knownRevision,
        CancellationToken cancellationToken)
    {
        if (!HasRpcAccess())
        {
            return Result<ManagementMasterSnapshot>.Failure(
                ResultFailureKind.NotConfigured,
                "店舗設定またはSupabase Edge Function設定が未設定です。");
        }

        var result = await RpcClient.PostArrayAsync(
            "store.get_management_master_snapshot",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_known_revision = string.IsNullOrWhiteSpace(knownRevision) ? null : knownRevision.Trim()
            },
            cancellationToken);
        if (!result.Succeeded || result.Rows.Count == 0)
        {
            var failure = RpcFailure<ManagementMasterSnapshot>(
                result.ErrorMessage,
                "管理マスタを取得できませんでした。");
            return Result<ManagementMasterSnapshot>.Failure(
                failure.FailureKind ?? ResultFailureKind.Unavailable,
                failure.ErrorMessage ?? "管理マスタを取得できませんでした。");
        }

        var row = result.Rows[0];
        var revision = ReadString(row, "master_revision");
        if (string.IsNullOrWhiteSpace(revision))
        {
            return Result<ManagementMasterSnapshot>.Failure(
                ResultFailureKind.InvalidResponse,
                "管理マスタのrevisionを取得できませんでした。");
        }

        var unchanged = ReadBool(row, "unchanged") ?? false;
        JsonElement? payload = null;
        if (!unchanged && row.TryGetProperty("snapshot", out var snapshot) && snapshot.ValueKind == JsonValueKind.Object)
        {
            payload = snapshot.Clone();
        }

        if (!unchanged && payload is null)
        {
            return Result<ManagementMasterSnapshot>.Failure(
                ResultFailureKind.InvalidResponse,
                "管理マスタの内容を取得できませんでした。");
        }

        return Result<ManagementMasterSnapshot>.Success(new ManagementMasterSnapshot(
            CurrentStoreDepartmentId,
            revision,
            unchanged,
            payload));
    }
}
