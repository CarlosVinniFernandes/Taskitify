# Personalization Expansion

Branch `t3/personalization-expansion`, from `t3/release-candidate` (aca354f). Not merged.

## Implemented

| Area | What |
|---|---|
| Minimal Mode | Floating lyrics window (`MinimalLyricsWindow`), own Settings page, tray switch, onboarding step |
| Personal looks | Save / name / apply / update / delete mini player looks beside the templates |
| Background intensity | Atmosphere, darken and blur for the existing mini player backgrounds |
| Per-song sync | An adjustment for the song playing, added to the global one |
| Tour | Minimal Mode step shows the real feature; last card has two playlist buttons side by side |

### Minimal Mode
- Defaults (game-friendly, per review): five verses graduated in size (small, medium, current, medium, small), no background, outline for legibility, no title/controls/buttons, clicks pass through, topmost. Off until turned on.
- Options: verses shown (1, current+next, 3, 5), graduated sizes on/off, highlight, motion, alignment, typeface, size, spacing, background (clear/soft/album) + opacity, text colour (white/warm/album/dark), legibility (none/shadow/outline), title+artist, playback controls, keep the taskbar player, topmost, lock, click-through.
- Window: drag anywhere, resize from 10 px edges or an always-visible corner grip; position/size persist in physical px and come back on screen when a monitor/resolution is gone (`FloatingPlacement`). Toolbar (mini player, settings, lock, close) is always visible while clicks are caught, never hover-only.
- Click-through: `WS_EX_TRANSPARENT`, no polling; turned off from the tray item or Settings (the window's buttons hide while it is on).
- Games: never takes focus (`WS_EX_NOACTIVATE`, tool window); topmost is reasserted on foreground changes (WinEvent hook + one delayed retry), so it stays over borderless full-screen games.
- Taskbar player hides while it is on (new hide reason; its layout settings untouched) unless "keep the taskbar player" is on; the mini player opens from the lyrics' toolbar or the tray.
- Sync: same `LyricsSyncService` (track change, pause, seek, offsets, Lyric Flow by line and by word, no lyrics). No timer of its own; hidden = unsubscribed. Nothing shown while nothing plays; only the song when there are no synced lyrics.

## UX decisions
- Every option is on the page, no submenus; pictured choices reuse `ThumbTile`/`ChoiceList` and `MiniPlayerLookThumbnails` (new pictures: verses shown, background/text/legibility over a real desktop photo with the song's colours).
- Changes apply at once and show on the real window (preview while Minimal Mode is off, ended when Settings close), like the mini player.
- Personal looks live in the Templates section: one Save button, inline name box (focused after saving), Update and Delete on the same row; a small person badge marks them. Look and layout stay independent (layout and width are never stored).
- Inapplicable background sliders stay in place, disabled, with one line saying why.
- Sync: "All songs", "This song" (slider, −100/Reset/+100), "Applied now". Song adjustments save only when non-zero; only clearing all of them asks for confirmation.

## Reused components
`LyricLineViewModel.Emphasize`, `LyricMotionText`, `LyricInkDriver`/`LyricInk` (Lyric Flow), `LyricsSyncService`, `MiniPlayerBackdrop`, `MiniPlayerLookThumbnails`, `LookOption<T>`, `ThumbTile`, `ChoiceList`, `ToggleSwitch`, `FlatButton`/`AccentButton`, `ForegroundWatcher`, `WindowInterop`, `GpuRendering`, tour building blocks.

## New settings (all optional in old files)
- `Minimal` (`MinimalModeSettings`): Enabled, KeepTaskbar, Topmost, Locked, ClickThrough, Highlight, Motion, Typeface, Alignment, FontSize, Lines, GraduatedSizes, LineSpacing, Backdrop, BackdropOpacity, TextColor, Legibility, ShowSong, ShowControls, X/Y/Width/Height.
- `MiniPlayerPresets`: list of `{ Name, Look, Elements }`.
- `MiniPlayerLook.BackgroundEffect` (1), `BackgroundDim` (0), `BackgroundBlur` (1): defaults = backgrounds as before.
- `TrackOffsets`: `{ "artist — title": ms }`, ±3000 per song, sum clamped to ±5000.

## Verification
- `dotnet test tests/Taskitify.Tests`: 240 passed (new: placement recovery, defaults/compatibility, presets apply/persist, intensity mapping/limits, per-song key/sum/limits/persistence; localization completeness covers the 6 languages).
- Build: Debug, 0 errors.
- Real app runs on a throwaway copy of the user's data (`TASKITIFY_DATA_DIR`, English for screenshots): Minimal Mode over the desktop with real songs (track changes, Lyric Flow ink, 1/3/5 verses, left/centre, album/soft/clear, outline), window ex-style checked (`TRANSPARENT|LAYERED|TOPMOST|NOACTIVATE|TOOLWINDOW`), placement on first run and after a lost position, taskbar player hidden, settings pages (Minimal, mini player templates with a personal look, background sliders, sync section), tour steps in en/pt/it.

## Known limitations
- Exclusive full-screen games (the game owns the display) show no other window; borderless/windowed full screen works.
- Graduated sizes are a render scale: every verse keeps the same slot, so spacing is even rather than tight.
- Per-song key is artist + title: Windows gives no Spotify id, and the id Spotify's lookup learns is missing for cached/local/LRCLIB lyrics. Two different songs with identical names share an adjustment.
- Tray/Settings are the only way back from click-through by design (nothing on the window can be clicked).
- The Lyrics page's Lyric Flow hint still names only the bar and the mini player (the Minimal page says it follows that setting).

## Pending
- Manual check by the user on the real session: dragging/resizing by hand and a borderless full-screen game.
- Optional: an "Open Minimal Mode settings" item in the mini player's right-click menu.
