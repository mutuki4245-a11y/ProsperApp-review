using System.Text.Json;
using ProsperApp.Features.Shared;

namespace ProsperApp.Infrastructure.Supabase;

public abstract class SupabaseRepositoryBase(
    ISupabaseRpcClient rpcClient,
    ILocalSettingsProvider? localSettingsProvider = null)
{
    protected ISupabaseRpcClient RpcClient { get; } = rpcClient;

    protected long CurrentStoreDepartmentId => localSettingsProvider?.GetCurrent().StoreDepartmentId ?? 0;

    protected bool HasRpcAccess()
    {
        return RpcClient.HasAccess &&
               (localSettingsProvider is null || CurrentStoreDepartmentId > 0);
    }

    protected async Task<Result<IReadOnlyList<JsonElement>>> PostRpcArrayResultAsync<TPayload>(
        string functionName,
        TPayload payload,
        CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return Result<IReadOnlyList<JsonElement>>.Failure(
                ResultFailureKind.NotConfigured,
                "Supabase Edge Function設定が未設定です。");
        }

        var result = await RpcClient.PostArrayAsync(functionName, payload, ct);
        return result.Succeeded
            ? Result<IReadOnlyList<JsonElement>>.Success(result.Rows)
            : RpcFailure<IReadOnlyList<JsonElement>>(
                result.ErrorMessage,
                "DBから情報を取得できませんでした。");
    }

    protected async Task<Result<string?>> PostRpcScalarResultAsync<TPayload>(
        string functionName,
        TPayload payload,
        CancellationToken ct)
    {
        if (!HasRpcAccess())
        {
            return Result<string?>.Failure(
                ResultFailureKind.NotConfigured,
                "Supabase Edge Function設定が未設定です。");
        }

        var result = await RpcClient.PostScalarAsync(functionName, payload, ct);
        return result.Succeeded
            ? Result<string?>.Success(result.Body)
            : RpcFailure<string?>(
                result.ErrorMessage,
                "DBから情報を取得できませんでした。");
    }

    protected static Result<T> RpcFailure<T>(string? rawError, string fallbackMessage)
    {
        var message = string.IsNullOrWhiteSpace(rawError) ? fallbackMessage : rawError;
        if (message.Contains("401", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("403", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("permission denied", StringComparison.OrdinalIgnoreCase))
        {
            return Result<T>.Failure(ResultFailureKind.PermissionDenied, PermissionErrorMessage());
        }

        return Result<T>.Failure(ResultFailureKind.Unavailable, message);
    }

    protected static long? NormalizeId(long? id)
    {
        return id is > 0 ? id : null;
    }

    protected static string? NormalizeCastDisplayNameList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var names = value
            .Split('、', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(StripDepartmentSuffixFromCastDisplayName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        return names.Length == 0 ? null : string.Join("、", names);
    }

    private static string StripDepartmentSuffixFromCastDisplayName(string value)
    {
        var separatorIndex = value.LastIndexOf('：');
        return separatorIndex > 0
            ? value[..separatorIndex].Trim()
            : value.Trim();
    }

    protected static string PermissionErrorMessage()
    {
        return "Edge Function経由のRPC実行権限がありません。prosper-rpcのキー設定を確認してください。";
    }
}
