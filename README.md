<h1 align="center">Jellyfin OPDS Plugin</h1>
<h3 align="center">Part of the <a href="https://jellyfin.org">Jellyfin Project</a></h3>

<p align="center">
<img alt="Plugin Banner" src="https://raw.githubusercontent.com/jellyfin/jellyfin-ux/master/plugins/SVG/jellyfin-plugin-opds.svg?sanitize=true"/>
<br/>
<br/>
<a href="https://github.com/jellyfin/jellyfin-plugin-opds/actions?query=workflow%3A%22Build+Plugin%22">
<img alt="GitHub Workflow Status" src="https://img.shields.io/github/workflow/status/jellyfin/jellyfin-plugin-opds/Test%20Build%20Plugin.svg">
</a>
<a href="https://github.com/jellyfin/jellyfin-plugin-opds">
<img alt="GPLv3 License" src="https://img.shields.io/github/license/jellyfin/jellyfin-plugin-opds.svg"/>
</a>
<a href="https://github.com/jellyfin/jellyfin-plugin-opds/releases">
<img alt="Current Release" src="https://img.shields.io/github/release/jellyfin/jellyfin-plugin-opds.svg"/>
</a>
</p>

Feed is located at `${baseUrl}/opds`

Authenticated feed is accessed as `https://user:pass@${baseUrl}/opds`

## Page streaming (OPDS-PSE)

Comic book archives (`.cbz`, `.cbr`, `.cb7`, `.cbt`, `.zip`, `.rar`, `.7z`, `.tar`) and PDF
documents (`.pdf`) can be read page by page by
[OPDS clients that support the page streaming extension](https://anansi-project.github.io/docs/opds-pse/intro)
(e.g. [Panels](https://panels.app/)), without downloading the whole file:

`GET /opds/stream/{bookId}?page={pageNumber}`

* `page` is the zero-based number of the requested page.
* `width` (optional) limits the maximum width of the returned JPEG image.

Catalog entries advertise the total page count (`pse:count`) and, when the user has started
reading, the last read page and its date (`pse:lastRead`, `pse:lastReadDate`) so that clients
can resume where the user left off. The requested width is capped at 4096 pixels.

PDF pages are rendered on demand (at 150 dpi, or scaled to the requested width), which is
more expensive than extracting comic archive images.

The feature is disabled by default and can be enabled with the
"Enable Page Streaming (OPDS-PSE)" option in the plugin configuration.

## Reading progression (OPDS Progression)

Reading positions can be synchronized with
[OPDS clients that support the draft progression specification](https://github.com/opds-community/drafts/blob/main/opds-progression-1.0.md):

`GET /opds/progression/{bookId}` returns the last known progression of the authenticated
user as an `application/opds-progression+json` document (or an empty payload when the book
has not been opened yet); `PUT` with such a document stores the reading position reported
by the client. Updates are written back to Jellyfin's own user data, so progress reported
by OPDS clients shows up in every Jellyfin client, and updates that are older than the
stored position are rejected with `409 Conflict`.

Unlike page streaming, progression covers every book in the feeds, including formats that
cannot be streamed page by page such as EPUB.

Server side, documents are derived from Jellyfin's stored reading position: the `references`
array carries the path of the current page inside the comic archive (or `#page={n}` for PDF
documents, per RFC 8118) whenever the book has a page model. Responses are marked
`Cache-Control: no-store`, since reading positions are personal and change over time.

Jellyfin's user data has no fields for the reporting device, the reported title or the exact
references a client sent, so the plugin keeps them in a small JSON store next to the plugin
configuration (`Jellyfin.Plugin.Opds.progression.json`, capped and rewritten atomically).
While the stored position still belongs to that report, documents answer with the device,
title and references the client actually sent; once another client moves the position the
store entry goes stale, and the synthesized device and the derived reference take over again.

When the feature is enabled, unauthorized requests to the progression endpoints receive an
[OPDS authentication document](https://github.com/opds-community/drafts/blob/main/authentication-for-opds-1.0.md)
as the body of the `401` response. The same document is served at
`GET /opds/authentication.json` (media type `application/opds-authentication+json`) and,
when anonymous access is disabled, linked from the root feed.

The feature is disabled by default and can be enabled with the "Enable Reading Progression
(OPDS Progression)" option in the plugin configuration.
