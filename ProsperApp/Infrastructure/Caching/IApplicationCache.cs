namespace ProsperApp.Infrastructure.Caching;

public interface IApplicationCache
{
    bool TryGetValue<T>(string key, out T? value);

    void Set<T>(
        string key,
        T value,
        TimeSpan? ttl,
        string category,
        string displayName);

    void Remove(string key);

    IReadOnlyList<ApplicationCacheStatus> GetStatuses();

    int ClearAll();
}

public sealed record ApplicationCacheStatus(
    string Key,
    string Category,
    string DisplayName,
    bool IsCached,
    DateTimeOffset LastFetchedAt,
    DateTimeOffset? ExpiresAt);
