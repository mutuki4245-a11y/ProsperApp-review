using Microsoft.Extensions.Caching.Memory;

namespace ProsperApp.Tests;

public class ApplicationMemoryCacheTests
{
    [Fact]
    public void Set_TracksCacheStatusAndExpiration()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var now = new DateTimeOffset(2026, 7, 30, 12, 0, 0, TimeSpan.Zero);
        var cache = new ApplicationMemoryCache(memoryCache, new FixedTimeProvider(now));

        cache.Set("master:items", new[] { 1, 2 }, TimeSpan.FromMinutes(10), "マスタ", "商品");

        Assert.True(cache.TryGetValue<int[]>("master:items", out var value));
        Assert.Equal([1, 2], value!);

        var status = Assert.Single(cache.GetStatuses());
        Assert.Equal("master:items", status.Key);
        Assert.Equal("マスタ", status.Category);
        Assert.Equal("商品", status.DisplayName);
        Assert.True(status.IsCached);
        Assert.Equal(now, status.LastFetchedAt);
        Assert.Equal(now.AddMinutes(10), status.ExpiresAt);
    }

    [Fact]
    public void ClearAll_RemovesTrackedEntriesAndKeepsTheirStatusVisible()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var cache = new ApplicationMemoryCache(
            memoryCache,
            new FixedTimeProvider(DateTimeOffset.UtcNow));
        cache.Set("runtime:day", 1L, TimeSpan.FromSeconds(30), "実行時", "営業日");
        cache.Set("master:tables", new[] { "A1" }, TimeSpan.FromMinutes(10), "マスタ", "卓番");

        var cleared = cache.ClearAll();

        Assert.Equal(2, cleared);
        Assert.False(cache.TryGetValue<long>("runtime:day", out _));
        Assert.False(cache.TryGetValue<string[]>("master:tables", out _));
        Assert.All(cache.GetStatuses(), status => Assert.False(status.IsCached));
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }
}
