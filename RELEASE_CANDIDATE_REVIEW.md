# Release candidate review

Branch `t3/release-candidate` (from `t3/lyrics-share`, which already contains `main`). The other agents'
branches (website, community, short links) do not touch the desktop app; nothing of theirs was changed.
The 42 Lyrics Studio export templates, the website, the share contract and the Vercel setup were not touched.

## Problems found and fixed

| Priority | Problem | Fix |
|---|---|---|
| P0 | An unreadable `settings.json` was silently replaced by defaults on the next change (all preferences lost). | The unreadable file is kept as `settings.json.invalid` before anything is saved. |
| P1 | First run still opened the old welcome dialog; the new tour was reachable only from a temporary tray item, and updating users never saw it. | The tour opens once for new installations and once for users updating (`TourVersion`), is marked seen as soon as it closes (saved at once), and can be reopened from Settings › General and the tray. |
| P1 | Mini player: choosing "no motion" changed the whole composition (Cinema lyrics 28 → ~22 px, other weights, spacing and line sizes), so disabling animation changed the look. | "No motion" keeps the smooth layout and only stops animating it. The global "animations" switch already only changed durations. |
| P2 | Icon-only buttons (previous, next, shuffle, play/pause, Settings) had tooltips but no accessible name: screen readers read the icon glyph. | 10 buttons get their tooltip text as `AutomationProperties.Name`. |

## Improvements delivered in this cycle (already on the branch)

- Lyric Flow: words follow the pace of nearby lines, the last word is held, then a rest. Mean word error 499 → 334 ms, bias +402 → +37 ms on JamendoLyrics (79 songs, 21,580 words, four languages; `tools/LyricFlowStudy`).
- Guided setup in the tour (taskbar elements, paused behavior, mini player look), saved only on confirm and only what changed.
- Store rating invitation: after 14 days since first use and 7 days with music, one tray notification offers the Store review page (`ms-windows-store://review/?PFN=…`). Store package only, never during the first run or the tour, shown once, local state only, no star request.

## Performance (measured)

- Tour: CPU per step down from up to 93 % of a core to 5–31 % (live mini player through a VisualBrush, players asleep on other steps, pre-rendered backgrounds).
- Background use, Release build, Spotify running: 0.68 % of one core over 30 s, 161 MB working set, 29 threads. No idle problem found, so nothing was changed there.

## Verified

- Build (Release and Debug), full test suite 217/217.
- Tour lifecycle on an isolated copy of the settings: opens on first launch, marks `TourVersion = 1` on close, does not open on the next two launches.
- Guided setup: save writes only changed values (bar width and lyric size preserved), undo restores, continue without saving changes nothing.
- Lyric Flow: app and study model give identical numbers; flow tests pass.
- Short links: success, reuse while unchanged, new link after a change, 429, foreign domain and offline all behave as specified (local stub server).

## Remaining risks

- Not exercised on this machine: multiple monitors, auto-hide taskbar, full-screen apps, DPI changes and Explorer restarts (the code handles `TaskbarCreated`; no regression was introduced, but they were not re-tested).
- Rating invitation and `ms-windows-store://review` were only verified to stay silent outside the Store package; the packaged path needs a check on an installed MSIX.
- "No motion" now looks like smooth motion without movement; users who preferred the old static small-type look will see larger lyrics.
- The old first-run `WelcomeWindow` is no longer opened (kept in the code, unused).

## Before publishing to the Microsoft Store

- Confirm the contact address (`ContactInfo.Email`, currently `hello@carlosfernandes.dev`).
- Install the MSIX once: clean install (tour appears), update over the current Store version (settings kept, tour appears once), rating invitation path.
- Check that Release builds hide the experimental share-as-a-link (they do unless `TASKITIFY_SHARE_LINKS=1`); the website keeps its viewer and API disabled.
- Review the five non-English tour and rating texts with native speakers.
- Store screenshots in `marketing/microsoft-store/campaign` (titles and Windows UI language noted in `SOURCES.md`).

## Deliberately left for later (P3)

- Minimal Mode: shown in the tour only as a concept ("Coming soon"); not implemented.
- Lyric sharing (links, short links): experimental until the lyrics rights review.
- Language-aware syllable rules for Lyric Flow (English silent e, CJK) — the app does not know the lyrics' language; gain measured at 2 ms.
- Settings reorganisation (out of scope of this round).
