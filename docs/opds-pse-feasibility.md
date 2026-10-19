# Feasibility Study: OPDS Page Streaming Extension (OPDS-PSE) Support

**Target:** [OPDS-PSE 1.2](https://github.com/anansi-project/opds-pse/blob/master/v1.2.md)
**Requested in:** [#29 Feature Request: OPDS-pse support](https://github.com/jellyfin/jellyfin-plugin-opds/issues/29) (open since 2022, `major-feature` label, 13 👍)
**Date:** 2026-10-19

---

## 1. Verdict

**Feasible with moderate effort.** The Jellyfin server already ships every low-level
capability PSE needs (archive access, PDF tooling, image encoding, and even book page
counting), so this feature is mostly *wiring* inside this plugin rather than new
infrastructure.

Recommended delivery in two phases:

| Phase | Scope | Estimated effort |
|---|---|---|
| **Phase 1** | PSE 1.2 feed links + streaming endpoint for comic archives (cbz/cbr/cb7/cbt, zip/rar/7z/tar), JPEG output, config toggle | ~1.5–2 dev days |
| **Phase 2** | `{maxWidth}` resizing, `pse:lastRead` / `pse:lastReadDate` progress attributes | +0.5–1 dev day |
| **Phase 3 (optional)** | PDF page streaming via server-provided PDFtoImage/PDFium | +1–2 dev days |
| **Not feasible** | EPUB / AZW / AZW3 / MOBI streaming (no layout engine, no meaningful page model) | — |

The feature is fully backward compatible: OPDS-PSE adds one optional acquisition link per
entry; clients that ignore it keep working exactly as today.

---

## 2. What PSE 1.2 requires

1. **Feed-level:** the Atom `<feed>` element must declare the namespace
   `http://vaemendis.net/opds-pse/ns`.
2. **Entry-level:** each streamable entry carries an extra acquisition link:

   ```xml
   <link rel="http://vaemendis.net/opds-pse/stream"
         type="image/jpeg"
         href="{baseUrl}/opds/stream/{bookId}?page={pageNumber}&amp;width={maxWidth}"
         pse:count="35"
         pse:lastRead="10"
         pse:lastReadDate="2010-01-10T10:01:11Z"/>
   ```

   - `rel` — fixed value `http://vaemendis.net/opds-pse/stream`.
   - `type` — **MUST** be `image/jpeg`, `image/gif` or `image/png`.
   - `pse:count` — **MUST** be the total page count.
   - `href` — **MUST** contain `{pageNumber}`; **MAY** contain `{maxWidth}`.
   - `pse:lastRead` (1-based) and `pse:lastReadDate` — optional.
3. **Endpoint semantics:** pages are numbered `0 … N-1`; the response is a single image.
4. Known server implementations (interoperability references): Komga, Kavita, Codex,
   LANraragi. Known client: Panels (iOS, 2.14.2+). All other OPDS clients simply ignore
   the link.

---

## 3. Current plugin architecture (relevant pieces)

| File | Role | Change needed |
|---|---|---|
| `Jellyfin.Plugin.Opds/OpdsApi.cs` | ASP.NET controller, `[Route("opds")]`, Basic-auth via `AuthorizeAsync()`, `BuildOutput()` serializes DTOs with `XmlHelper` | Add `GET opds/stream/{bookId}` endpoint |
| `Services/OpdsFeedProvider.cs` | Builds all feeds; `CreateEntry(Book, baseUrl)` is the single place where entry links are created | Add PSE link in `CreateEntry` |
| `Models/FeedDto.cs` | Root DTO, `[XmlAttribute("xmlns")] = Atom` | Add `xmlns:pse` declaration |
| `Models/LinkDto.cs` | `rel/href/type/title/length/mtime` XML attributes | Add `pse:count`, `pse:lastRead`, `pse:lastReadDate` |
| `Configuration/PluginConfiguration.cs` + `Configuration/Web/*` | Only `AllowAnonymousAccess` today | Add feature toggle(s) |

Notes:

- Serialization is `XmlSerializer` (namespace = Atom, per `XmlHelper.Create`). Namespaced
  attributes are supported; the serializer will emit a prefix (e.g. `ns1:count` or a
  forced `pse:`) — **clients resolve by namespace URI, not prefix**, but a golden-file
  serialization test is recommended to keep the output clean.
- The repo has **no test project**; a small one should be added for the DTO changes.
- `build.yaml` ships **only `Jellyfin.Plugin.Opds.dll`** (`artifacts:` list), and
  `targetAbi` is `12.0.0.0` (Jellyfin 10.12+). Any new dependency must resolve from the
  server, not be bundled.

---

## 4. Server capabilities already available (key findings)

Verified against `jellyfin/jellyfin` master (the target of `Jellyfin.Controller 12.*-*`):

1. **Archive access — SharpCompress 0.50.4** is already a server package and is already
   used to extract covers from `.cbz/.cbr/.cb7/.cbt`
   (`MediaBrowser.Providers/Books/ComicImageProvider.cs`). Read-only RAR/7z/zip/tar
   extraction is proven in-process.
2. **Page counting — already done by the server.** `ProbeProvider.FetchAsync(Book …)`
   (new in 10.12, `ICustomMetadataProvider<Book>`) sets
   `item.RunTimeTicks = pageCount * 10000` for cb7/cbr/cbt/cbz and PDF
   (`Conversion.GetPageCount`), and uses a percentage convention for EPUB.
   - Caveat: the archive branch counts *all* non-directory entries, so a
     `ComicInfo.xml` makes `RunTimeTicks` off by one. The plugin should therefore count
     **image entries only**, itself, so that `pse:count` always matches what the stream
     endpoint actually serves.
3. **PDF rendering — PDFtoImage 5.2.1 (MIT, native PDFium) + SkiaSharp 3.119.4** are
   already server dependencies, so PDF page rendering is *possible* from a plugin without
   bundling native libraries (PDFium is not thread-safe; PDFtoImage serializes access
   with internal locks).
4. **Image resizing/encoding — `IImageEncoder`
   (`MediaBrowser.Controller.Drawing.IImageEncoder`, DI singleton `SkiaEncoder`)** exposes
   `EncodeImage(inputPath, …, ImageProcessingOptions, ImageFormat.Jpg)` which honours
   `MaxWidth`/`MaxHeight` and `Quality`, and inputs jpeg/png/webp/gif/bmp.
   Caveat: it is **path-based** (extract page to a temp file, encode to another temp
   file), and with quality ≥ 90 and a "supported" input it may *passthrough* the original
   file — so non-JPEG sources must be forced through re-encode (e.g. quality 85).
5. **Reading progress — `IUserDataManager.GetUserData(user, item)` /
   `GetUserDataBatch(items, user)`** give `UserItemData.PlaybackPositionTicks` and
   `LastPlayedDate`. Per the 10.12 probe comments, book clients report *page × 10000*
   ticks, so `pse:lastRead = PlaybackPositionTicks / 10000` and
   `pse:lastReadDate = LastPlayedDate` are directly derivable (verify the 0/1-based
   convention against a real book client during implementation).
6. **Assembly loading — `PluginLoadContext`** resolves from the plugin folder and falls
   back to the default context (server's `.deps.json`/TPA) when a DLL is not shipped.
   Therefore referencing `SharpCompress` (and optionally `PDFtoImage`) *without* bundling
   them is safe and requires no change to `build.yaml artifacts`.

---

## 5. Proposed design (Phase 1 + 2)

### 5.1 Feed changes

- `FeedDto`: add an `xmlns:pse = http://vaemendis.net/opds-pse/ns` declaration
  (only feeds that contain PSE links strictly need it; adding it everywhere is harmless).
- `LinkDto`: add nullable `[XmlAttribute(Namespace = "http://vaemendis.net/opds-pse/ns")]`
  `count`, `lastRead`, `lastReadDate` members (`long?`/`DateTime?` with string proxy
  properties, mirroring the existing `length`/`mtime` pattern).
- `OpdsFeedProvider.CreateEntry`: append

  ```csharp
  entry.Links.Add(new LinkDto(
      "http://vaemendis.net/opds-pse/stream",
      $"{baseUrl}/opds/stream/{book.Id}?page={{pageNumber}}",
      "image/jpeg")
  { Count = pageService.GetPageCount(book) });
  ```

  only when the book has streamable pages (see format matrix). Omit `{maxWidth}` from
  `href` until resizing is implemented — the spec marks it `MAY`.

### 5.2 Page service (new `Services/PageStreamService` + interface)

- `GetPageCount(book)` / `GetPageEntries(book)` — single source of truth:
  - open with `ArchiveFactory.OpenAsyncArchive`,
  - filter to image extensions (`jpg/jpeg/png/gif/webp/bmp/tif/tiff/avif`),
  - **natural sort** of entry keys (`2.jpg` before `10.jpg`; most comics are zero-padded
    but not all — Komga/Kavita use natural ordering, and our `count` and page indices
    must agree),
  - skip directories and nested junk (`__MACOSX`, `ComicInfo.xml`).
- Caching: `ConcurrentDictionary` keyed by `(path, size, lastWriteTimeUtc)` with a cap,
  so feed rendering does not re-open archives on every request (zip central-directory
  scans are ~ms; RAR/7z header scans are the slow path but still far cheaper than
  decompression).
- `GetPage(book, index, maxWidth)` — extract entry #index (0-based), normalize to JPEG.

### 5.3 Endpoint (new in `OpdsApi`)

```
GET /opds/stream/{bookId}?page={pageNumber}&width={maxWidth}
```

- Reuse the existing `AuthorizeAsync()` pattern (401 + `WWW-Authenticate` on failure,
  `Guid.Empty` when anonymous access is allowed) — same as `Cover`/`Download`.
- Validate `0 <= page < count`, otherwise `404`.
- Pipeline: extract to temp file → `IImageEncoder.EncodeImage(..., ImageFormat.Jpg,
  quality 85)` (forced re-encode; `MaxWidth` set when `width` was supplied) → return
  `FileStreamResult` with `image/jpeg` → delete temp files.
- Deterministic `type="image/jpeg"` in the feed means *all* pages must be normalized to
  JPEG (this also solves mixed jpg/png/webp archives, where per-page formats vary).
- Phase 3 (PDF): same endpoint, branch on extension, render with
  `PDFtoImage.Conversion` — count is free (`RunTimeTicks / 10000`).

### 5.4 Configuration

- `EnablePageStreaming` (default **off**) — feature toggle for safe rollout.
- (Phase 2) optionally a server-side cap for `width` to bound bandwidth/CPU.

---

## 6. Format support matrix

| Format | `pse:count` | Page image extraction | Phase |
|---|---|---|---|
| `.cbz` / `.zip` | ✅ own image-only count (server's `RunTimeTicks` available as hint) | ✅ SharpCompress | 1 |
| `.cbr` / `.rar` | ✅ | ✅ SharpCompress (read-only RAR) | 1 |
| `.cb7` / `.7z` | ✅ | ✅ | 1 |
| `.cbt` / `.tar` | ✅ | ✅ | 1 |
| `.pdf` | ✅ (server already computes) | ✅ PDFtoImage/PDFium — native, serialized, heavier | 3 |
| `.epub` / `.azw(3)` / `.mobi` | ❌ only a percentage convention (1 tick) | ❌ would require a full layout/render engine | out of scope |

For non-streamable formats we simply omit the PSE link — perfectly valid PSE/OPDS.

---

## 7. Risks & mitigations

| # | Risk | Impact | Mitigation |
|---|---|---|---|
| 1 | Feed rendering latency: one archive listing per entry on large feeds | Slow feeds | Count cache keyed by path/size/mtime; feature toggle; consider lazy/on-demand count only if profiling demands |
| 2 | `XmlSerializer` namespace-prefix handling for `pse:*` attributes | Malformed/ugly XML, client parse errors | Golden-file serialization test; verify with `xmllint` and against Panels/Komga's feed parser; prefix name is cosmetic, URI is what matters |
| 3 | Page ordering (`10.jpg` vs `2.jpg`) and non-image entries | Wrong pages / wrong `count` | One shared ordered image-entry list used by both count and extraction; natural-sort comparer |
| 4 | CPU/bandwidth from per-request decode + JPEG re-encode | Server load under heavy use | Re-encode only when needed (width supplied or source not jpeg); optional in-memory LRU of hot pages later; JPEG quality ~85 |
| 5 | Temp-file churn for path-based `IImageEncoder` | I/O overhead, cleanup on failure | `try/finally`, unique names in `Path.GetTempPath()`, consider direct SkiaSharp stream API in phase 2 (server already ships SkiaSharp 3.119.4) |
| 6 | Dependency version drift vs. server (SharpCompress/PDFtoImage not bundled) | `FileNotFoundException` if server downgrades | Pin package versions to the server's `Directory.Packages.props` (0.50.4 / 5.2.1); `targetAbi 12.0.0.0` already bounds the supported server range |
| 7 | Exotic page formats (AVIF/TIFF/HEIC) not supported by `IImageEncoder` | Broken page for those comics | Serve raw bytes as best effort, or declare fallback `type` per entry at feed time based on archive inspection; document limitation |
| 8 | No per-item authorization on existing endpoints (`Cover`/`Download`) carries over to `Stream` | Any authenticated user can read any item's pages by id | Optional hardening: verify the user can see the item (query with `SetUser`) — worth doing for the new endpoint |
| 9 | PDF phase: PDFium not thread-safe + native dependency + memory spikes | Under-provisioned servers stall | Keep PDF in a separate phase, reuse server's PDFtoImage (internal global lock), cap concurrent renders |
| 10 | `pse:lastRead` base (0- vs 1-based) reporting from clients | Off-by-one reading resume | Spec says 1-based; verify against a real book client during phase 2; attribute is optional |

---

## 8. Testing / validation plan

1. **Unit tests (new test project):** DTO serialization golden files (feed with PSE link,
   correct namespace + attributes), natural-sort ordering, page-count filtering
   (archive with `ComicInfo.xml` + nested dirs), page index bounds.
2. **Manual interop:**
   - Feed lint: `xmllint --noout` + OPDS validators; confirm namespace resolves.
   - Client: **Panels (iOS)** is the reference PSE 1.2 client; also test a generic OPDS
     client (Panelfile/Komga-compatible tooling) to confirm non-PSE clients are unaffected.
   - Formats: padded/non-padded cbz, cbr (RAR5), cb7, cbt, a webp-based archive, PDF
     (phase 3), epub (must have *no* PSE link).
3. **Performance:** render "Recently Added" and a 1000-book letter feed cold vs. warm
   cache; stream pages while a library scan runs.
4. **Auth:** Basic-auth with/without `AllowAnonymousAccess`; expired password → 401 +
   `WWW-Authenticate`.

---

## 9. Decisions needed from maintainers

1. Ship Phase 1 only (archives), or attempt Phase 1+2 together?
2. Feature default: `EnablePageStreaming` off by default (recommended) or on?
3. Always transcode to JPEG, or serve original bytes when the source is already
   jpeg/png/gif (saves CPU, but `type` then varies per page within an archive)?
4. Is adding a test project acceptable as part of this work?
5. Should endpoint hardening (per-item visibility check) also be applied to the existing
   `Download`/`Cover` endpoints in the same PR, or separately?

---

## 10. Suggested work breakdown (Phase 1)

1. `Models/LinkDto.cs`, `Models/FeedDto.cs` — PSE attributes + namespace declaration.
2. New `Services/IPageStreamService` + `PageStreamService` (list/count/extract, natural
   sort, image filter, cache).
3. `OpdsApi` — `GetBookPage` endpoint (`stream/{bookId}`), auth, JPEG normalization.
4. `OpdsFeedProvider.CreateEntry` — emit PSE link when count > 0.
5. `PluginConfiguration` + `Configuration/Web/config.html|js` — toggle.
6. Test project + golden-file/ordering tests.
7. `build.yaml` changelog/version bump, README section documenting the endpoint.
