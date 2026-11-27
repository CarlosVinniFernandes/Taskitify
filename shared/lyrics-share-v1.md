# Lyrics Share Link — contract v1

Status: **experimental** (desktop Phase 1). The web renderer does not exist yet; the desktop only
shows "Share as a link" in development builds, or with `TASKITIFY_SHARE_LINKS=1`.

A shared link carries one Lyrics Studio composition and, optionally, a dedication. Everything is in
the URL fragment, so the server never receives it:

```text
https://<base>/s#<code>
```

- Base address: `LyricShareLink.BaseUrl` in `src/Taskitify/Services/LyricShareLink.cs`, currently
  `https://taskitify.carlosfernandes.dev/s` (**placeholder until the site's address is decided**).
- The page reads `location.hash` (without `#`), decodes it and renders the composition in the browser.
  No backend, database, upload or HTTP request is involved in making or opening a link.

## 1. Encoding

```text
code = base64url_nopad( gzip( utf8( json ) ) )
```

1. **JSON**: the object in section 2, compact (no indentation), UTF-8. Properties left at their
   default are omitted. Text may contain any Unicode; characters outside the BMP (emoji) are written
   as JSON `\uXXXX` surrogate pairs by the desktop, which every JSON parser reads back.
2. **gzip** (RFC 1952). The desktop uses .NET `GZipStream` at `SmallestSize`. Decoders must accept any
   valid gzip stream (do not depend on header bytes such as OS or time).
3. **base64url** (RFC 4648 §5: `-` and `_`), **without** `=` padding. Decoders add padding back.

Decoding in the browser: `DecompressionStream("gzip")` (or a small library such as fflate), then
`TextDecoder("utf-8")` and `JSON.parse`.

## 2. Payload

Short property names keep links short. Types are JSON types.

| Key | Type | Required | Default | Meaning |
|---|---|---|---|---|
| `v` | integer | yes | — | Contract version: `1`. |
| `t` | string | yes | — | Template id (section 4). |
| `f` | string | no | `"story"` | Proportion: `story` 1080×1920, `square` 1080×1080, `portrait` 1080×1350, `landscape` 1920×1080. |
| `s` | object | yes | — | Song (below). |
| `l` | array | yes | — | Chosen lines, in order: 1 to 6 items. |
| `o` | object | no | all defaults | Look: shared visual settings (below). |
| `x` | object | no | template defaults | The template's own custom fields (section 4). |
| `d` | object | no | none | Dedication (below). Absent = the composition alone. |
| `c` | string | no | today | Date the link was made, `YYYY-MM-DD` (local). Fills `{date}` in fields. |

`s` — song

| Key | Type | Required | Meaning |
|---|---|---|---|
| `t` | string | yes | Title (≤ 120 characters). |
| `a` | string | yes | Artist (≤ 120). |
| `b` | string | no | Album (≤ 120). |
| `sp` | string | no | Spotify track id (22 letters/digits), when the desktop already knows it. Lets the page show the official artwork/embed. |

`l[]` — line

| Key | Type | Required | Meaning |
|---|---|---|---|
| `x` | string | yes | The line (≤ 200 characters). |
| `ms` | integer | no | Its time in the song, milliseconds (for the timestamp option and players). |

`o` — look (each key only when it differs from the default)

| Key | Type | Default | Values |
|---|---|---|---|
| `font` | string | `auto` (the template's own) | `modern`, `serif`, `handwritten`, `mono`, `condensed` |
| `tf` | string | none | The template's own typeface choice (same values as `font`), when the user picked one inside the template. |
| `scale` | number | `1` | Lyrics size, `0.7`–`1.4`, two decimals. Text that would not fit still shrinks. |
| `align` | string | `auto` | `left`, `center`, `right` |
| `color` | string | album colour | `#RRGGBB`, uppercase. Without it the page derives a colour (from the artwork when `sp` is known, else the template's palette). |
| `cover` | boolean | `true` | `false` hides the album artwork. |
| `mark` | string | `spotify` | Credit shown: `spotify`, `taskitify`, `none`. |
| `time` | boolean | `false` | `true` shows the first line's time. |
| `img` | string | `album` | Image the template uses: `album`, `personal`, `none`. **Images never travel**: for `personal` the page shows the template's placeholder (or the artwork). |

`d` — dedication (all optional; an empty dedication is omitted)

| Key | Type | Limit | Meaning |
|---|---|---|---|
| `from` | string | 40 | Sender's name. |
| `to` | string | 40 | Recipient's name. |
| `msg` | string | 280 | Personal message (may contain line breaks). |

Limits are in characters (Unicode text elements are never split). The desktop trims text, writes
empty text as absent and cuts what is longer than the limits.

## 3. Size

- The desktop refuses to make a link whose code exceeds **6,000 characters** and asks for fewer lines
  or a shorter message.
- Measured with the app's serializer: everything at its limits with hard-to-compress text is
  **4767 characters** (16 custom fields of 120 characters each); at its limits without custom fields, **2292**.
  Ordinary cards are 200–600.
- Decoders should reject codes over 8,000 characters and decompressed JSON over 64 KB.

## 4. Templates

`t` is the Studio style id: the `LyricCardStyle` member name, exactly as written below. These ids are
**stable**: a style may be renamed in the interface, never in the contract. If one ever must change,
the old id stays accepted as an alias.

`x` holds only the custom fields the user changed, keyed by the catalog key
(`src/Taskitify/Models/LyricStudioCatalog.cs`); a missing key means the catalog default. Values are
strings (≤ 120 characters, at most 16 keys, sorted). Defaults may contain tokens the renderer fills:
`{title}`, `{artist}`, `{album}`, `{date}` (`{date}` from `c`). Choice fields hold one of the
listed values; colour fields hold `#RRGGBB` or are absent (the template's own colour).

A renderer that does not know a template, or a field, falls back gracefully: unknown `t` → a neutral
card with the lines and the dedication; unknown `x` keys → ignored.

| Template id (`t`) | Category | Custom fields (`x`): choices, or the default when left out |
|---|---|---|
| `Classic` | Essentials | — |
| `Gradient` | Essentials | — |
| `Blur` | Essentials | — |
| `Minimal` | Essentials | — |
| `Karaoke` | Essentials | — |
| `Polaroid` | Memories | — |
| `PhotoBooth` | Memories | `title` = "After the music", `ref` = "PB–004 / CONTACT PRINT" |
| `Letter` | Memories | `model` (Vintage / Romantic / Minimal), `to` = "You", `hello` = "Dear you,", `bye` = "With love,", `sign` = "{artist}", `date` = "{date}", `ps` (free text) |
| `FilmNegative` | Memories | `frame` = "27A", `stock` = "35 MM / 400", `note` = "AVAILABLE LIGHT" |
| `Telegram` | Memories | `from` = "{artist}", `to` = "Someone I miss", `date` = "{date}", `stamp` = "Delivered with feeling", `note` = "End of transmission." |
| `MuseumExhibit` | Memories | `collection` = "Permanent collection", `id` = "Sound and memory · 027", `year` (free text), `note` = "Recorded sound. Private collection." |
| `Retro` | DigitalNostalgia | — |
| `MsnMessenger` | DigitalNostalgia | `model` (Classic / XP Blue / XP Silver), `who` = "{artist}", `status` = "Available", `time` = "{time}", `note` = "Send me a song…" |
| `WindowsXpError` | DigitalNostalgia | `title` = "Taskitify.exe", `message` = "Unable to forget.", `code` = "MEMORY_STILL_EXISTS", `icon` (Error / Warning / Info), `primary` = "Listen again", `secondary` = "Cancel" |
| `FloppyDisk` | DigitalNostalgia | `file` = "{title}.LRC", `device` (free text), `label` = "Side one / private copy", `id` = "1.44 MB" |
| `Terminal` | DigitalNostalgia | `user` = "listener", `host` = "localhost", `command` = "now-playing", `lyrics` = "lyrics --current", `note` = "selection saved", `theme` (Green / Amber / Mono / Blue / Modern) |
| `BlueScreen` | DigitalNostalgia | `intro` = "A memory has been detected.", `code` = "UNEXPECTED_FEELINGS", `note` = "Press any key to feel again." |
| `AimAway` | DigitalNostalgia | `user` = "midnight_listener", `time` = "{time}", `status` = "Away", `intro` = "If you need me, leave a song.", `note` = "Be back when the record ends." |
| `Calculator` | DigitalNostalgia | `expression` = "VALUE OF A MOMENT =", `result` = "Ans = ∞", `device` (free text) |
| `Paint98` | DigitalNostalgia | `file` = "{title}.bmp", `ink` (free text), `doodle` (free text), `doodle2` (free text), `canvas` (free text), `shape` (Stars / Hearts / Flowers / Notes / Sun / Scribbles / Underline / None), `deco` (Spray / Rainbow / Clouds / Sparkles / None) |
| `Orkut` | DigitalNostalgia | `profile` = "{artist}", `from` = "A friend", `date` = "{date}", `section` = "Testimonials" |
| `IpodClassic` | DigitalNostalgia | `model` (White / Black), `device` (free text), `label` = "Now playing", `engrave` = "Made for the mixtape" |
| `Player` | MusicCulture | — |
| `Vinyl` | MusicCulture | — |
| `CassetteTape` | MusicCulture | `model` (Black / Clear / White / Red), `sticker` (Stripe / Kraft / Rainbow / Plain), `title` = "Songs I kept", `to` = "For the road home", `side` (A / B), `label` (free text), `note` = "LOW NOISE / HIGH FEELING" |
| `ConcertTicket` | Collectibles | `id` = "TKT–027", `date` = "{date}", `title` = "One song only" |
| `LyricReceipt` | Collectibles | `paper` (White / Aged thermal), `title` = "MUSIC & MEMORIES", `shop` = "Taskitify Record Shop", `id` = "027", `date` = "{date}", `extra` = "PAYMENT: one good memory", `note` = "Thank you for listening!" |
| `BoardingPass` | Collectibles | `from` = "NOW", `to` = "MEMORY", `id` = "TK–027", `date` = "{date}", `note` = "Keep this moment close." |
| `LibraryCard` | Collectibles | `library` = "Library of Listening", `category` = "Recorded music", `stamp` = "On loan", `date` = "{date}", `return` = "Never" |
| `MusicPassport` | Collectibles | `country` = "Republic of Memories", `origin` = "Home", `issue` = "{date}", `expire` = "Never", `stamp` = "HEARD · REMEMBERED", `note` = "An imaginary document for a real song." |
| `ClassifiedDocument` | Collectibles | `dept` = "Department of Musical Memories", `class` = "Top secret", `id` = "TK–027", `stamp` = "Declassified", `note` = "Extracted evidence", `footer` = "Distribution limited to people who listen." |
| `Editorial` | Literary | — |
| `Dictionary` | Literary | `word` = "{title}", `category` (free text), `class` (free text), `number` = "1", `note` = "{artist}", `memo` (free text), `memo2` (free text) |
| `BookPage` | Literary | `chapter` = "Chapter VII", `page` = "127", `initial` (On / Off), `highlight` (On / Off), `note` = "{artist}" |
| `PeriodicTable` | Literary | `symbol` (free text), `id` = "027", `class` (free text), `state` (free text), `half` = "{duration}", `note` = "Observed properties" |
| `Crossword` | Literary | `word` = "{title}", `id` = "No. 27", `clue` = "A song worth keeping." |
| `GameBoy` | Gaming | `model` (Original gray / Clear / Yellow / Black), `device` (free text), `screen` (free text), `label` = "Pocket Lyrics", `note` = "PLAY MODE" |
| `NintendoDs` | Gaming | `model` (White / Black / Pink / Blue), `device` (free text), `intro` = "A song was discovered.", `note` = "Dual Sound" |
| `PokemonEncounter` | Gaming | `intro` = "A wild song appeared!", `level` = "98", `options` = "LISTEN / FAVORITE / SAVE / RUN", `note` = "RARE FIND" |
| `RpgInventory` | Gaming | `rarity` (Common / Rare / Epic / Legendary), `item` = "Memory", `rating` = "★★★★★", `value` = "Priceless", `trade` = "Never", `note` = "A moment held in sound." |
| `MagicCard` | Gaming | `type` = "Enchantment", `category` = "Memory", `rarity` (Common / Rare / Legendary / Holographic), `symbol` = "◆ ◇", `id` = "027 / 042" |
| `Chess` | Gaming | `title` = "The decisive move", `result` = "Unfinished", `light` (free text), `dark` (free text), `note` = "The quiet move is the one you remember." |

42 templates.

## 5. Versioning and compatibility

- `v` changes only for an incompatible change (a key changes meaning or type). Adding optional keys,
  templates, fields or enum values does **not** change `v`.
- Decoders ignore unknown keys, treat unknown enum values as the default, and must keep opening every
  version they ever supported (links live forever in chats).
- Encoders write the lowest version that represents the content.
- New templates get new ids; existing ids, field keys and their meaning never change.

## 6. Privacy and rights

- **The link is the content.** Anyone who has it can read the lyrics, names and message: base64 is an
  encoding, not encryption. The desktop says so next to the "Copy link" button.
- The fragment is not sent to the server, but it can be kept in browser history, chat previews and by
  anyone it is forwarded to.
- No images: artwork, personal photos and other binaries are never included.
- **Before publication:** confirm the right to show lyric excerpts on the website (licensing of the
  lyrics shown by a page that renders shared links). Until then the feature stays experimental and
  hidden in production builds.

## 7. Examples (generated by the app)

Made by `tools/LyricShareExamples` (`dotnet run --project tools/LyricShareExamples`), which uses the
app's own `LyricShareLink` and checks that every code decodes back to the same JSON. Songs, lines and
names are original demonstration content. A TypeScript decoder must turn each code into the JSON shown.

### `composition-only`

JSON (196 bytes, exactly as the app writes it):

```json
{"v":1,"t":"Paint98","s":{"t":"After the Rain","a":"Demo Artist","b":"Evening Studies"},"l":[{"x":"Every little moment","ms":13000},{"x":"Some moments stay with us.","ms":19000}],"c":"2026-11-27"}
```

Code (222 characters):

```text
H4sIAAAAAAACCjWNwQ6CMBAFf2Wz52JaTFR6I9G7kaPxgLpKE1oSuqCE8O8uoteZeXkj9miNQkaLx9IFznaoMKIdvyh_MLXAFcFJpJhS4J58A3nLLrKQq5BDT8GFJxTc3R1FnBTWaM8jvhfZDlA75prAN57CPPPyYdZa60ktWSHmpyNELgd4Oa6gi6t_nc31ReFN6lSnm8SYJN3i9AE_aSg6xAAAAA
```

### `customized-with-dedication`

JSON (570 bytes, exactly as the app writes it):

```json
{"v":1,"t":"LyricReceipt","f":"square","s":{"t":"Golden Hour","a":"Demo Artist","b":"Coastline","sp":"1a2B3c4D5e6F7g8H9i0J1k"},"l":[{"x":"Let the world slow down","ms":6000},{"x":"Golden hour on the avenue","ms":12000},{"x":"Every color sounds like you","ms":18000}],"o":{"font":"mono","scale":1.1,"align":"left","color":"#C98B3A","mark":"taskitify","time":true},"x":{"note":"Keep the change, keep the song","paper":"Aged thermal","title":"SONGS FOR YOU"},"d":{"from":"Carlos","to":"Ana","msg":"This one always makes me think of our walks by the sea."},"c":"2026-11-27"}
```

Code (528 characters):

```text
H4sIAAAAAAACCk1Q0U7cMBD8lZX7GlAS2uO4twMKqK16ErQPVdWHJdnkrDje1HYuRKf79-4GkPpi2eOZ2dk5moPZFJlJZmO-zcFWj1SRHZLJTCNQ_DtiIHlEszkupHt2NXl44DEIjILcUs-wDclGVT0LcsMYk7N-EQ4CFFheX1Qfbz_R6u6yXT9c2fxL0ZlTZpzZ_D6aF51OCdKeYOLgaoiOJ6h58mLRy_BVnuen7JX5FmEvEYD9IsID-ZHeuEX5H_nzgcIMFTsOEHn0dQRnO4KZx3f6Wul_MsO6Y8Ne1-zZs6av0JFQzqUidLb18uWo0UUXS3l-uLlaX19s1QxDJ0DC2Nlkm1mgZHuRpzCS7Pqi_p6TIOYr0bAkr_boW8qgewci-1aUAw6k9tuWasVDj24xTBrIPO2-3z_B3e4Rfu1-apH1Ej5wr_1jcByVzergcdm0lfuPvY3SmRTmJpwj9NiRnCQTrO-AG9BSJ3RdhOf5NQ_huQ6oRF7m5eqsKM7KS3P6B0bnWOs6AgAA
```

### `unicode-dedication`

JSON (352 bytes, exactly as the app writes it):

```json
{"v":1,"t":"Polaroid","f":"portrait","s":{"t":"Canção do Caminho","a":"Artista de Demonstração"},"l":[{"x":"Café quente na janela"},{"x":"o céu de outono em mim"}],"o":{"tf":"handwritten","cover":false,"img":"personal"},"d":{"from":"João","to":"Zoë \uD83C\uDF19","msg":"Feliz aniversário! ✨ Que essa música te acompanhe."},"c":"2026-11-27"}
```

Code (375 characters):

```text
H4sIAAAAAAACCiVPO07EQAy9inGdRWSR-KRDu9qCClo-hZU4ZFBmvMxMFkSUm9AgClhxA9q5CSfBExpL9vt6xB1WZYERK7ySnryYBgtsdd2Kj55M1DVgNc6UFbn0mT4EGoEVWeM6UZgUufDRhEjQMKzZiguqnak4FdhjdTviy2zQpj08DewigyN4JMc9KecfFqjTfsgmMkRxAmzBGovTfYEyl8jNOnLNszcxstP0WnbssWqpD1ygsQ-5O_sgjvoc3mRd68Xq_VJyI31XzfBG0jfcDeuz45XOTXmugA1ZvuHevAI5o84hvXsjB_D79gXXAwOHQGDTTzA1gT5BtdgtuY4Pc1it6uXR8mRRlovlKU5_voUPC2ABAAA
```

