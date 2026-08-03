using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace ProsperApp.Infrastructure.Caching;

public sealed class ApplicationMemoryCache(
    IMemoryCache memoryCache,
    TimeProvider timeProvider) : IApplicationCache
{
    private readonly IMemoryCache _memoryCache = memoryCache;
    private readonly TimeProvider _timeProvider = timeProvider;
    private readonly ConcurrentDictionary<string, CacheRegistration> _registrations =
        new(StringComparer.Ordinal);

    public bool TryGetValue<T>(string key, out T? value)
    {
        if (_memoryCache.TryGetValue(key, out T? cached))
        {
            value = cached;
            return true;
        }

        value = default;
        return false;
    }

    public void Set<T>(
        string key,
        T value,
        TimeSpan? ttl,
        string category,
        string displayName)
    {
        var fetchedAt = _timeProvider.GetUtcNow();
        var options = new MemoryCacheEntryOptions
        {
            Priority = CacheItemPriority.Normal
        };
        if (ttl is { } duration)
        {
            options.AbsoluteExpirationRelativeToNow = duration;
        }

        _memoryCache.Set(key, value, options);
        _registrations[key] = new CacheRegistration(
            category,
            displayName,
            fetchedAt,
            ttl is { } expiration ? fetchedAt.Add(expiration) : null);
    }

    public void Remove(string key)
    {
        _memoryCache.Remove(key);
    }

    public IReadOnlyList<ApplicationCacheStatus> GetStatuses()
    {
        return _registrations
            .Select(entry => new ApplicationCacheStatus(
                entry.Key,
                entry.Value.Category,
                entry.Value.DisplayName,
                _memoryCache.TryGetValue(entry.Key, out _),
                entry.Value.LastFetchedAt,
                entry.Value.ExpiresAt))
            .OrderBy(status => status.Category)
            .ThenBy(status => status.DisplayName)
            .ThenBy(status => status.Key, StringComparer.Ordinal)
            .ToList();
    }

    public int ClearAll()
    {
        var count = 0;
        foreach (var key in _registrations.Keys)
        {
            if (_memoryCache.TryGetValue(key, out _))
            {
                count++;
            }

            _memoryCache.Remove(key);
        }

        return count;
    }

    private sealed record CacheRegistration(
        string Category,
        string DisplayName,
        DateTimeOffset LastFetchedAt,
        DateTimeOffset? ExpiresAt);
}
