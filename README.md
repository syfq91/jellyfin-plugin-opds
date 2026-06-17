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

## Features

### OPDS Feed

Browse and download your book library from any OPDS-compatible reader (KOReader, Calibre, etc.).

- OPDS feed at `${baseUrl}/opds`
- Authenticated feed at `https://user:pass@${baseUrl}/opds`

### KoSync (KOReader Progress Sync)

Two-way reading progress synchronization for [KOReader](https://github.com/koreader/koreader) devices. No separate sync server needed.

1. Configure KOReader to use your Jellyfin server URL as the custom sync server
2. Login with your Jellyfin credentials
3. Browse books via OPDS, download, and read — progress syncs automatically across devices

## Build

### With Docker

```bash
# Build the plugin
docker build -t jellyfin-plugin-opds .

# Extract the DLL
docker create --name tmp jellyfin-plugin-opds
docker cp tmp:/plugin/Jellyfin.Plugin.Opds.dll .
docker rm tmp
```

### Without .NET SDK (Docker BuildKit)

```bash
docker build --output type=local,dest=./output .
```

The plugin DLL will be at `./output/plugin/Jellyfin.Plugin.Opds.dll`.

### With .NET SDK

```bash
dotnet publish -c Release
```

## Installation

1. Build or download `Jellyfin.Plugin.Opds.dll`
2. Copy it to your Jellyfin `plugins/` directory
3. Restart Jellyfin
4. Go to **Dashboard > Plugins > OPDS Feed** to configure

### Configuration

| Option | Default | Description |
|---|---|---|
| Allow Anonymous Access | Off | Allow unauthenticated OPDS access |
| Enable KoSync Server | On | Enable KOReader progress sync API |
| Enable User Registration | Off | Allow KOReader to create users (users must exist in Jellyfin) |

## License

This project is licensed under the [GPLv3 License](LICENSE).
