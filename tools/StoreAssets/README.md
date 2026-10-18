# Microsoft Store image export

Run from the repository root on Windows:

```powershell
dotnet run --project tools/StoreAssets -- .
```

Exports PNG files to `dist/store-assets/en`:

- Four 1920 × 1080 English listing images using the actual production WPF views.
- Store icons at 71, 150 and 300 pixels square.
- Square store artwork at 1080 and 2160 pixels.
- Poster artwork at 720 × 1080, matching the dimensions requested by Partner Center.

The exported app views use an offline demonstration track, original sample lyrics and a vector album cover. Each presentation image identifies the demonstration content. These are rendered views, not captures of the user's desktop. The exporter neither changes the running app nor loads/saves user preferences, contacts LRCLIB, or controls Spotify. The existing Taskitify logo is reused for the store artwork. The app's current Windows theme is used to render settings.

The export tool is separate from the shipped application and is not included in the MSIX.
