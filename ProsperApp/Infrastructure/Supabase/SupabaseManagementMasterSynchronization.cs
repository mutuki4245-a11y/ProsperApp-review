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

    public async Task<Result<ManagementMasterMutationResult>> MutateAsync(
        ManagementMasterMutation input,
        bool allowAdminActions,
        CancellationToken cancellationToken)
    {
        if (!HasRpcAccess())
        {
            return Result<ManagementMasterMutationResult>.Failure(
                ResultFailureKind.NotConfigured,
                "店舗設定またはSupabase Edge Function設定が未設定です。");
        }

        if (!Guid.TryParse(input.OperationId, out var operationId) ||
            string.IsNullOrWhiteSpace(input.Area) ||
            string.IsNullOrWhiteSpace(input.Action) ||
            string.IsNullOrWhiteSpace(input.ExpectedAreaRevision) ||
            input.Payload.ValueKind != JsonValueKind.Object)
        {
            return Result<ManagementMasterMutationResult>.Failure(
                ResultFailureKind.InvalidInput,
                "管理マスタの保存内容を確認してください。");
        }

        var result = await RpcClient.PostArrayAsync(
            "store.save_management_master_v2",
            new
            {
                p_department_id = CurrentStoreDepartmentId,
                p_operation_id = operationId.ToString("D"),
                p_area = input.Area.Trim(),
                p_action = input.Action.Trim(),
                p_expected_area_revision = input.ExpectedAreaRevision.Trim(),
                p_payload = input.Payload,
                p_allow_admin_actions = allowAdminActions
            },
            cancellationToken);
        if (!result.Succeeded || result.Rows.Count == 0)
        {
            var failure = RpcFailure<ManagementMasterMutationResult>(
                result.ErrorMessage,
                "管理マスタを保存できませんでした。");
            return Result<ManagementMasterMutationResult>.Failure(
                failure.FailureKind ?? ResultFailureKind.Unavailable,
                failure.ErrorMessage ?? "管理マスタを保存できませんでした。");
        }

        var row = result.Rows[0];
        if (!row.TryGetProperty("master_delta", out var delta) ||
            delta.ValueKind is not (JsonValueKind.Array or JsonValueKind.Object))
        {
            return Result<ManagementMasterMutationResult>.Failure(
                ResultFailureKind.InvalidResponse,
                "保存後の管理マスタ応答が正しくありません。");
        }

        return Result<ManagementMasterMutationResult>.Success(new ManagementMasterMutationResult(
            operationId.ToString("D"),
            ReadString(row, "status") ?? "unavailable",
            ReadString(row, "master_revision") ?? string.Empty,
            ReadString(row, "area_revision") ?? string.Empty,
            ReadString(row, "area") ?? input.Area,
            delta.Clone(),
            ReadString(row, "error_code"),
            ReadString(row, "error_message")));
    }
}
