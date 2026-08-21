using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

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
    public void ClearAll_RemovesTrackedEntriesAndTheirRegistrations()
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
        Assert.Empty(cache.GetStatuses());
    }

    [Fact]
    public void Remove_RemovesTrackedEntryAndItsRegistration()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var cache = new ApplicationMemoryCache(
            memoryCache,
            new FixedTimeProvider(DateTimeOffset.UtcNow));
        cache.Set("runtime:day", 1L, TimeSpan.FromSeconds(30), "実行時", "営業日");

        cache.Remove("runtime:day");

        Assert.False(cache.TryGetValue<long>("runtime:day", out _));
        Assert.Empty(cache.GetStatuses());
    }

    [Fact]
    public void Expiration_RemovesRegistration()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var cache = new ApplicationMemoryCache(
            memoryCache,
            new FixedTimeProvider(DateTimeOffset.UtcNow));
        cache.Set("runtime:day", 1L, TimeSpan.FromMilliseconds(1), "実行時", "営業日");

        Assert.True(SpinWait.SpinUntil(
            () => !cache.TryGetValue<long>("runtime:day", out _),
            TimeSpan.FromSeconds(5)));
        Assert.True(SpinWait.SpinUntil(
            () => cache.GetStatuses().Count == 0,
            TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void ReplacedEntryCallback_DoesNotRemoveNewRegistration()
    {
        using var memoryCache = new CallbackControlledMemoryCache();
        var cache = new ApplicationMemoryCache(
            memoryCache,
            new FixedTimeProvider(DateTimeOffset.UtcNow));
        cache.Set("master:items", new[] { 1 }, null, "マスタ", "旧商品");
        cache.Set("master:items", new[] { 2 }, null, "マスタ", "新商品");

        memoryCache.InvokePendingCallbacks();

        Assert.True(cache.TryGetValue<int[]>("master:items", out var value));
        Assert.Equal([2], value!);
        var status = Assert.Single(cache.GetStatuses());
        Assert.Equal("新商品", status.DisplayName);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            return utcNow;
        }
    }

    private sealed class CallbackControlledMemoryCache : IMemoryCache
    {
        private readonly Dictionary<object, TestCacheEntry> _entries = [];
        private readonly Queue<TestCacheEntry> _pendingCallbacks = [];

        public ICacheEntry CreateEntry(object key)
        {
            return new TestCacheEntry(key, Commit);
        }

        public bool TryGetValue(object key, out object? value)
        {
            if (_entries.TryGetValue(key, out var entry))
            {
                value = entry.Value;
                return true;
            }

            value = null;
            return false;
        }

        public void Remove(object key)
        {
            if (_entries.Remove(key, out var entry))
            {
                entry.Reason = EvictionReason.Removed;
                _pendingCallbacks.Enqueue(entry);
            }
        }

        public void InvokePendingCallbacks()
        {
            while (_pendingCallbacks.TryDequeue(out var entry))
            {
                foreach (var registration in entry.PostEvictionCallbacks)
                {
                    registration.EvictionCallback!(
                        entry.Key,
                        entry.Value,
                        entry.Reason,
                        registration.State);
                }
            }
        }

        public void Dispose()
        {
            _entries.Clear();
            _pendingCallbacks.Clear();
        }

        private void Commit(TestCacheEntry entry)
        {
            if (_entries.Remove(entry.Key, out var replaced))
            {
                replaced.Reason = EvictionReason.Replaced;
                _pendingCallbacks.Enqueue(replaced);
            }

            _entries[entry.Key] = entry;
        }

        private sealed class TestCacheEntry(
            object key,
            Action<TestCacheEntry> commit) : ICacheEntry
        {
            public object Key { get; } = key;
            public object? Value { get; set; }
            public DateTimeOffset? AbsoluteExpiration { get; set; }
            public TimeSpan? AbsoluteExpirationRelativeToNow { get; set; }
            public TimeSpan? SlidingExpiration { get; set; }
            public IList<IChangeToken> ExpirationTokens { get; } = [];
            public IList<PostEvictionCallbackRegistration> PostEvictionCallbacks { get; } = [];
            public CacheItemPriority Priority { get; set; }
            public long? Size { get; set; }
            public EvictionReason Reason { get; set; } = EvictionReason.None;

            public void Dispose()
            {
                commit(this);
            }
        }
    }
}
