using System;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.Opds.Services;

/// <summary>
/// Provides caching for HTTP Basic Authentication credentials to avoid repetitive
/// cryptographic password hashing on concurrent requests.
/// </summary>
public interface IBasicAuthCache
{
    /// <summary>
    /// Gets or creates the authenticated user ID for the given credentials.
    /// </summary>
    /// <param name="username">The username.</param>
    /// <param name="password">The password.</param>
    /// <param name="remoteIp">The remote IP address.</param>
    /// <param name="authenticateFunc">The asynchronous authentication delegate to call on cache miss.</param>
    /// <param name="validateUserFunc">The synchronous user validation delegate to verify cached user is still active.</param>
    /// <returns>The authenticated user ID, or null if authentication fails.</returns>
    Task<Guid?> GetOrAuthenticateAsync(
        string username,
        string password,
        string remoteIp,
        Func<Task<Guid?>> authenticateFunc,
        Func<Guid, bool> validateUserFunc);

    /// <summary>
    /// Clears all cached credentials.
    /// </summary>
    void Clear();
}
