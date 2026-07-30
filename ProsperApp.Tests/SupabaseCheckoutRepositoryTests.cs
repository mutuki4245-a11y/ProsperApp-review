using System.Text.Json;
using ProsperApp.Features.Shared;
using ProsperApp.Services;

namespace ProsperApp.Tests;

public class SupabaseCheckoutRepositoryTests
{
    [Fact]
    public async Task GetPaymentMethodsAsync_ReturnsSuccessfulEmptyListWhenRpcReturnsNoRows()
    {
        var rpcClient = new FakeSupabaseRpcClient
        {
            ArrayResult = SupabaseRpcResult.Success("[]") with { Rows = [] }
        };
        var repository = new SupabaseCheckoutRepository(
            rpcClient,
            new FakeLocalSettingsProvider(42));

        var result = await repository.GetPaymentMethodsAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Value);
        Assert.Null(result.FailureKind);
        Assert.Null(result.ErrorMessage);
        Assert.Equal("store.get_payment_methods", rpcClient.FunctionName);
        Assert.Equal(42, rpcClient.Payload.GetProperty("p_department_id").GetInt64());
    }

    [Fact]
    public async Task GetPaymentMethodsAsync_ReturnsUnavailableFailureWhenRpcFails()
    {
        var rpcClient = new FakeSupabaseRpcClient
        {
            ArrayResult = SupabaseRpcResult.Failed("HTTP 503 upstream unavailable")
        };
        var repository = new SupabaseCheckoutRepository(
            rpcClient,
            new FakeLocalSettingsProvider(42));

        var result = await repository.GetPaymentMethodsAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ResultFailureKind.Unavailable, result.FailureKind);
        Assert.Contains("HTTP 503 upstream unavailable", result.ErrorMessage);
    }

    [Fact]
    public async Task GetPaymentMethodsAsync_ReturnsPermissionDeniedForForbiddenRpc()
    {
        var rpcClient = new FakeSupabaseRpcClient
        {
            ArrayResult = SupabaseRpcResult.Failed("HTTP 403 forbidden")
        };
        var repository = new SupabaseCheckoutRepository(
            rpcClient,
            new FakeLocalSettingsProvider(42));

        var result = await repository.GetPaymentMethodsAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ResultFailureKind.PermissionDenied, result.FailureKind);
        Assert.Equal(
            "Edge Function経由のRPC実行権限がありません。prosper-rpcのキー設定を確認してください。",
            result.ErrorMessage);
    }

    [Fact]
    public async Task GetPaymentMethodsAsync_ReturnsNotConfiguredWithoutRpcAccess()
    {
        var rpcClient = new FakeSupabaseRpcClient { HasAccess = false };
        var repository = new SupabaseCheckoutRepository(
            rpcClient,
            new FakeLocalSettingsProvider(42));

        var result = await repository.GetPaymentMethodsAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ResultFailureKind.NotConfigured, result.FailureKind);
        Assert.Equal("Supabase Edge Function設定が未設定です。", result.ErrorMessage);
        Assert.Null(rpcClient.FunctionName);
    }

    private sealed class FakeSupabaseRpcClient : ISupabaseRpcClient
    {
        public bool HasAccess { get; init; } = true;

        public SupabaseRpcResult ArrayResult { get; init; } =
            SupabaseRpcResult.Success("[]") with { Rows = [] };

        public string? FunctionName { get; private set; }

        public JsonElement Payload { get; private set; }

        public Task<SupabaseRpcResult> PostArrayAsync<TPayload>(
            string functionName,
            TPayload payload,
            CancellationToken ct)
        {
            FunctionName = functionName;
            Payload = JsonSerializer.SerializeToElement(payload);
            return Task.FromResult(ArrayResult);
        }

        public Task<SupabaseRpcResult> PostScalarAsync<TPayload>(
            string functionName,
            TPayload payload,
            CancellationToken ct)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class FakeLocalSettingsProvider(long departmentId) : ILocalSettingsProvider
    {
        public LocalSettings GetCurrent()
        {
            return new LocalSettings { StoreDepartmentId = departmentId };
        }
    }
}
