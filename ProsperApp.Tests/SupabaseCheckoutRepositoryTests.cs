using System.Text.Json;
using ProsperApp.Features.Shared;
using ProsperApp.Features.StoreBootstrap;
using ProsperApp.Infrastructure.Caching;
using ProsperApp.Services;

namespace ProsperApp.Tests;

public class SupabaseCheckoutRepositoryTests
{
    [Fact]
    public async Task GetPaymentMethodsAsync_ReturnsSuccessfulEmptyListWhenBootstrapReturnsNoRows()
    {
        var rpcClient = new FakeSupabaseRpcClient();
        var bootstrapper = FakeStoreMasterBootstrapper.Success("{\"payment_methods\":[]}");
        var repository = CreateRepository(rpcClient, bootstrapper);

        var result = await repository.GetPaymentMethodsAsync(CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Value);
        Assert.Null(result.FailureKind);
        Assert.Null(result.ErrorMessage);
        Assert.Equal(1, bootstrapper.EnsureCallCount);
        Assert.Null(rpcClient.FunctionName);
    }

    [Fact]
    public async Task GetPaymentMethodsAsync_ReturnsUnavailableFailureWhenBootstrapFails()
    {
        var rpcClient = new FakeSupabaseRpcClient();
        var bootstrapper = FakeStoreMasterBootstrapper.Failure(
            ResultFailureKind.Unavailable,
            "HTTP 503 upstream unavailable");
        var repository = CreateRepository(rpcClient, bootstrapper);

        var result = await repository.GetPaymentMethodsAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ResultFailureKind.Unavailable, result.FailureKind);
        Assert.Contains("HTTP 503 upstream unavailable", result.ErrorMessage);
    }

    [Fact]
    public async Task GetPaymentMethodsAsync_ReturnsPermissionDeniedWhenBootstrapIsForbidden()
    {
        var rpcClient = new FakeSupabaseRpcClient();
        var bootstrapper = FakeStoreMasterBootstrapper.Failure(
            ResultFailureKind.PermissionDenied,
            "Edge Function経由のRPC実行権限がありません。prosper-rpcのキー設定を確認してください。");
        var repository = CreateRepository(rpcClient, bootstrapper);

        var result = await repository.GetPaymentMethodsAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ResultFailureKind.PermissionDenied, result.FailureKind);
        Assert.Equal(
            "Edge Function経由のRPC実行権限がありません。prosper-rpcのキー設定を確認してください。",
            result.ErrorMessage);
    }

    [Fact]
    public async Task GetPaymentMethodsAsync_ReturnsNotConfiguredWhenBootstrapIsNotConfigured()
    {
        var rpcClient = new FakeSupabaseRpcClient { HasAccess = false };
        var bootstrapper = FakeStoreMasterBootstrapper.Failure(
            ResultFailureKind.NotConfigured,
            "Supabase Edge Function設定が未設定です。");
        var repository = CreateRepository(rpcClient, bootstrapper);

        var result = await repository.GetPaymentMethodsAsync(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(ResultFailureKind.NotConfigured, result.FailureKind);
        Assert.Equal("Supabase Edge Function設定が未設定です。", result.ErrorMessage);
        Assert.Null(rpcClient.FunctionName);
        Assert.Equal(1, bootstrapper.EnsureCallCount);
    }

    private static SupabaseCheckoutRepository CreateRepository(
        FakeSupabaseRpcClient rpcClient,
        FakeStoreMasterBootstrapper bootstrapper) =>
        new(
            rpcClient,
            new FakeLocalSettingsProvider(42),
            new FakeApplicationCache(),
            bootstrapper);

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

    private sealed class FakeStoreMasterBootstrapper(Result<StoreBootstrapPayload> result)
        : IStoreMasterBootstrapper
    {
        public int EnsureCallCount { get; private set; }

        public static FakeStoreMasterBootstrapper Success(string json)
        {
            using var document = JsonDocument.Parse(json);
            return new FakeStoreMasterBootstrapper(Result<StoreBootstrapPayload>.Success(
                new StoreBootstrapPayload(42, document.RootElement.Clone(), false)));
        }

        public static FakeStoreMasterBootstrapper Failure(ResultFailureKind kind, string message) =>
            new(Result<StoreBootstrapPayload>.Failure(kind, message));

        public Task<Result<StoreBootstrapPayload>> GetStoreBootstrapAsync(CancellationToken ct) =>
            Task.FromResult(result);

        public Task<Result<StoreBootstrapPayload>> EnsureAsync(CancellationToken ct)
        {
            EnsureCallCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class FakeApplicationCache : IApplicationCache
    {
        public bool TryGetValue<T>(string key, out T? value)
        {
            value = default;
            return false;
        }

        public void Set<T>(string key, T value, TimeSpan? ttl, string category, string displayName)
        {
        }

        public void Remove(string key)
        {
        }

        public IReadOnlyList<ApplicationCacheStatus> GetStatuses() => [];

        public int ClearAll() => 0;
    }
}
