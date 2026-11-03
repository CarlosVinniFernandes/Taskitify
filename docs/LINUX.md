# Taskitify on Linux

Taskitify follows **Spotify Desktop** through MPRIS (D-Bus) and shows the synced lyric line either in a
floating bar or, on GNOME, **in the top bar next to the clock**, the Linux counterpart of covering the
Windows 11 weather widget. Lyrics come from LRCLIB (and your own `.lrc` files); no Spotify login is needed.

[Português](LINUX.pt-BR.md)

## Install

Download `taskitify_<version>_amd64.deb` from the releases and install it:

```bash
sudo apt install ./taskitify_1.0.0_amd64.deb
```

Or build it yourself (needs the .NET 8 SDK and `dpkg-deb`):

```bash
packaging/linux/build-deb.sh 1.0.0
```

Run `taskitify` (or start it from the app menu). It lives in the tray and in the top bar; leave Spotify
playing and the line shows up.

## The lyrics in the GNOME top bar

The package ships a GNOME Shell extension, `taskitify-lyrics@carlosvinnifernandes.github.io`, supporting
GNOME Shell 45 to 50. Enable it, then **log out and back in** (GNOME on Wayland only discovers new
extensions at login):

```bash
gnome-extensions enable taskitify-lyrics@carlosvinnifernandes.github.io
```

The line appears right after the Activities button, left of the clock, and hides itself when nothing is
playing. With the extension on, the floating bar is turned off automatically; bring it back in
**Settings → Floating lyrics bar**.

How it works: the app writes the current line to `$XDG_RUNTIME_DIR/taskitify/line.json`
(`{"line", "title", "artist", "playing"}`) and the extension just watches that file. Anything else can read it
too, for example a Waybar or Polybar module:

```bash
jq -r .line "$XDG_RUNTIME_DIR/taskitify/line.json"
```

## The floating bar and the mini player

- **Drag** the bar anywhere; the spot is remembered. Right-click → *Reset bar position* puts it back.
- **Click** the bar (or the tray icon) to open the mini player: cover, controls, progress and the full lyrics
  with the current line highlighted. Click a line to jump to it; `Esc` closes it.
- **Right-click** the bar: fetch the lyrics again (from LRCLIB or from the cache), settings, quit.
- The bar is an always-on-top window. GNOME on Wayland runs it through XWayland, which lets it keep its
  position; compositors that ignore "always on top" (some tiling setups) may need a window rule.

## Settings and files

| What | Where |
|---|---|
| Settings | `~/.config/taskitify/settings.json` |
| Lyrics cache | `~/.cache/taskitify/lyrics/` |
| Your own `.lrc` files | `~/.local/share/taskitify/lyrics/` (`Artist - Title.lrc`; used before LRCLIB) |
| Start at login | `~/.config/autostart/taskitify.desktop` (Settings → Start at login) |

The `XDG_*` variables are honoured. The 6 languages of the Windows app are shared (Settings → Language;
the default follows the system locale).

## Spotify's own lyrics (optional, experimental)

Same idea as the Windows app: Spotify first, LRCLIB when Spotify has no synced lyrics. **Settings → Spotify
lyrics → Connect** opens a Spotify sign-in window (WebKitGTK). It needs `python3-gi` and
`gir1.2-webkit2-4.1` (the `.deb` recommends them):

```bash
sudo apt install python3-gi gir1.2-webkit2-4.1
```

The sign-in lives in its own profile, `~/.local/share/taskitify/spotify-web-session`; Spotify Desktop's and
your browser's profiles are never read. The hidden web player starts only when a token or search is needed
and stops 30 s later. **Disconnect** deletes that profile. It uses Spotify's unofficial web-player service,
so it can stop working when Spotify changes it; LRCLIB keeps working.

## What is not on Linux yet
- **Volume** is controlled in Spotify or the system mixer, not from the bar.
- Spotify in the browser is not followed (same as Windows, on purpose).

## Development

```bash
dotnet test tests/Taskitify.Tests                                  # Core, shared with Windows
dbus-run-session -- dotnet test tests/Taskitify.Platform.Linux.Tests   # MPRIS against a fake player
dotnet run --project src/Taskitify.Linux
xvfb-run -a python3 tests/spotify-web-helper/test_helper.py        # the WebKitGTK helper, against a local fake web player
```

No Spotify account needed: `tools/fake-spotify/fake_spotify.py` plays a small fake playlist on
`org.mpris.MediaPlayer2.spotify`, answers play/pause/next/previous/seek/shuffle/repeat, and uses songs that
exist on LRCLIB. Needs `python3-dbus` and `python3-gi`.

Layout:

```
src/Taskitify.Core            Media/IMediaSource, Lyrics/LyricsSession: no OS or UI types, shared with Windows
src/Taskitify.Platform.Linux  MPRIS client, XDG paths, autostart, line file, settings, Spotify web session
src/Taskitify.Linux           Avalonia app: bar, mini player, settings, tray; SpotifyWeb/spotify_web.py is the WebKitGTK helper
gnome-extension/              the top-bar extension
packaging/linux/              .deb build, .desktop file
```

The Windows app keeps its own GSMTC implementation (`SpotifyMonitor`) and is unchanged.
