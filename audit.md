# Code Audit — jellyfin-plugin-opds

**Audited on**: 2026-10-20  
**Auditor**: Automated deep review (every source file read line-by-line)  
**Build status**: ✅ 0 warnings, 0 errors (`dotnet build`)  
**Test status**: ✅ All tests passing (`dotnet test`)  
**Target framework**: net10.0  
**Lines of production code**: ~3,500 (41 `.cs` files excl. obj/bin)  
**Lines of test code**: ~2,300 (8 test files)

---

## Table of Contents

- [Executive Summary](#executive-summary)
- [Architecture Overview](#architecture-overview)
- [Findings](#findings)
  - [C-1: Auth cache key ignores remote IP — cache poisoning / auth bypass](#c-1-auth-cache-key-ignores-remote-ip--cache-poisoning--auth-bypass)
  - [H-1: Search self-link is not URL-encoded — broken URLs with special characters](#h-1-search-self-link-is-not-url-encoded--broken-urls-with-special-characters)
  - [H-2: Null-forgiving `OpdsPlugin.Instance!` dereference across the codebase](#h-2-null-forgiving-opdsplugininstance-dereference-across-the-codebase)
  - [H-3: Temp file leak in thumbnail generation](#h-3-temp-file-leak-in-thumbnail-generation)
  - [H-4: Thumbnail cache key collision and symlink attack surface](#h-4-thumbnail-cache-key-collision-and-symlink-attack-surface)
  - [H-5: O(n log n) eviction on every save in ProgressionStore](#h-5-on-log-n-eviction-on-every-save-in-progressionstore)
  - [M-1: Cache stampede on full eviction in PageStreamService and BasicAuthCache](#m-1-cache-stampede-on-full-eviction-in-pagestreamservice-and-basicauthcache)
  - [M-2: Archive opened twice per page-stream request](#m-2-archive-opened-twice-per-page-stream-request)
  - [M-3: Exceptions silently swallowed in GetRootFolders](#m-3-exceptions-silently-swallowed-in-getrootfolders)
  - [M-4: Exceptions silently swallowed in GetBookThumbnail](#m-4-exceptions-silently-swallowed-in-getbookthumbnail)
  - [M-5: No size limit on extracted archive pages — decompression bomb risk](#m-5-no-size-limit-on-extracted-archive-pages--decompression-bomb-risk)
  - [M-6: Folder browsing has no visibility check for anonymous users](#m-6-folder-browsing-has-no-visibility-check-for-anonymous-users)
  - [M-7: Download, cover, and thumbnail endpoints lack per-item authorization](#m-7-download-cover-and-thumbnail-endpoints-lack-per-item-authorization)
  - [L-1: Multi-character `filterStart` silently returns all books](#l-1-multi-character-filterstart-silently-returns-all-books)
  - [L-2: Typo in property name `UpdateTimeSTr`](#l-2-typo-in-property-name-updatetimestr)
  - [L-3: Feed IDs are random GUIDs — not stable across requests](#l-3-feed-ids-are-random-guids--not-stable-across-requests)
  - [L-4: `UserRootFolder` detection may not cover all Jellyfin topologies](#l-4-userrootfolder-detection-may-not-cover-all-jellyfin-topologies)
  - [L-5: Singleton vs scoped service lifetime mismatch risk](#l-5-singleton-vs-scoped-service-lifetime-mismatch-risk)
  - [I-1: Silent discard of unparseable `LengthStr` values](#i-1-silent-discard-of-unparseable-lengthstr-values)
  - [I-2: CA2227 / CA1002 pragma suppressions across DTO files](#i-2-ca2227--ca1002-pragma-suppressions-across-dto-files)
  - [I-3: Code duplication across feed methods](#i-3-code-duplication-across-feed-methods)
  - [I-4: Good practices observed](#i-4-good-practices-observed)
- [Priority Matrix](#priority-matrix)
- [Appendix: Files Reviewed](#appendix-files-reviewed)

---

## Executive Summary

The plugin is well-structured with clean separation between the API controller (`OpdsApi`), service layer (`OpdsFeedProvider`, `PageStreamService`, `ProgressionService`, `ProgressionStore`, `BasicAuthCache`), and model DTOs. The build is warning-free with `TreatWarningsAsErrors` enabled and static analysis configured via `AnalysisMode=AllEnabledByDefault`. Test coverage exists for the core services.

The audit identified **22 findings**:

| Severity | Count | Categories |
|----------|------:|------------|
| 🔴 Critical | 1 | Security |
| 🟠 High | 5 | Security (2), Bug (2), Performance (1) |
| 🟡 Medium | 7 | Security (3), Performance (2), Quality (2) |
| 🔵 Low | 5 | Bug (1), Quality (4) |
| ℹ️ Informational | 4 | Quality (4) |

The most urgent items are:
1. The **authentication cache key** does not include the remote IP address, enabling potential auth bypass of IP-scoped restrictions (C-1).
2. The **download, cover, and thumbnail endpoints** do not verify that the authenticated user has access to the requested item, allowing cross-library data access (M-7).
3. The **thumbnail cache** uses predictable filenames in a world-writable temp directory, creating a symlink attack surface (H-4).

---

## Architecture Overview

```
┌──────────────────────────────────────────────────────────────┐
│                        OpdsApi.cs                            │
│              (ASP.NET Core Controller, 887 lines)            │
│                                                              │
│   GET /opds                    → GetRootFeed()               │
│   GET /opds/Books              → GetAlphabeticalRootFeed()   │
│   GET /opds/Books/RecentlyAdded→ GetRecentlyAdded()          │
│   GET /opds/Books/Favorite     → GetFavoriteBooks()          │
│   GET /opds/Books/Letter/{x}   → GetAlphabeticalFeed()       │
│   GET /opds/Genres             → GetGenres()                 │
│   GET /opds/Genres/{id}        → GetBooksByGenre()           │
│   GET /opds/Folders            → GetFolders()                │
│   GET /opds/Folders/{id}       → GetFolder()                 │
│   GET /opds/Search/{terms}     → SearchBookFromRoute()       │
│   GET /opds/Search?q=          → SearchBookFromQuery()       │
│   GET /opds/osd                → GetSearchDescription()      │
│   GET /opds/authentication.json→ GetAuthenticationDocument() │
│   GET /opds/Cover/{id}         → GetBookImage()              │
│   GET /opds/Thumbnail/{id}     → GetBookThumbnail()          │
│   GET /opds/Download/{id}      → DownloadBook()              │
│   GET /opds/Stream/{id}        → GetBookPage()               │
│   GET /opds/Progression/{id}   → GetProgression()            │
│   PUT /opds/Progression/{id}   → UpdateProgression()         │
└──────────┬────────────┬──────────┬──────────┬────────────────┘
           │            │          │          │
     ┌─────▼────┐ ┌─────▼────┐ ┌──▼───┐ ┌───▼──────────┐
     │OpdsFeed  │ │PageStream│ │Progr.│ │BasicAuthCache│
     │Provider  │ │Service   │ │Svc   │ │              │
     │(1196 ln) │ │(482 ln)  │ │(278) │ │(96 ln)       │
     └──┬───────┘ └──┬───────┘ └──┬───┘ └──────────────┘
        │            │            │
        │       ┌────▼─────┐  ┌──▼──────────┐
        │       │PdfPage   │  │Progression  │
        │       │Source    │  │Store        │
        │       │(89 ln)   │  │(167 ln)     │
        │       └──────────┘  └─────────────┘
        │
  ┌─────▼──────────────────────────────────┐
  │         Jellyfin Server APIs           │
  │  ILibraryManager, ISearchManager,      │
  │  IUserManager, IUserDataManager,       │
  │  IImageEncoder                         │
  └────────────────────────────────────────┘
```

**DI Registration** (`PluginServiceRegistrator.cs`):
- `IOpdsFeedProvider` → `OpdsFeedProvider` (scoped)
- `IBasicAuthCache` → `BasicAuthCache` (singleton)
- `IPageStreamService` → `PageStreamService` (singleton)
- `IPdfPageSource` → `PdfPageSource` (singleton)
- `IProgressionStore` → `ProgressionStore` (singleton)
- `IProgressionService` → `ProgressionService` (singleton)

**Authentication model**: HTTP Basic Authentication with an in-memory SHA-256 credential cache. Anonymous access is optional and configurable. Progression endpoints always require authentication.

---

## Findings

---

### C-1: Auth cache key ignores remote IP — cache poisoning / auth bypass

| Field | Value |
|-------|-------|
| **Severity** | 🔴 Critical |
| **Category** | Security |
| **Status** | ✅ Resolved |
| **File** | `Jellyfin.Plugin.Opds/Services/BasicAuthCache.cs` |
| **Lines** | 70–76 |

#### Description

The `ComputeHash` method constructs the cache key from only the username and password:

```csharp
// BasicAuthCache.cs, lines 70-76
private static string ComputeHash(string username, string password)
{
    var raw = username + "\0" + password;
    var bytes = Encoding.UTF8.GetBytes(raw);
    var hash = SHA256.HashData(bytes);
    return Convert.ToHexString(hash);
}
```

The `remoteIp` parameter is accepted by `GetOrAuthenticateAsync` (line 21) and passed through from `OpdsApi.AuthorizeAsync` (line 746), but it is **never used** in the hash computation.

#### Impact

1. **IP-scoped policy bypass**: If Jellyfin applies different authentication behavior per IP (e.g., local network auto-approval, remote IP rate limiting, or brute-force lockouts), the cache silently bypasses these policies. An authentication that succeeded from IP `192.168.1.100` will be served from cache for a request from `203.0.113.50` without re-checking.

2. **Stale credential window**: When a user's password is changed, the cache continues to accept the old password from any IP for up to 60 minutes (the configured `AuthCacheDurationMinutes`). This is somewhat expected for any credential cache, but coupling it with the missing IP dimension amplifies the window.

3. **Brute-force amplification**: Jellyfin's `AuthenticateUser` method logs failed attempts and may trigger lockouts. By caching the failure (if the plugin were to cache failures — currently it doesn't, but the design invites it), an attacker could enumerate valid usernames without triggering the lockout on their own IP.

#### Reproduction

1. Configure Jellyfin with IP-based access restrictions.
2. Authenticate via OPDS from an allowed IP `A`.
3. Within the cache TTL, send the same credentials from a restricted IP `B`.
4. Observe that the request is served from cache without re-checking Jellyfin's IP restrictions.

#### Recommendation

Include the `remoteIp` in the hash to ensure the cache is scoped per IP:

```csharp
private static string ComputeHash(string username, string password, string remoteIp)
{
    var raw = username + "\0" + password + "\0" + remoteIp;
    var bytes = Encoding.UTF8.GetBytes(raw);
    var hash = SHA256.HashData(bytes);
    return Convert.ToHexString(hash);
}
```

Update the `GetOrAuthenticateAsync` call site to pass `remoteIp` through.

---

### H-1: Search self-link is not URL-encoded — broken URLs with special characters

| Field | Value |
|-------|-------|
| **Severity** | 🟠 High |
| **Category** | Bug |
| **File** | `Jellyfin.Plugin.Opds/Services/OpdsFeedProvider.cs` |
| **Lines** | 919, 930 |

#### Description

In `SearchBooks`, the self link embeds the raw search term directly:

```csharp
// OpdsFeedProvider.cs, line 919
new LinkDto("self", baseUrl + "/opds/search/" + searchTerm + "?", ...)
```

But the pagination call on line 930 correctly URL-encodes the search term:

```csharp
// OpdsFeedProvider.cs, line 930
ApplyPagination(feed, baseUrl + "/opds/search/" + Uri.EscapeDataString(searchTerm), ...)
```

#### Impact

When a user searches for a term containing special characters (spaces, `&`, `#`, `?`, unicode), the self-link in the Atom feed will contain an invalid URL. OPDS clients that follow the self-link will get a 404 or malformed request. The pagination links (next, previous, last) will work correctly because they are encoded.

**Example**: Searching for `"C# programming"` produces:
- Self link: `/opds/search/C# programming?` ← broken (fragment `# programming?` is discarded)
- Next link: `/opds/search/C%23%20programming?startIndex=50&limit=50` ← correct

#### Recommendation

```csharp
var encodedSearch = Uri.EscapeDataString(searchTerm);
// Use encodedSearch in both the self link and ApplyPagination
new LinkDto("self", baseUrl + "/opds/search/" + encodedSearch + "?", ...)
// ...
ApplyPagination(feed, baseUrl + "/opds/search/" + encodedSearch, ...)
```

---

### H-2: Null-forgiving `OpdsPlugin.Instance!` dereference across the codebase

| Field | Value |
|-------|-------|
| **Severity** | 🟠 High |
| **Category** | Bug |
| **File** | Multiple files |
| **Lines** | See list below |

#### Description

`OpdsPlugin.Instance` is a static nullable property:

```csharp
// OpdsPlugin.cs, line 32
public static OpdsPlugin? Instance { get; private set; }
```

It is assigned in the constructor (line 26) and is `null` before the plugin is instantiated. Throughout the codebase, it is accessed with the null-forgiving operator `!`:

| File | Line | Usage |
|------|------|-------|
| `OpdsApi.cs` | 566 | `OpdsPlugin.Instance!.Configuration.EnablePageStreaming` |
| `OpdsApi.cs` | 706 | `OpdsPlugin.Instance!.Configuration.AllowAnonymousAccess` |
| `OpdsApi.cs` | 872 | `OpdsPlugin.Instance?.Configuration.DefaultPageSize ?? 50` |
| `OpdsFeedProvider.cs` | 83 | `OpdsPlugin.Instance!.Configuration.AllowAnonymousAccess` |
| `OpdsFeedProvider.cs` | 502 | `OpdsPlugin.Instance?.Configuration.ThumbnailWidth ?? 240` |
| `OpdsFeedProvider.cs` | 1029 | `OpdsPlugin.Instance!.Configuration.EnablePageStreaming` |
| `OpdsFeedProvider.cs` | 1060 | `OpdsPlugin.Instance!.Configuration.EnableProgression` |
| `OpdsFeedProvider.cs` | 1083 | `OpdsPlugin.Instance!.Configuration.EnablePageStreaming` |
| `OpdsFeedProvider.cs` | 1099 | `OpdsPlugin.Instance?.Configuration.DefaultPageSize ?? 50` |
| `BasicAuthCache.cs` | 80 | `OpdsPlugin.Instance?.Configuration.AuthCacheDurationMinutes ?? 10` |

Note the inconsistency: some uses (`OpdsApi.cs:872`, `OpdsFeedProvider.cs:502`, `BasicAuthCache.cs:80`) correctly use `?.` with a fallback, while others use the `!` operator which throws `NullReferenceException` if `Instance` is null.

#### Impact

During plugin initialization, shutdown, or if the plugin fails to construct, any of the `!` access sites will throw an unhandled `NullReferenceException`, potentially crashing the Jellyfin server request pipeline.

#### Recommendation

**Option A** (preferred): Inject the configuration via DI instead of static access:
```csharp
public class OpdsApi : ControllerBase
{
    private readonly PluginConfiguration _config;

    public OpdsApi(..., OpdsPlugin plugin)
    {
        _config = plugin.Configuration;
    }
}
```

**Option B** (minimal change): Replace all `Instance!` with `Instance?.` and a safe fallback:
```csharp
var config = OpdsPlugin.Instance?.Configuration;
var allowAnonymous = config?.AllowAnonymousAccess ?? false;
```

---

### H-3: Temp file leak in thumbnail generation

| Field | Value |
|-------|-------|
| **Severity** | 🟠 High |
| **Category** | Bug / Resource Leak |
| **File** | `Jellyfin.Plugin.Opds/Services/OpdsFeedProvider.cs` |
| **Lines** | 842–883 |

#### Description

The `GetBookThumbnail` method creates a temp file for the image encoder output:

```csharp
// OpdsFeedProvider.cs, lines 857-866
var tempOutput = cachePath + ".tmp";
var encodedPath = _imageEncoder.EncodeImage(
    imagePath,
    fileInfo.LastWriteTimeUtc,
    tempOutput,
    true,
    null,
    80,
    options,
    ImageFormat.Jpg);
```

The `encodedPath` returned by `EncodeImage` may differ from `tempOutput` (the encoder may write to a different location). If `File.Move(encodedPath, cachePath, true)` on line 872 throws an exception, the temp file at `encodedPath` (or `tempOutput`) is never cleaned up.

Compare with the analogous code in `PageStreamService.ConvertToJpeg` (lines 428–464), which correctly uses a `try/finally` block with `DeleteTemporaryFile` calls:

```csharp
// PageStreamService.cs, lines 460-464 — correct pattern
finally
{
    DeleteTemporaryFile(inputPath);
    DeleteTemporaryFile(outputPath);
}
```

#### Impact

Over time, failed thumbnail generations will accumulate orphaned temp files in the system temp directory. In constrained environments (Docker containers, embedded systems), this can exhaust disk space.

#### Recommendation

Wrap the thumbnail encoding in try/finally:

```csharp
var tempOutput = cachePath + ".tmp";
try
{
    var encodedPath = _imageEncoder.EncodeImage(...);
    if (File.Exists(encodedPath))
    {
        if (encodedPath != cachePath)
        {
            File.Move(encodedPath, cachePath, true);
        }
        return File.ReadAllBytes(cachePath);
    }
    return null;
}
catch (Exception ex)
{
    _logger?.LogWarning(ex, "Failed to generate thumbnail for {BookId}", bookId);
    return null;
}
finally
{
    if (File.Exists(tempOutput)) File.Delete(tempOutput);
}
```

---

### H-4: Thumbnail cache key collision and symlink attack surface

| Field | Value |
|-------|-------|
| **Severity** | 🟠 High |
| **Category** | Security |
| **File** | `Jellyfin.Plugin.Opds/Services/OpdsFeedProvider.cs` |
| **Lines** | 842–843 |

#### Description

The thumbnail cache key is constructed as:

```csharp
// OpdsFeedProvider.cs, lines 842-843
var cacheKey = $"opds-thumb-{fileInfo.Length}-{fileInfo.LastWriteTimeUtc.Ticks}-{maxWidth}.jpg";
var cachePath = Path.Combine(Path.GetTempPath(), cacheKey);
```

Two problems:

**1. No book ID in the key.** Two different book covers with the same file size, last-write timestamp, and requested width will produce the same cache key. The first book's thumbnail will be served for the second book.

**2. Predictable path in a world-writable directory.** Since the cache key components (file size, timestamp, width) are guessable, an attacker with local filesystem access can pre-create a symlink at the predicted path:
```bash
ln -s /etc/jellyfin/secrets.json /tmp/opds-thumb-12345-638123456789012345-240.jpg
```
The subsequent `File.ReadAllBytes(cachePath)` will follow the symlink and read arbitrary files. The content is then served as a JPEG response, leaking the file contents.

#### Impact

- **Cache collision**: Wrong thumbnails served to users.
- **Symlink attack**: Local privilege escalation / sensitive file disclosure.

#### Recommendation

1. Include the book ID in the cache key:
   ```csharp
   var cacheKey = $"opds-thumb-{bookId:N}-{fileInfo.Length}-{fileInfo.LastWriteTimeUtc.Ticks}-{maxWidth}.jpg";
   ```

2. Use a plugin-specific cache directory instead of the system temp directory:
   ```csharp
   var cacheDir = Path.Combine(applicationPaths.CachePath, "opds-thumbnails");
   Directory.CreateDirectory(cacheDir);
   var cachePath = Path.Combine(cacheDir, cacheKey);
   ```

3. Optionally verify the cache file is a regular file (not a symlink) before reading:
   ```csharp
   var info = new FileInfo(cachePath);
   if (info.Exists && (info.Attributes & FileAttributes.ReparsePoint) == 0)
   {
       return File.ReadAllBytes(cachePath);
   }
   ```

---

### H-5: O(n log n) eviction on every save in ProgressionStore

| Field | Value |
|-------|-------|
| **Severity** | 🟠 High |
| **Category** | Performance |
| **File** | `Jellyfin.Plugin.Opds/Services/ProgressionStore.cs` |
| **Lines** | 95–102 |

#### Description

When the store is at capacity (4096 entries), every `Save` call triggers `EvictOldest`:

```csharp
// ProgressionStore.cs, lines 95-102
private static void EvictOldest(Dictionary<string, ProgressionStoreEntry> entries)
{
    var oldest = entries.OrderBy(entry => entry.Value.Modified).FirstOrDefault();
    if (oldest.Key is not null)
    {
        entries.Remove(oldest.Key);
    }
}
```

`OrderBy` allocates an intermediate sorted collection over all entries, which is O(n log n) in time and O(n) in memory. This happens while holding the `_syncRoot` lock (line 68), blocking all concurrent `Get` calls.

#### Impact

At capacity, every progression update takes O(n log n) = O(4096 × 12) ≈ 49,000 comparisons and allocates a sorted array of 4096 elements, all under a lock. For a multi-user server with active OPDS readers, this creates a latency spike on every update.

#### Recommendation

Use `MinBy` (available since .NET 6) for O(n) eviction:

```csharp
private static void EvictOldest(Dictionary<string, ProgressionStoreEntry> entries)
{
    var oldest = entries.MinBy(entry => entry.Value.Modified);
    if (oldest.Key is not null)
    {
        entries.Remove(oldest.Key);
    }
}
```

Or, for amortized O(1), track the oldest entry key as a field updated on every insert.

---

### M-1: Cache stampede on full eviction in PageStreamService and BasicAuthCache

| Field | Value |
|-------|-------|
| **Severity** | 🟡 Medium |
| **Category** | Concurrency / Performance |
| **Files** | `PageStreamService.cs` (lines 261–264, 316–319), `BasicAuthCache.cs` (lines 48–54) |

#### Description

All three caches use the same eviction strategy — clear everything when full:

```csharp
// PageStreamService.cs, lines 261-264
if (_pageCache.Count >= MaxCacheEntries)
{
    _pageCache.Clear();
}

// BasicAuthCache.cs, lines 51-54
if (_cache.Count >= MaxCacheEntries)
{
    _cache.Clear();
}
```

#### Impact

When any cache reaches its limit:
1. All entries are discarded atomically.
2. All concurrent requests simultaneously experience cache misses.
3. All requests re-perform the expensive operation (archive parsing, authentication).
4. This creates a "thundering herd" that can spike CPU and I/O.

For `BasicAuthCache`, every concurrent OPDS request during the stampede will call `AuthenticateUser`, which involves cryptographic password hashing — a deliberately slow operation.

#### Recommendation

Replace `Clear()` with partial eviction. Options:
- **LRU eviction**: Use `MemoryCache` with sliding expiration.
- **Percentage eviction**: Remove the oldest 25% of entries.
- **Probabilistic eviction**: Each insert has a small chance of evicting a random entry.

For `BasicAuthCache`, the existing `PruneExpired` method (lines 84–92) already removes expired entries. Call it unconditionally before checking count, and only clear as a last resort:

```csharp
PruneExpired(now);
if (_cache.Count >= MaxCacheEntries)
{
    // Remove oldest 25% by expiration time
    var toRemove = _cache.OrderBy(p => p.Value.ExpirationUtc).Take(MaxCacheEntries / 4);
    foreach (var item in toRemove) _cache.TryRemove(item.Key, out _);
}
```

---

### M-2: Archive opened twice per page-stream request

| Field | Value |
|-------|-------|
| **Severity** | 🟡 Medium |
| **Category** | Performance |
| **File** | `Jellyfin.Plugin.Opds/Services/PageStreamService.cs` |
| **Lines** | 105–143 |

#### Description

`GetPage()` calls `TryGetPageList()` (line 137) which opens the archive to enumerate page entries, then closes it. It then calls `ExtractPage()` (line 142) which opens the **same archive again** to extract the specific page.

```csharp
// Simplified flow:
public PageStreamResult? GetPage(Guid bookId, int pageNumber, int? maxWidth)
{
    // 1st archive open: TryGetPageList → ArchiveFactory.OpenArchive (line 251)
    if (!TryGetPageList(book, out var pages) || pageNumber >= pages.Count)
        return null;

    // 2nd archive open: ExtractPage → ArchiveFactory.OpenArchive (line 368)
    return ExtractPage(book, pages[pageNumber], maxWidth);
}
```

For a cached page list, the first open is skipped (cache hit at line 240), so in practice the double-open only occurs on the first request for each book. Subsequent requests only open the archive once (in `ExtractPage`).

#### Impact

The first page request for each book decompresses the archive twice. For large RAR/7z archives, this can add hundreds of milliseconds of latency.

#### Recommendation

Provide a combined method that opens the archive once, validates the page list, and extracts the page in a single pass. The cache can still be populated as a side effect.

---

### M-3: Exceptions silently swallowed in GetRootFolders

| Field | Value |
|-------|-------|
| **Severity** | 🟡 Medium |
| **Category** | Quality |
| **File** | `Jellyfin.Plugin.Opds/Services/OpdsFeedProvider.cs` |
| **Lines** | 300–316 |

#### Description

Two `catch (Exception)` blocks with empty bodies:

```csharp
// OpdsFeedProvider.cs, lines 300-306
try
{
    topLevelFolders = _libraryManager.GetUserRootFolder()?.GetChildren(user, true)?.OfType<Folder>();
}
catch (Exception)
{
    // ← completely empty
}
// ... lines 310-316 have the same pattern
```

#### Impact

If the library manager throws (e.g., database connection lost, concurrent modification), the error is silently discarded and the user sees an empty folder list with no diagnostic information. This makes production debugging extremely difficult.

#### Recommendation

At minimum, log a warning:

```csharp
catch (Exception ex)
{
    _logger.LogWarning(ex, "Unable to enumerate top-level folders");
}
```

This requires adding an `ILogger<OpdsFeedProvider>` dependency to the class (which it currently lacks).

---

### M-4: Exceptions silently swallowed in GetBookThumbnail

| Field | Value |
|-------|-------|
| **Severity** | 🟡 Medium |
| **Category** | Quality |
| **File** | `Jellyfin.Plugin.Opds/Services/OpdsFeedProvider.cs` |
| **Lines** | 880–882 |

#### Description

```csharp
// OpdsFeedProvider.cs, lines 880-882
catch (Exception)
{
    return null;
}
```

No logging whatsoever for thumbnail generation failures.

#### Impact

Same as M-3 — silent failures make debugging impossible. If the image encoder is misconfigured, crashes on certain image formats, or encounters filesystem errors, there is no diagnostic trail.

#### Recommendation

Add logging, consistent with the pattern used in `PageStreamService`:

```csharp
catch (Exception ex)
{
    _logger.LogWarning(ex, "Unable to generate thumbnail for book {BookId}", bookId);
    return null;
}
```

---

### M-5: No size limit on extracted archive pages — decompression bomb risk

| Field | Value |
|-------|-------|
| **Severity** | 🟡 Medium |
| **Category** | Security |
| **File** | `Jellyfin.Plugin.Opds/Services/PageStreamService.cs` |
| **Lines** | 376–379 |

#### Description

When extracting a page from a comic archive, the full entry is decompressed into memory without any size limit:

```csharp
// PageStreamService.cs, lines 376-379
using var entryStream = entry.OpenEntryStream();
using var buffer = new MemoryStream();
entryStream.CopyTo(buffer);
imageBytes = buffer.ToArray();
```

A crafted archive could contain an entry that is small when compressed but decompresses to gigabytes (a "zip bomb" / decompression bomb).

#### Impact

An attacker who can upload a book to the Jellyfin library (or who compromises an existing book file) could trigger an out-of-memory condition on the server by requesting a page stream of the malicious archive.

#### Recommendation

Add a size limit and a size-limited copy:

```csharp
private const long MaxPageSizeBytes = 100 * 1024 * 1024; // 100 MB

// ...
using var entryStream = entry.OpenEntryStream();
using var buffer = new MemoryStream();
var totalRead = 0L;
var readBuffer = new byte[81920];
int bytesRead;
while ((bytesRead = entryStream.Read(readBuffer, 0, readBuffer.Length)) > 0)
{
    totalRead += bytesRead;
    if (totalRead > MaxPageSizeBytes)
    {
        _logger.LogWarning("Page {EntryKey} in {Path} exceeds {Limit} bytes", entryKey, path, MaxPageSizeBytes);
        return null;
    }
    buffer.Write(readBuffer, 0, bytesRead);
}
imageBytes = buffer.ToArray();
```

---

### M-6: Folder browsing has no visibility check for anonymous users

| Field | Value |
|-------|-------|
| **Severity** | 🟡 Medium |
| **Category** | Security |
| **File** | `Jellyfin.Plugin.Opds/Services/OpdsFeedProvider.cs` |
| **Lines** | 397–409 |

#### Description

In `GetFolder`, the visibility check only applies when a user is present:

```csharp
// OpdsFeedProvider.cs, lines 405-409
var user = userId != Guid.Empty ? _userManager.GetUserById(userId) : null;
if (user is not null && !folder.IsVisibleStandalone(user))
{
    return null;
}
```

When anonymous access is enabled (`userId == Guid.Empty`), `user` is `null`, so the `IsVisibleStandalone` check is skipped entirely. An anonymous user can browse any folder in the library by guessing or enumerating folder GUIDs.

#### Impact

With anonymous access enabled, any folder in the Jellyfin server can be browsed via the OPDS API, including folders that would normally be restricted by Jellyfin's access controls (e.g., adult content, private libraries).

#### Recommendation

When anonymous access is enabled, either:
1. Restrict anonymous browsing to folders that are in a "books" collection type, or
2. Skip the check entirely (accept the risk and document it), or
3. Use a default "guest" user for anonymous access whose permissions can be configured.

---

### M-7: Download, cover, and thumbnail endpoints lack per-item authorization

| Field | Value |
|-------|-------|
| **Severity** | 🟡 Medium |
| **Category** | Security |
| **Status** | ✅ Resolved |
| **File** | `Jellyfin.Plugin.Opds/OpdsApi.cs` |
| **Lines** | 431–547 |

#### Description

The `GetBookImage`, `GetBookThumbnail`, and `DownloadBook` endpoints authenticate the user but never verify that the authenticated user has access to the requested item:

```csharp
// OpdsApi.cs, lines 528-547 (DownloadBook)
public async Task<ActionResult> DownloadBook(Guid bookId)
{
    try
    {
        await AuthorizeAsync().ConfigureAwait(false);  // ← authenticates user
        var bookPath = _opdsFeedProvider.GetBook(bookId);  // ← no visibility check
        if (string.IsNullOrEmpty(bookPath))
            return NotFound();
        return PhysicalFile(bookPath, MimeTypes.GetMimeType(bookPath), fileName);
    }
    // ...
}
```

Compare with the `GetProgression` endpoint (lines 610–616), which correctly checks visibility:

```csharp
// OpdsApi.cs, lines 616 — correct pattern
if (!book.IsVisibleStandalone(user))
{
    return ProgressionProblem(StatusCodes.Status403Forbidden, ...);
}
```

#### Impact

Any authenticated user can download any book, view any cover image, or generate any thumbnail, regardless of whether the book is in a library they have access to. This is an IDOR (Insecure Direct Object Reference) vulnerability.

#### Recommendation

Add visibility checks to all item-access endpoints:

```csharp
public async Task<ActionResult> DownloadBook(Guid bookId)
{
    try
    {
        var userId = await AuthorizeAsync().ConfigureAwait(false);
        var book = _libraryManager.GetItemById<Book>(bookId);
        if (book is null || string.IsNullOrEmpty(book.Path))
            return NotFound();

        if (userId != Guid.Empty)
        {
            var user = _userManager.GetUserById(userId);
            if (user is not null && !book.IsVisibleStandalone(user))
                return NotFound();
        }

        return PhysicalFile(book.Path, MimeTypes.GetMimeType(book.Path), Path.GetFileName(book.Path));
    }
    catch (AuthenticationException) { ... }
}
```

---

### L-1: Multi-character `filterStart` silently returns all books

| Field | Value |
|-------|-------|
| **Severity** | 🔵 Low |
| **Category** | Bug |
| **File** | `Jellyfin.Plugin.Opds/Services/OpdsFeedProvider.cs` |
| **Lines** | 665–668 |

#### Description

```csharp
if (filterStart.Length != 1)
{
    filterStart = string.Empty;
}
```

Any multi-character value (e.g., `"AB"`, `"test"`, URL-encoded characters) causes `filterStart` to be emptied, which sets `NameStartsWith = ""` on the query — effectively returning **all** books. The route `Books/Letter/{startFilter}` accepts arbitrary strings.

#### Recommendation

Validate more strictly:

```csharp
if (filterStart.Length != 1 && !string.Equals(filterStart, "00", StringComparison.Ordinal))
{
    return BadRequest();
}
```

Or accept multi-character prefixes intentionally and document it.

---

### L-2: Typo in property name `UpdateTimeSTr`

| Field | Value |
|-------|-------|
| **Severity** | 🔵 Low |
| **Category** | Quality |
| **File** | `Jellyfin.Plugin.Opds/Models/LinkDto.cs` |
| **Line** | 253 |

#### Description

```csharp
public string? UpdateTimeSTr  // ← capital S, lowercase r
```

All other serialization proxy properties use consistent casing: `LengthStr`, `PageCountStr`, `LastReadStr`, `LastReadDateStr`. This one has an inconsistent capital `S` before `Tr`.

#### Recommendation

Rename to `UpdateTimeStr`. Since the property is `[EditorBrowsable(EditorBrowsableState.Never)]`, external callers should not be affected, but the XML attribute name `mtime` is what matters for serialization and would not change.

---

### L-3: Feed IDs are random GUIDs — not stable across requests

| Field | Value |
|-------|-------|
| **Severity** | 🔵 Low |
| **Category** | Quality |
| **File** | `Jellyfin.Plugin.Opds/Services/OpdsFeedProvider.cs` |
| **Lines** | 93, 200, 220, 285, 434, 544, 640, 710, 773, 916 |

#### Description

Every feed method generates a fresh `Guid.NewGuid().ToString()` as the `<id>` element:

```csharp
Id = Guid.NewGuid().ToString(),
```

The Atom specification (RFC 4287 §4.2.6) says:
> "Its content MUST be an IRI that is permanently associated with the entry."

Some OPDS clients use the feed ID for caching, deduplication, or sync. A feed ID that changes on every request defeats these mechanisms.

#### Recommendation

Use a deterministic ID derived from the feed type and parameters:

```csharp
Id = "urn:jellyfin:opds:recentlyadded:" + startIndex + ":" + limit
```

---

### L-4: `UserRootFolder` detection may not cover all Jellyfin topologies

| Field | Value |
|-------|-------|
| **Severity** | 🔵 Low |
| **Category** | Quality |
| **File** | `Jellyfin.Plugin.Opds/Services/OpdsFeedProvider.cs` |
| **Line** | 418 |

#### Description

```csharp
if (parent is not null && parent is not UserRootFolder && parent.Id != _libraryManager.RootFolder?.Id)
```

The "up" navigation link logic assumes the parent hierarchy is: `RootFolder → UserRootFolder → Library → Subfolder → ...`. In some Jellyfin setups (especially with network shares or symlinks), the hierarchy may differ, causing the "up" link to point to an unexpected location.

#### Recommendation

Consider using a stack-based approach where the client tracks its navigation path, or always point "up" to the known folder listing endpoint when in doubt.

---

### L-5: Singleton vs scoped service lifetime mismatch risk

| Field | Value |
|-------|-------|
| **Severity** | 🔵 Low |
| **Category** | Quality |
| **File** | `Jellyfin.Plugin.Opds/PluginServiceRegistrator.cs` |
| **Lines** | 16–22 |

#### Description

```csharp
serviceCollection.AddScoped<IOpdsFeedProvider, OpdsFeedProvider>();   // scoped
serviceCollection.AddSingleton<IBasicAuthCache, BasicAuthCache>();    // singleton
serviceCollection.AddSingleton<IPageStreamService, PageStreamService>(); // singleton
serviceCollection.AddSingleton<IPdfPageSource, PdfPageSource>();      // singleton
serviceCollection.AddSingleton<IProgressionStore, ProgressionStore>(); // singleton
serviceCollection.AddSingleton<IProgressionService, ProgressionService>(); // singleton
```

`OpdsFeedProvider` is scoped (created per request) but depends on `IPageStreamService` (singleton). This is fine because a shorter-lived service can depend on a longer-lived one. However, `ProgressionService` is a singleton that depends on `IUserDataManager`, which in Jellyfin is also a singleton, so there is no captive dependency issue **currently**.

The risk is future: if Jellyfin ever changes `IUserDataManager` or `ILibraryManager` to scoped lifetime, the singletons will hold stale references.

#### Recommendation

Document this design decision. Consider making all services scoped for safety, or add a comment explaining why singletons are correct.

---

### I-1: Silent discard of unparseable `LengthStr` values

| Field | Value |
|-------|-------|
| **Severity** | ℹ️ Info |
| **Category** | Quality |
| **File** | `Jellyfin.Plugin.Opds/Models/LinkDto.cs` |
| **Lines** | 101–118 |

#### Description

When `LengthStr` is set to a non-null, non-numeric string (e.g., `"abc"`), the `long.TryParse` fails and the `Length` property retains its previous value. The same pattern appears in `PageCountStr`, `LastReadStr`, `LastReadDateStr`, and `UpdateTimeSTr`.

This is a defensible design for XML deserialization (graceful degradation), but could be surprising if a caller sets a value and reads back something different.

---

### I-2: CA2227 / CA1002 pragma suppressions across DTO files

| Field | Value |
|-------|-------|
| **Severity** | ℹ️ Info |
| **Category** | Quality |
| **Files** | `EntryDto.cs`, `FeedDto.cs`, `ProgressionDocument.cs`, `ProgressionStoreEntry.cs` |

#### Description

These files suppress CA2227 (collection properties should be read only) and CA1002 (do not expose generic lists). The suppressions are necessary because `XmlSerializer` and `System.Text.Json` require mutable collections with public setters for deserialization.

This is expected and correct. The suppressions are file-scoped, which is appropriate.

---

### I-3: Code duplication across feed methods

| Field | Value |
|-------|-------|
| **Severity** | ℹ️ Info |
| **Category** | Quality |
| **File** | `Jellyfin.Plugin.Opds/Services/OpdsFeedProvider.cs` |

#### Description

The following pattern is repeated in `GetRecentlyAdded`, `GetFavoriteBooks`, `GetAllBooks`, `GetBooksByGenre`, `GetRootFolders`, `GetFolder`, and `SearchBooks`:

```
1. Normalize pagination (NormalizeStartIndex, NormalizeLimit)
2. Build InternalItemsQuery with pagination and user
3. Execute query via _libraryManager.GetItemsResult
4. Cast results to Book, build entries via CreateEntry
5. Apply pagination links via ApplyPagination
6. Return FeedDto
```

Each method duplicates ~30-40 lines of this structure. The link arrays (self, start, up, search, search-template) are also duplicated with minor variations.

#### Recommendation

Extract a helper method:

```csharp
private FeedDto BuildBookFeed(
    string baseUrl, string selfPath, string upPath,
    Guid userId, InternalItemsQuery query,
    int startIndex, int limit)
{
    // ... shared logic
}
```

---

### I-4: Good practices observed

The codebase demonstrates several good practices worth acknowledging:

| Practice | Where |
|----------|-------|
| Atomic file writes (write `.tmp`, then `File.Move`) | `ProgressionStore.Persist()` |
| Consistent `ConfigureAwait(false)` | All async methods in `OpdsApi` |
| Input validation on untrusted data | `ProgressionService.IsValid()` — checks range, null, max references, max reference length |
| `XmlSerializer` caching | `XmlHelper.Create()` with `ConcurrentDictionary` |
| SHA-256 hashing of cached credentials | `BasicAuthCache.ComputeHash()` — passwords are never stored in plaintext in the cache |
| Interface-based design with DI | All services have interfaces; constructors accept interfaces |
| Comprehensive XML documentation | Every public type and member has `///` documentation |
| Thread-safe caches | `ConcurrentDictionary` used throughout |
| Conditional cache duration expiry | `BasicAuthCache` validates cached entries against configurable TTL |
| Test coverage | Unit tests exist for `BasicAuthCache`, `NaturalSortComparer`, `PageStreamService`, `ProgressionService`, `ProgressionStore`, `OpdsFeedProvider`, and `OpdsApi` progression endpoints |
| Clean build | `TreatWarningsAsErrors=true`, `AnalysisMode=AllEnabledByDefault`, 0 warnings |
| Null safety | `<Nullable>enable</Nullable>` with nullable reference types throughout |

---

## Priority Matrix

| ID | Severity | Category | Effort | Priority | Status |
|----|----------|----------|--------|----------|--------|
| C-1 | 🔴 Critical | Security | Low (1-line change) | **P0** | ✅ Resolved |
| M-7 | 🟡 Medium | Security | Medium (3 endpoints) | **P0** | ✅ Resolved |
| H-1 | 🟠 High | Bug | Low (2-line change) | **P1** | Open |
| H-4 | 🟠 High | Security | Low (add bookId to key) | **P1** | Open |
| H-3 | 🟠 High | Bug | Low (add try/finally) | **P1** | Open |
| M-5 | 🟡 Medium | Security | Low (add size check) | **P1** | Open |
| H-2 | 🟠 High | Bug | Medium (12+ call sites) | **P2** | Open |
| H-5 | 🟠 High | Performance | Low (use MinBy) | **P2** | Open |
| M-3 | 🟡 Medium | Quality | Low (add logging) | **P2** | Open |
| M-4 | 🟡 Medium | Quality | Low (add logging) | **P2** | Open |
| M-6 | 🟡 Medium | Security | Medium (design decision) | **P2** | Open |
| M-1 | 🟡 Medium | Performance | Medium (eviction redesign) | **P3** | Open |
| M-2 | 🟡 Medium | Performance | Medium (refactor) | **P3** | Open |
| L-1 | 🔵 Low | Bug | Low | **P3** | Open |
| L-2 | 🔵 Low | Quality | Trivial | **P3** | Open |
| L-3 | 🔵 Low | Quality | Low | **P3** | Open |
| L-4 | 🔵 Low | Quality | Low | **P3** | Open |
| L-5 | 🔵 Low | Quality | Trivial (add comment) | **P3** | Open |
| I-1 | ℹ️ Info | Quality | — | — | Open |
| I-2 | ℹ️ Info | Quality | — | — | Open |
| I-3 | ℹ️ Info | Quality | Medium | **P4** | Open |
| I-4 | ℹ️ Info | Quality | — | — | N/A |

---

## Appendix: Files Reviewed

### Production Code (41 files)

| File | Lines | Purpose |
|------|------:|---------|
| `OpdsApi.cs` | 887 | ASP.NET Core controller — all OPDS endpoints |
| `OpdsPlugin.cs` | 59 | Plugin entry point and singleton instance |
| `PluginServiceRegistrator.cs` | 23 | DI service registration |
| `Configuration/PluginConfiguration.cs` | 54 | Plugin settings model |
| `Configuration/Web/config.html` | 71 | Plugin settings UI (HTML) |
| `Configuration/Web/config.js` | 43 | Plugin settings UI (JavaScript) |
| `Services/OpdsFeedProvider.cs` | 1196 | Feed generation for all OPDS catalogs |
| `Services/IOpdsFeedProvider.cs` | 143 | Interface for OpdsFeedProvider |
| `Services/PageStreamService.cs` | 482 | Comic page streaming (archives + PDF) |
| `Services/IPageStreamService.cs` | — | Interface for PageStreamService |
| `Services/PdfPageSource.cs` | 89 | PDF page rendering via PDFtoImage + SkiaSharp |
| `Services/IPdfPageSource.cs` | — | Interface for PdfPageSource |
| `Services/ProgressionService.cs` | 278 | Reading progression sync |
| `Services/IProgressionService.cs` | — | Interface for ProgressionService |
| `Services/ProgressionStore.cs` | 167 | JSON file-backed progression fidelity store |
| `Services/IProgressionStore.cs` | 27 | Interface for ProgressionStore |
| `Services/BasicAuthCache.cs` | 96 | In-memory SHA-256 credential cache |
| `Services/IBasicAuthCache.cs` | 32 | Interface for BasicAuthCache |
| `Services/NaturalSortComparer.cs` | 111 | Natural sort for archive page ordering |
| `Services/XmlHelper.cs` | 30 | Cached XmlSerializer factory |
| `Models/FeedDto.cs` | 93 | Atom feed DTO |
| `Models/EntryDto.cs` | 111 | Atom entry DTO |
| `Models/LinkDto.cs` | 275 | Atom link DTO with PSE extensions |
| `Models/AuthorDto.cs` | — | Author DTO |
| `Models/ContentDto.cs` | — | Content DTO |
| `Models/PublisherDto.cs` | — | Publisher DTO |
| `Models/OpenSearchDescriptionDto.cs` | 83 | OpenSearch description DTO |
| `Models/OpenSearchUrlDto.cs` | — | OpenSearch URL DTO |
| `Models/PageStreamResult.cs` | — | Page stream response DTO |
| `Models/PseConstants.cs` | 46 | OPDS-PSE constants |
| `Models/ProgressionDocument.cs` | 45 | OPDS progression document DTO |
| `Models/ProgressionDevice.cs` | 21 | Progression device DTO |
| `Models/ProgressionStoreEntry.cs` | 46 | Progression store entry DTO |
| `Models/ProgressionUpdateResult.cs` | 8 | Update result record |
| `Models/ProgressionUpdateStatus.cs` | 22 | Update status enum |
| `Models/ProgressionProblem.cs` | 27 | RFC 7807 problem details DTO |
| `Models/ProgressionSerializer.cs` | 42 | JSON serializer for progression types |
| `Models/ProgressionConstants.cs` | 37 | OPDS progression constants |
| `Models/AuthenticationDocument.cs` | — | Auth for OPDS document DTO |
| `Models/AuthenticationDocumentBuilder.cs` | 36 | Auth document factory |
| `Models/AuthenticationObject.cs` | — | Auth method DTO |
| `Models/AuthenticationLabels.cs` | — | Auth label DTO |
| `Models/AuthenticationConstants.cs` | 27 | Auth for OPDS constants |

### Test Code (8 files)

| File | Lines | Covers |
|------|------:|--------|
| `BasicAuthCacheTests.cs` | 117 | BasicAuthCache |
| `NaturalSortComparerTests.cs` | — | NaturalSortComparer |
| `OpdsFeedProviderTests.cs` | 386 | OpdsFeedProvider |
| `OpdsApiProgressionTests.cs` | 337 | OpdsApi progression endpoints |
| `PageStreamServiceTests.cs` | 299 | PageStreamService |
| `ProgressionServiceTests.cs` | 492 | ProgressionService |
| `ProgressionStoreTests.cs` | 154 | ProgressionStore |
| `ProgressionSchemaTests.cs` | 91 | Progression JSON schema validation |
| `PseSerializationTests.cs` | 133 | PSE XML serialization |
| `PseProgressTests.cs` | — | PSE progress calculations |
