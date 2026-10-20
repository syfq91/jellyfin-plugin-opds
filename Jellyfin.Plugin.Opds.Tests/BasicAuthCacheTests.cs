using System;
using System.Threading.Tasks;
using Jellyfin.Plugin.Opds.Services;
using Xunit;

namespace Jellyfin.Plugin.Opds.Tests;

/// <summary>
/// Unit tests for <see cref="BasicAuthCache"/>.
/// </summary>
public class BasicAuthCacheTests
{
    [Fact]
    public async Task GetOrAuthenticateAsync_Caches_Successful_Authentication()
    {
        var cache = new BasicAuthCache();
        var expectedUserId = Guid.NewGuid();
        var callCount = 0;

        Task<Guid?> Authenticate()
        {
            callCount++;
            return Task.FromResult<Guid?>(expectedUserId);
        }

        // First call - should call authenticate
        var result1 = await cache.GetOrAuthenticateAsync("alice", "secret", "127.0.0.1", Authenticate, _ => true);
        Assert.Equal(expectedUserId, result1);
        Assert.Equal(1, callCount);

        // Second call with same credentials - should return cached userId without calling authenticate
        var result2 = await cache.GetOrAuthenticateAsync("alice", "secret", "127.0.0.1", Authenticate, _ => true);
        Assert.Equal(expectedUserId, result2);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task GetOrAuthenticateAsync_Does_Not_Cache_Failed_Authentication()
    {
        var cache = new BasicAuthCache();
        var callCount = 0;

        Task<Guid?> Authenticate()
        {
            callCount++;
            return Task.FromResult<Guid?>(null);
        }

        // First call - fails
        var result1 = await cache.GetOrAuthenticateAsync("alice", "wrong", "127.0.0.1", Authenticate, _ => true);
        Assert.Null(result1);
        Assert.Equal(1, callCount);

        // Second call - should re-attempt authenticate
        var result2 = await cache.GetOrAuthenticateAsync("alice", "wrong", "127.0.0.1", Authenticate, _ => true);
        Assert.Null(result2);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task GetOrAuthenticateAsync_Distinguishes_Different_Credentials()
    {
        var cache = new BasicAuthCache();
        var user1 = Guid.NewGuid();
        var user2 = Guid.NewGuid();

        var result1 = await cache.GetOrAuthenticateAsync("alice", "secret1", "127.0.0.1", () => Task.FromResult<Guid?>(user1), _ => true);
        var result2 = await cache.GetOrAuthenticateAsync("bob", "secret2", "127.0.0.1", () => Task.FromResult<Guid?>(user2), _ => true);

        Assert.Equal(user1, result1);
        Assert.Equal(user2, result2);
    }

    [Fact]
    public async Task GetOrAuthenticateAsync_Distinguishes_Different_RemoteIps()
    {
        var cache = new BasicAuthCache();
        var expectedUserId = Guid.NewGuid();
        var callCount = 0;

        Task<Guid?> Authenticate()
        {
            callCount++;
            return Task.FromResult<Guid?>(expectedUserId);
        }

        var result1 = await cache.GetOrAuthenticateAsync("alice", "secret", "192.168.1.100", Authenticate, _ => true);
        Assert.Equal(expectedUserId, result1);
        Assert.Equal(1, callCount);

        var result2 = await cache.GetOrAuthenticateAsync("alice", "secret", "203.0.113.50", Authenticate, _ => true);
        Assert.Equal(expectedUserId, result2);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task Clear_Clears_Cache()
    {
        var cache = new BasicAuthCache();
        var userId = Guid.NewGuid();
        var callCount = 0;

        Task<Guid?> Authenticate()
        {
            callCount++;
            return Task.FromResult<Guid?>(userId);
        }

        await cache.GetOrAuthenticateAsync("alice", "secret", "127.0.0.1", Authenticate, _ => true);
        Assert.Equal(1, callCount);

        cache.Clear();

        await cache.GetOrAuthenticateAsync("alice", "secret", "127.0.0.1", Authenticate, _ => true);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task GetOrAuthenticateAsync_Reauthenticates_When_User_No_Longer_Valid()
    {
        var cache = new BasicAuthCache();
        var userId = Guid.NewGuid();
        var callCount = 0;

        Task<Guid?> Authenticate()
        {
            callCount++;
            return Task.FromResult<Guid?>(userId);
        }

        await cache.GetOrAuthenticateAsync("alice", "secret", "127.0.0.1", Authenticate, _ => true);
        Assert.Equal(1, callCount);

        // Now user validity returns false (e.g. user was deleted from Jellyfin)
        var result = await cache.GetOrAuthenticateAsync("alice", "secret", "127.0.0.1", Authenticate, _ => false);
        Assert.Equal(userId, result);
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task GetOrAuthenticateAsync_Evicts_Partial_On_Capacity_Preventing_Stampede()
    {
        // Cache with max capacity 4
        var cache = new BasicAuthCache(4);
        var u1 = Guid.NewGuid();
        var u2 = Guid.NewGuid();
        var u3 = Guid.NewGuid();
        var u4 = Guid.NewGuid();
        var u5 = Guid.NewGuid();

        var calls = 0;
        Task<Guid?> Auth(Guid u)
        {
            calls++;
            return Task.FromResult<Guid?>(u);
        }

        // Fill cache with 4 entries
        await cache.GetOrAuthenticateAsync("user1", "p1", "127.0.0.1", () => Auth(u1), _ => true);
        await Task.Delay(10, TestContext.Current.CancellationToken);
        await cache.GetOrAuthenticateAsync("user2", "p2", "127.0.0.1", () => Auth(u2), _ => true);
        await Task.Delay(10, TestContext.Current.CancellationToken);
        await cache.GetOrAuthenticateAsync("user3", "p3", "127.0.0.1", () => Auth(u3), _ => true);
        await Task.Delay(10, TestContext.Current.CancellationToken);
        await cache.GetOrAuthenticateAsync("user4", "p4", "127.0.0.1", () => Auth(u4), _ => true);
        Assert.Equal(4, calls);

        // Add 5th entry to trigger eviction (evicts oldest 25% = 1 entry: user1)
        await cache.GetOrAuthenticateAsync("user5", "p5", "127.0.0.1", () => Auth(u5), _ => true);
        Assert.Equal(5, calls);

        // user2, user3, user4, user5 should still be in cache (no additional auth calls)
        await cache.GetOrAuthenticateAsync("user2", "p2", "127.0.0.1", () => Auth(u2), _ => true);
        await cache.GetOrAuthenticateAsync("user3", "p3", "127.0.0.1", () => Auth(u3), _ => true);
        await cache.GetOrAuthenticateAsync("user4", "p4", "127.0.0.1", () => Auth(u4), _ => true);
        await cache.GetOrAuthenticateAsync("user5", "p5", "127.0.0.1", () => Auth(u5), _ => true);
        Assert.Equal(5, calls); // No new calls: entries were retained!

        // user1 was evicted, so accessing user1 should trigger 1 new auth call
        await cache.GetOrAuthenticateAsync("user1", "p1", "127.0.0.1", () => Auth(u1), _ => true);
        Assert.Equal(6, calls);
    }
}
