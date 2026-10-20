using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Opds.Services;

/// <summary>
/// Thread-safe in-memory cache for validated basic authentication credentials.
/// </summary>
public class BasicAuthCache : IBasicAuthCache
{
    private const int DefaultMaxCacheEntries = 1000;
    private readonly int _maxCacheEntries;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="BasicAuthCache"/> class with default settings.
    /// </summary>
    public BasicAuthCache()
        : this(DefaultMaxCacheEntries)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BasicAuthCache"/> class with a custom maximum cache entry limit.
    /// </summary>
    /// <param name="maxCacheEntries">The maximum cache entries.</param>
    internal BasicAuthCache(int maxCacheEntries)
    {
        _maxCacheEntries = maxCacheEntries;
    }

    /// <inheritdoc />
    public async Task<Guid?> GetOrAuthenticateAsync(
        string username,
        string password,
        string remoteIp,
        Func<Task<Guid?>> authenticateFunc,
        Func<Guid, bool> validateUserFunc)
    {
        ArgumentNullException.ThrowIfNull(username);
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(remoteIp);
        ArgumentNullException.ThrowIfNull(authenticateFunc);
        ArgumentNullException.ThrowIfNull(validateUserFunc);

        var key = ComputeHash(username, password, remoteIp);
        var now = DateTime.UtcNow;

        if (_cache.TryGetValue(key, out var cached) && cached.ExpirationUtc > now)
        {
            if (validateUserFunc(cached.UserId))
            {
                var durationMinutes = GetCacheDurationMinutes();
                _cache[key] = new CacheEntry(cached.UserId, now.AddMinutes(durationMinutes));
                return cached.UserId;
            }

            _cache.TryRemove(key, out _);
        }

        var authenticatedUserId = await authenticateFunc().ConfigureAwait(false);
        if (authenticatedUserId.HasValue && authenticatedUserId.Value != Guid.Empty)
        {
            if (_cache.Count >= _maxCacheEntries)
            {
                PruneExpired(now);
                if (_cache.Count >= _maxCacheEntries)
                {
                    // Evict oldest 25% of entries by expiration time to prevent cache stampede
                    var evictCount = Math.Max(1, _maxCacheEntries / 4);
                    var toRemove = _cache
                        .OrderBy(p => p.Value.ExpirationUtc)
                        .Take(evictCount)
                        .Select(p => p.Key)
                        .ToArray();

                    foreach (var k in toRemove)
                    {
                        _cache.TryRemove(k, out _);
                    }
                }
            }

            var durationMinutes = GetCacheDurationMinutes();
            _cache[key] = new CacheEntry(authenticatedUserId.Value, now.AddMinutes(durationMinutes));
        }

        return authenticatedUserId;
    }

    /// <inheritdoc />
    public void Clear()
    {
        _cache.Clear();
    }

    private static string ComputeHash(string username, string password, string remoteIp)
    {
        var raw = username + "\0" + password + "\0" + remoteIp;
        var bytes = Encoding.UTF8.GetBytes(raw);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private static int GetCacheDurationMinutes()
    {
        var configured = OpdsPlugin.Instance?.Configuration.AuthCacheDurationMinutes ?? 10;
        return Math.Clamp(configured, 1, 60);
    }

    private void PruneExpired(DateTime now)
    {
        foreach (var pair in _cache)
        {
            if (pair.Value.ExpirationUtc <= now)
            {
                _cache.TryRemove(pair.Key, out _);
            }
        }
    }

    private sealed record CacheEntry(Guid UserId, DateTime ExpirationUtc);
}
