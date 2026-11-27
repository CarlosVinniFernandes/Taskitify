# Lyric Flow study

Measures how closely Lyric Flow's estimate (line-synced lyrics, no word timings) follows the singer,
on [JamendoLyrics](https://github.com/f90/jamendolyrics): 79 Creative Commons songs in English, French,
German and Spanish, annotated word by word. Prints only error statistics.

```powershell
python tools/LyricFlowStudy/prepare.py dataset.json            # downloads the annotations (kept local)
dotnet run --project tools/LyricFlowStudy -c Release -- dataset.json        # the app's LyricFlow and variants
dotnet run --project tools/LyricFlowStudy -c Release -- dataset.json grid   # parameter search, even/odd songs
```

Error = |estimated − annotated| time at which each word starts and ends filling, over 21,580 words.

| Model | Mean error | Median | 90th pct. | Within 300 ms | Bias |
|---|---|---|---|---|---|
| Before (line spread over the time to the next line) | 499 ms | 259 ms | 1259 ms | 54.6% | +402 ms (late) |
| Now (neighbours' pace, held last word, rest) | 334 ms | 166 ms | 824 ms | 67.8% | +37 ms |

The chosen parameters win on both halves of the songs (even/odd), so they are not fitted to a few
songs. The English-only silent-e rule (−2 ms overall) is left out: the app does not know the language.
