"""Downloads JamendoLyrics (word-annotated, Creative Commons songs; github.com/f90/jamendolyrics) and
writes dataset.json for the study. The data stays local: it is not part of this repository."""
import csv, io, json, sys, urllib.request, zipfile

out = sys.argv[1] if len(sys.argv) > 1 else "dataset.json"
z = zipfile.ZipFile(io.BytesIO(urllib.request.urlopen("https://github.com/f90/jamendolyrics/archive/refs/heads/master.zip").read()))
root = "jamendolyrics-master/"
read = lambda name: list(csv.DictReader(io.StringIO(z.read(root + name).decode("utf-8"))))
songs = []
for r in read("JamendoLyrics.csv"):
    name = r["Filepath"].rsplit(".", 1)[0]
    lines, words, k = read(f"annotations/lines/{name}.csv"), read(f"annotations/words/{name}.csv"), 0
    entry = []
    for line in lines:
        n = len(line["lyrics_line"].split())
        entry.append({"start": float(line["start_time"]), "end": float(line["end_time"]), "text": line["lyrics_line"],
                      "words": [[float(w["word_start"]), float(w["word_end"])] for w in words[k:k + n]]})
        k += n
    songs.append({"lang": r["Language"], "lines": entry})
json.dump(songs, open(out, "w", encoding="utf-8"))
print(len(songs), "songs")
