<p align="center">
  <img src="docs/images/logo.png" alt="Taskitify" width="140" />
</p>

<h1 align="center">Taskitify</h1>

<p align="center"><b>English</b> · <a href="README.pt-BR.md">Português</a></p>

<p align="center">
  <b>Spotify lyrics on the Windows 11 taskbar, right where the weather widget sits.</b><br />
  Synced line by line, colored by the album art, with a mini player one click away.
</p>

<p align="center">
  <a href="https://github.com/CarlosVinniFernandes/Taskitify/releases/latest"><b>⬇ Download</b></a> ·
  <a href="#how-to-use">How to use</a> ·
  <a href="#customization">Customization</a> ·
  <a href="docs/COMO-FUNCIONA.md">How it works</a>
</p>

<p align="center">
  <img src="docs/images/bar.png" alt="Synced lyrics on the taskbar" width="640" />
</p>

---

## What it is

Taskitify shows the current line of the song playing on Spotify **right on the taskbar**, in the space used by the Windows 11 weather widget.

- **Playing:** the lyric replaces the weather and follows the song line by line.
- **Paused, stopped or Spotify closed:** the weather comes back instantly. If you prefer, Taskitify can shrink to a minimized player instead of disappearing.
- **Click the lyric:** a mini player opens with the album art, controls, progress and the scrolling lyrics.

No Spicetify, no Spotify login and no Premium needed. Taskitify reads what is playing through Windows' own media sessions and fetches lyrics from [LRCLIB](https://lrclib.net), an open database of synced lyrics.

## Features

| | |
|---|---|
| 🎤 **Synced lyrics** | The current line on the taskbar, in one or two lines, with a smooth transition. Keeps up with pauses, skips and seeks. |
| 🎨 **Album colors** | The background is a gradient taken from the album art, like Spotify's, and it fades to the new colors on every song. |
| 🎛️ **Taskbar player** | Previous, play/pause, next, shuffle, repeat, volume and a progress bar. You choose which ones appear. |
| 🪟 **Mini player** | One click on the lyric shows the album art, controls and the full scrolling lyrics with the current line highlighted. Click a line to jump to it. |
| ⏸️ **Minimized player** | When paused, Taskitify can shrink to the size of the weather widget (art, title and a play button) instead of hiding. |
| 🧩 **Build your bar** | A replica of the player in the settings: click each element to turn it on (green) or off (white). |
| 🖥️ **Made for Windows 11** | Hides with the taskbar in fullscreen, never steals focus, stays out of Alt+Tab and handles DPI scaling and dark mode. |
| 🪶 **Lightweight** | About 60 MB of RAM and ~1% of one CPU core while playing; idle when paused. Never runs as administrator. |

<p align="center">
  <img src="docs/images/miniplayer.png" alt="Mini player with scrolling lyrics" width="360" />
  &nbsp;&nbsp;
  <img src="docs/images/minimized.png" alt="Minimized player while paused" width="300" />
</p>
<p align="center"><sub>Mini player (click the lyric) · minimized player while the song is paused</sub></p>

## Installation

1. Download `Taskitify-win-x64.zip` from the [**Releases**](https://github.com/CarlosVinniFernandes/Taskitify/releases/latest) page.
2. Extract `Taskitify.exe` to a permanent folder, for example `Documents\Taskitify`.
3. Open `Taskitify.exe` and play a song on Spotify.

> **"Windows protected your PC"?** The executable is not signed with a paid certificate, so SmartScreen warns the first time. Click **More info** and then **Run anyway**. The code is open: you can review everything here or [build it yourself](#building).

**Requirements:** 64-bit Windows 11 (or Windows 10 version 1809 or later) and **Spotify Desktop** installed. No .NET installation needed.

## How to use

| Action | What happens |
|---|---|
| Play a song on Spotify | The lyric replaces the weather widget |
| Click the lyric | Opens or closes the mini player |
| `Esc` or click outside | Closes the mini player |
| Click a line in the mini player | Jumps the song to that line |
| Mouse wheel on the volume button | Changes Spotify's volume (click to mute) |
| Right-click the lyric | Search lyrics again, show song and artist, calibrate position, settings, exit |
| Tray icon | Settings, mini player and exit |

To start with Windows: **Settings → General → Start with Windows**.

## Customization

Under **Settings → Style**, Taskitify shows a replica of the player. Click each element, including the lyric: **green** shows on the taskbar, **white** stays hidden. The minimized player (while the song is paused) has its own builder and its own width.

<p align="center">
  <img src="docs/images/settings-style.png" alt="Style settings with the player builder" width="480" />
</p>

You can also choose:

- **Background:** the album-art gradient or the same color as the taskbar.
- **Main text:** the lyric or the song title.
- **Progress bar:** below everything, or to the right of the album art (with a larger cover).
- **When paused:** hide (the weather comes back) or minimize the player.
- **No synced lyrics:** show the song title or hide.
- **Font size, width, animations** and scrolling for text that does not fit.
- **Timing offset**, if the lyrics run early or late.
- **Position calibration**, in case Windows places the weather widget somewhere else.

## Privacy

- No account, no login and no telemetry.
- The only network request is the lyrics lookup on [LRCLIB](https://lrclib.net), with the song title, artist, album and duration.
- Settings and the lyrics cache stay on your computer: `%AppData%\Taskitify` and `%LocalAppData%\Taskitify`.

## FAQ

**The lyrics did not show up.** Some songs are not on LRCLIB; in that case the taskbar shows the song title. When LRCLIB is under heavy load, lyrics may take a few seconds to arrive. Right-click the lyric → **Search lyrics again** to force a new lookup.

**The lyrics are early or late.** Settings → Sync → **Timing offset**.

**The widget showed up in the wrong place.** Right-click → **Calibrate position**, drag it and adjust the width with the mouse wheel.

**Does it work with Spotify in the browser?** No. Only Spotify Desktop (on purpose, so it is never confused with videos or other tabs).

**Can I like a song from Taskitify?** No. Saving to your library requires the Spotify API with a login, which this project avoids.

## Building

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
dotnet build                                   # build
dotnet test                                    # 74 tests (lyrics, sync, colors, layout)
dotnet run --project src/Taskitify             # run

# Single executable that needs no .NET installed (~179 MB; ~69 MB zipped)
dotnet publish src/Taskitify -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o publish
```

To build the distribution zip, compress `publish\Taskitify.exe` together with `dist\LEIA-ME.txt`. Architecture, technical decisions and known limitations are documented (in Portuguese) in [**docs/COMO-FUNCIONA.md**](docs/COMO-FUNCIONA.md).

## Credits

- Lyrics: [LRCLIB](https://lrclib.net).
- Visual inspiration: [Spicetify Lyric Miniplayer](https://github.com/FO-SS/Spictify-Lyric-Miniplayer). Taskitify is a standalone native app, not a Spotify extension.
- Spotify is a trademark of Spotify AB. This project is not affiliated with Spotify or Microsoft.

## License

[MIT](LICENSE) © 2026 Carlos Fernandes
