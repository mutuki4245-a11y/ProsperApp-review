using System.Text.Json;

namespace ProsperApp.Services;

public abstract class SupabaseRepositoryBase(
    ISupabaseRpcClient rpcClient,
    ILocalSettingsProvider? localSettingsProvider = null)
{
    protected ISupabaseRpcClient RpcClient { get; } = rpcClient;

    protected long CurrentStoreDepartmentId => localSettingsProvider?.GetCurrent().StoreDepartmentId ?? 0;

    protected bool HasRequiredSettings()
    {
        return RpcClient.HasReadAccess &&
               (localSettingsProvider is null || CurrentStoreDepartmentId > 0);
    }

    protected bool HasMutationSettings()
    {
        return RpcClient.HasSecretAccess &&
               (localSettingsProvider is null || CurrentStoreDepartmentId > 0);
    }

    protected async Task<IReadOnlyList<JsonElement>> PostRpcArrayAsync<TPayload>(
        string functionName,
        TPayload payload,
        CancellationToken ct)
    {
        if (!HasRequiredSettings())
        {
            return [];
        }

        var result = await RpcClient.PostArrayAsync(functionName, payload, requireSecretKey: false, ct);
        return result.Succeeded ? result.Rows : [];
    }

    protected static long? NormalizeId(long? id)
    {
        return id is > 0 ? id : null;
    }

    protected static string PermissionErrorMessage()
    {
        return "DBへの実行権限がありません。RPCのgrant設定を確認してください。";
    }
}
