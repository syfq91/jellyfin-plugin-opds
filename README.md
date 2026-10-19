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

Comic book archives (`.cbz`, `.cbr`, `.cb7`, `.cbt`, `.zip`, `.rar`, `.7z`, `.tar`) can be read page
by page by [OPDS clients that support the page streaming extension](https://anansi-project.github.io/docs/opds-pse/intro)
(e.g. [Panels](https://panels.app/)), without downloading the whole file:

`GET /opds/stream/{bookId}?page={pageNumber}`

* `page` is the zero-based number of the requested page.
* `width` (optional) limits the maximum width of the returned JPEG image.

The feature is disabled by default and can be enabled with the
"Enable Page Streaming (OPDS-PSE)" option in the plugin configuration.
