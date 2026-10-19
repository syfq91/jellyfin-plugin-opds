# Feasibility Study: OPDS Progression 1.0 Support

**Target:** [OPDS Progression 1.0 (draft)](https://github.com/opds-community/drafts/blob/main/opds-progression-1.0.md)
**Date:** 2026-10-19

---

## 1. Verdict

**Feasible with low-to-moderate effort, with one structural caveat.** OPDS Progression is
a small JSON API (one GET, one PUT, one document shape) that maps almost one-to-one onto
Jellyfin's existing per-user item progress (`UserItemData`). No new server capabilities are
needed: the plugin can read and write progress through the same `IUserDataManager` the
server itself uses for playback reporting, and JSON support is built into the runtime.

The structural caveat: Jellyfin persists **no device identity** on user data (verified
against 10.12 master — `UserItemData` has no `DeviceId`), while the spec requires a
`device` object (id + name) in every progression document. A fully faithful round-trip of
device information therefore requires plugin-side storage (Phase 3); earlier phases
synthesize the device and lose the client-supplied name.

Recommended delivery:

| Phase | Scope | Estimated effort |
|---|---|---|
| **Phase 1** | Core sync: `GET`/`PUT /opds/progression/{bookId}`, feed discovery link, error surface (400/401/403/404/409), config toggle, synthesized device | ~1–1.5 dev days |
| **Phase 2** | Spec-compliance polish: OPDS Authentication Document on `401`, server-derived `references`, schema-validation tests | ~1 dev day |
| **Phase 3 (optional)** | Fidelity: plugin-side store for device id/name, `title` and `references` round-trip | ~1–1.5 dev days |

The feature is backward compatible: it adds one optional link per entry and one endpoint
pair; clients that ignore both are unaffected. Like the PSE work, it should ship
**disabled by default** — the spec is still a living draft.

---

## 2. What OPDS Progression 1.0 requires

1. **Discovery** — each publication entry links to a per-publication service:

   ```xml
   <link rel="http://opds-spec.org/progression"
         type="application/opds-progression+json"
         href="{baseUrl}/opds/progression/{bookId}"/>
   ```

   (In OPDS 2.0 the same link may carry an `authenticate` hint; the plugin emits OPDS 1.x
   Atom feeds, where nested link properties do not exist.)

2. **Progression Document** (JSON, served as `application/opds-progression+json`):

   | Key | Format | Required | Meaning |
   |---|---|---|---|
   | `title` | string | No | e.g. chapter title |
   | `modified` | ISO 8601 | **Yes** | timestamp of last known progression |
   | `device` | `{ id: URI, name: string }` | **Yes** | device that reported the progression |
   | `progression` | float 0–1 | **Yes** | total progression as a fraction |
   | `references` | array of URI references | No | e.g. `#page=6`, `chapter1.html#par26`, `#t=67` |

3. **Interactions:**

   | Verb | Behavior | Success |
   |---|---|---|
   | `GET` | fetch last-known progression | `200` with document **or empty body** (never communicated) |
   | `PUT` | client reports progression; server decides whether to update | `200`/`201` with resulting document |

   **Failure codes:** `400 progression-invalid-payload`, `403 progression-incorrect-user`,
   `403 progression-locked`, `409 progression-date` (a more recent point is already
   available). A `401` must carry an OPDS Authentication Document; any other error must
   carry an RFC 7807 Problem Details object (`type` + `title`).

4. **Ecosystem status (2026-10):** living draft at `drafts.opds.io`, authored by the anansi
   project (Komga) community. Komga is the reference server implementation. Thorium Reader
   has an open issue for the consumer side ([edrlab/thorium-reader#3930](https://github.com/edrlab/thorium-reader/issues/3930),
   GET first). Panels support is unconfirmed. Adoption is early but real and moving toward
   the plugin's audience (EPUB readers in particular).

---

## 3. The Jellyfin data mapping (the key insight)

Jellyfin already stores everything the *core* of the spec needs, per user and per item,
in `MediaBrowser.Controller.Entities.UserItemData`:

| Progression concept | Jellyfin field | Notes |
|---|---|---|
| `progression` (float 0–1) | `PlaybackPositionTicks / RunTimeTicks` | uniform formula, see below |
| `modified` | `LastPlayedDate` | already maintained on every playback save |
| “never communicated” (`200` empty) | `PlaybackPositionTicks == 0` | direct mapping |
| `device` | **nothing** | 10.12 `UserItemData` has no `DeviceId` — the spec's one real gap |
| `title` | nothing | optional field; books lack chapter tracking for CBZ |
| `references` | nothing | optional; can be derived server-side (Phase 2) |

**The uniform formula.** For CBZ/PDF books the server's `ProbeProvider` sets
`RunTimeTicks = pageCount * 10000` and clients report pages as `page * 10000`. For EPUB
the server fixes `RunTimeTicks = TimeSpan.TicksPerSecond` and clients store the percentage
as a proportion of that second. In both conventions:

```
progression = PlaybackPositionTicks / RunTimeTicks       (0–1)
ticks       = round(progression * RunTimeTicks)          (PUT)
```

This means **progression works for every book type the feeds expose — including EPUB,
which OPDS-PSE cannot stream.** The two features are complementary: PSE serves comic/PDF
pages; Progression synchronizes *reading position* for everything.

**Write-back is native.** The server's own playback-report endpoint
(`POST /UserItems/{itemId}/UserData` in `ItemsController`) performs exactly the write this
spec needs: resolve user → load item **scoped to the user** (`ILibraryManager.GetItemById<T>(id, user)`
exists, as does `BaseItem.IsVisibleStandalone(user)`) → `IUserDataManager.SaveUserData(...)`
(sync overload and `UpdateUserItemDataDto` overload both exist;
`UserDataSaveReason.PlaybackProgress` is the exact semantic match). Saving triggers the
server's `UserDataSaved` event → websocket `UserDataChanged` → **jellyfin-web and other
Jellyfin clients see externally reported reading progress live**, for free.

**Synergy with the shipped PSE work:** a progression `PUT` updates `PlaybackPositionTicks`,
which is precisely what `pse:lastRead` (Phase 2 of PSE) is derived from — the two protocols
stay consistent automatically because they share one source of truth.

---

## 4. Proposed design (Phase 1)

### 4.1 Endpoint (new in `OpdsApi`)

```
GET  /opds/progression/{bookId}   → 200 application/opds-progression+json
                                    200 empty body (no progression)
                                    401 (no/anonymous user)
                                    404 (unknown/invisible item)
PUT  /opds/progression/{bookId}   → 200/201 with resulting document
                                    400/401/403/404/409 with problem details
```

- Reuse the existing Basic-auth `AuthorizeAsync()` pattern; this endpoint **always
  requires a user** (progression without an account is meaningless) — anonymous requests
  get `401`.
- Item lookup with the user scoping overload so that items hidden from the user produce
  `403 progression-incorrect-user` (or `404`, per maintainer preference; the spec's table
  suggests 403).

### 4.2 Mapping service

A small `ProgressionService` (registered like `PageStreamService`) keeps controller thin:

- `GetProgression(Book, User)` → document or `null` (empty body). Reads
  `GetUserData(user, book)`; builds the document; derives `device` (Phase 1: synthesized —
  `id: "urn:uuid:{userId}"`, `name: "Jellyfin ({username})"`) and omits optional fields.
- `UpdateProgression(Book, User, incoming)` → validates, applies the 409 staleness rule,
  writes via `SaveUserData(..., PlaybackProgress)`, returns the resulting document.

**409 rule (recommended):** store the client's claimed `modified` into `LastPlayedDate`;
reject with `409 progression-date` when an incoming `modified` is *older* than the stored
one. This gives a coherent comparison base and matches the spec's example problem type.
*(Alternative — see open questions — is pure last-writer-wins with server timestamps and
no 409.)*

**Validation (400):** `progression` present and within `[0,1]`; `modified` parseable ISO
8601; `device.id`/`device.name` present when `device` is sent; `references` a bounded
array of strings (e.g. ≤ 64 entries, ≤ 4 KB each) — defensively capped even though Phase 1
discards them.

### 4.3 Discovery

`LinkDto` gains the progression rel/type (new `ProgressionConstants` next to
`PseConstants`); `OpdsFeedProvider.CreateEntry` emits the link for **every** book entry
(streamable or not) when the toggle is on.

### 4.4 JSON & errors

System.Text.Json (no new dependencies): camelCase is not even needed — the spec's key
names are already lower camel (`progression`, `modified`, `device`); `DateTime` values
serialize as ISO 8601 with the default options when Kind is Utc. Errors are a tiny
`{ type, title, status }` record served as `application/problem+json` with the spec's
registry URLs (`https://registry.opds.io/error#progression-*`).

### 4.5 Configuration

`EnableProgression` (default **off**) — independent of `EnablePageStreaming`, because
progression deliberately covers non-streamable formats (EPUB) and because the spec is a
draft.

---

## 5. Spec compliance gaps and how they are handled

| Gap | Spec expectation | Handling |
|---|---|---|
| No device persistence in Jellyfin | `device` required with id **and name** | Phase 1: synthesize (`urn:uuid:{userId}` / `Jellyfin ({username})`) — schema-valid, name fidelity lost; Phase 3: plugin-side store |
| 401 must contain an OPDS Authentication Document | body = auth document | Phase 1: keep bare `401` + `WWW-Authenticate` (as all current endpoints do); Phase 2: serve `application/opds-authentication+json` and return it on 401 |
| `references` round-trip | echo what clients send | Phase 1: ignored; Phase 2: *derive* server-side (`#page=N` for PDFs per RFC 8118, archive entry key for CBZ via the existing natural-sorted page list); Phase 3: exact echo |
| `403 progression-locked` | publication no longer writable | No Jellyfin concept; not emitted (spec defines the code, using it is optional) |
| `title` (chapter) | optional | Omitted; Jellyfin books have no chapter model for CBZ |

All optional fields degrade gracefully — the spec's own recommendation is to fall back to
`progression` when specifics are unavailable, which is exactly what the mapping provides.

---

## 6. Edge cases

| Case | Behavior |
|---|---|
| `RunTimeTicks` null/0 (item not yet probed) | GET: empty body; PUT: still stores the absolute position (harmless), document returned reflects best effort; self-heals once the library scan probes the book |
| `progression` exactly 0 or 1 | accepted; 1 maps to `RunTimeTicks` (a reader reporting “finished”) |
| Multiple devices, concurrent PUTs | last-writer-wins at the user-data layer; the 409 rule rejects strictly older claims |
| jellyfin-web updates progress between client PUTs | client's next stale PUT gets 409 → client re-`GET`s → exactly the spec's intended flow |
| Clock skew between client devices | a device with a slow clock may see spurious 409s; acceptable and arguably correct (the other device claimed a later point) |
| Non-book items | endpoint resolves only `Book` items → 404 otherwise (feeds contain books only) |
| Anonymous server access | GET/PUT always 401 — progression requires an account |

---

## 7. Risks & mitigations

| # | Risk | Impact | Mitigation |
|---|---|---|---|
| 1 | Draft churn: rel/type/schema may change before final | Wasted surface area | Constants isolated; feature toggle default-off; snapshot-based schema tests detect drift |
| 2 | Early client adoption (Thorium GET-side in progress; Panels unconfirmed) | Feature unused for a while | Ship behind toggle; PSE `lastRead` already covers the Panels-style resume case |
| 3 | Device fidelity loss (Phase 1/2) | Generic device names in client prompts | Documented; Phase 3 store closes the gap if users care |
| 4 | 409 semantics are underspecified (“server determines”) | Interop surprises | Documented decision (client-`modified` comparison); trivially switchable to last-writer-wins |
| 5 | Overwriting `LastPlayedDate` semantics (double duty: UI “last played” + progression `modified`) | Cosmetic UI differences | Client timestamps are reasonable values there; server-time variant available if maintainer objects |
| 6 | EPUB tick convention is a server implementation quirk | Formula breaks if Jellyfin changes convention | Formula lives in one tested method; unit tests pin both conventions |
| 7 | PUT is an unauthenticated-by-item write surface | Abuse potential | Same Basic auth as every endpoint; range/size validation; user-scoped item lookup; cheap single-row writes |
| 8 | 401 without auth document (Phase 1) | Strict readers may not prompt correctly | Phase 2 closes it; Thorium's planned GET flow uses already-authenticated catalog sessions |

---

## 8. Test strategy

- **Mapping unit tests:** ticks ↔ float both directions across conventions (CBZ page math,
  EPUB percentage math), boundaries (0, 1, `RunTimeTicks` null), 409 staleness rule,
  validation failures.
- **Endpoint tests:** controller invoked directly with mocked `IUserDataManager` /
  `ILibraryManager` (established pattern in this repo): GET populated/empty, PUT
  200/201/400/403/409, anonymous 401, invisible item 403, wrong media handling.
- **Serialization golden tests:** exact JSON shape (keys, ISO 8601 `Z` timestamps, float
  formatting like `0.4999`), media type headers, problem-details payloads.
- **Phase 2:** validate emitted documents against a snapshot of the draft's
  `progression.schema.json` (test-only JSON-schema package, e.g. JsonSchema.Net).

No native dependencies are involved — everything is managed code, unlike the PSE PDF
phase.

---

## 9. Suggested work breakdown (Phase 1)

1. `Models/ProgressionConstants.cs` — rel, media type, error registry URLs.
2. `Models/ProgressionDocument.cs` + `ProgressionDevice` + `ProgressionProblem` (System.Text.Json).
3. `Services/IProgressionService.cs` + `ProgressionService.cs` — mapping, validation,
   409 rule, `SaveUserData` write-back with `UserDataSaveReason.PlaybackProgress`.
4. `OpdsApi` — `GetProgression`/`UpdateProgression` actions with Basic auth and problem
   responses.
5. `OpdsFeedProvider.CreateEntry` — progression link emission.
6. `PluginConfiguration.EnableProgression` (+ `config.html`/`config.js`), DI registration.
7. Tests (mapping, endpoint, serialization), README section, `build.yaml` changelog.

---

## 10. Open questions for maintainers

1. **Strict 409 vs last-writer-wins?** (recommended: strict on client `modified`)
2. **Store client `modified` in `LastPlayedDate`, or server receipt time?**
   (recommended: client `modified`)
3. **`403` vs `404` for items invisible to the user?** (spec's failure table suggests 403)
4. **Separate `EnableProgression` toggle** (recommended) or tie to `EnablePageStreaming`?
5. **Is the Phase 2 auth-document work in scope**, or is bare `401 + WWW-Authenticate`
   acceptable for the plugin's Basic-auth model?
6. **Phase 3 device store** — worth the storage/cleanup complexity, or is the synthesized
   device good enough long-term?
