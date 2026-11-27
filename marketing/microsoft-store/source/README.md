# Main Store screenshot

Composes `01-main-screenshot*.png` from the production WPF views: the mini player (Cinema wide, blurred cover,
Inter Display, expressive highlight, centred, Lyric Flow on) and the taskbar player, rendered with
`RenderTargetBitmap` at their final 4K scale, over a light browser and a Windows 11-style taskbar.

```powershell
dotnet run --project marketing/microsoft-store/source -c Release -- .
```

Writes, under `marketing/microsoft-store/`:

- `01-main-screenshot-preview.png`: internal review (reference song's name).
- `01-main-screenshot.png`: public version (demonstration track).
- `01-main-thumbnail.png`: public version at 308 × 173.
- `raw-captures/<edition>/`: mini player, taskbar widget, taskbar, browser and cover, separately.

Both editions use original demonstration lyrics and an original cover painted in the reference palette.
The tool runs on an isolated data folder (`%TEMP%\taskitify-store-shot`): it never reads or writes the user's
settings, never contacts Spotify or a lyrics provider, and does not start `Taskitify.App`, so a running
Taskitify is left alone. Build in Release when the app is running from the Debug folder.

## Campaign (five screenshots and the super hero art)

```powershell
dotnet run --project marketing/microsoft-store/source -c Release -- . --campaign   # app components
python marketing/microsoft-store/source/compose.py                                 # all six arts
python marketing/microsoft-store/source/compose.py 02 05                           # only some
```

`compose.py` combines the real Windows captures in `raw-captures/desktop`, the components and the CC0 photos into
`campaign/` (3840 × 2160) and `campaign/reduced/` (1280 × 720, 308 × 173 and a contact sheet).
Sources and licenses: `../SOURCES.md`. Page simulation: `../store-page-preview.html`.
