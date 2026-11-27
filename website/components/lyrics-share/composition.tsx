import type { CSSProperties, ReactNode } from "react";
import { songTime, type SharedSong } from "@/lib/lyrics-share";
import { shareCopy, type ShareLocale } from "@/lib/lyrics-share-copy";
import styles from "./composition.module.css";

type Props = { payload: SharedSong; locale: ShareLocale };

function duration(ms?: number) {
  if (ms === undefined) return "—";
  const seconds = Math.floor(ms / 1000);
  return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, "0")}`;
}

export function artKind(template: string) {
  if (template === "Letter") return "letter";
  if (template === "CassetteTape") return "cassette";
  if (template === "Retro") return "retro";
  return "editorial";
}

function fields({ payload, locale }: Props) {
  const { s, l, c, x } = payload;
  const date = c ? new Date(c + "T12:00:00") : new Date();
  const time = (ms?: number) => ms === undefined ? "—" : songTime(ms);
  const tokens: Record<string, string> = {
    title: s.t, artist: s.a, album: s.b ?? "",
    date: new Intl.DateTimeFormat(locale, { day: "numeric", month: "short", year: "numeric" }).format(date),
    time: time(l[0].ms), duration: duration(s.dur),
  };
  return (key: string, fallback: string) => (Object.hasOwn(x, key) ? x[key] : fallback)
    .replace(/\{(title|artist|album|date|time|duration)\}/g, (_, token: string) => tokens[token]);
}

function choice<T extends string>(value: string, options: readonly T[], fallback: T) {
  return options.find((option) => option === value) ?? fallback;
}

function CopyNote({ payload, locale }: Props) {
  const d = payload.d;
  if (!d || !(d.from || d.to || d.msg)) return null;
  const t = shareCopy[locale];
  return <aside className={styles.note} aria-label={t.dedication}>
    {d.to && <p><span lang={locale}>{t.to}</span> <bdi>{d.to}</bdi></p>}
    {d.msg && <p className={styles.message} dir="auto">{d.msg}</p>}
    {d.from && <p className={styles.sender}><span lang={locale}>{t.from}</span> <bdi>{d.from}</bdi></p>}
  </aside>;
}

function Lyrics({ payload }: { payload: SharedSong }) {
  return <blockquote className={styles.lyrics}>
    {payload.l.map((line, index) => <p key={index} dir="auto">{line.x}</p>)}
  </blockquote>;
}

function SongDetails({ payload }: { payload: SharedSong }) {
  return <p className={styles.track}><strong dir="auto">{payload.s.t}</strong><span dir="auto">{payload.s.a}</span></p>;
}

function Credit({ payload }: { payload: SharedSong }) {
  const { o, l } = payload;
  const time = o.time && l[0].ms !== undefined ? songTime(l[0].ms) : "";
  return (time || o.mark !== "none") && <div className={styles.mark}>
    {time && <span>{time}</span>}
    {o.mark !== "none" && <span>{o.mark === "spotify" ? "Spotify" : "Taskitify"}</span>}
  </div>;
}

function Letter(props: Props) {
  const { payload } = props;
  const get = fields(props);
  const model = choice(get("model", "Vintage"), ["Vintage", "Romantic", "Minimal"], "Vintage");
  const post = get("ps", "");
  return <div className={styles.letter} data-model={model}>
    <div className={styles.letterEnvelope} aria-hidden="true">
      <span className={styles.postage}>♪</span>
      <span className={styles.postmark} />
    </div>
    <div className={styles.letterPaper}>
      <span className={styles.washi} aria-hidden="true" />
      {model === "Romantic" && <span className={styles.pressedFlower} aria-hidden="true"><i /><i /><i /></span>}
      <header className={styles.address}><bdi>{get("to", "You")}</bdi><bdi>{get("date", "{date}")}</bdi></header>
      <p className={styles.greeting} dir="auto">{get("hello", "Dear you,")}</p>
      <CopyNote {...props} />
      <Lyrics payload={payload} />
      <div className={styles.signature}>
        <p dir="auto">{get("bye", "With love,")}</p>
        <p dir="auto">{get("sign", "{artist}")}</p>
      </div>
      {post && <p className={styles.postscript} dir="auto">P.S. {post}</p>}
      <SongDetails payload={payload} />
      <Credit payload={payload} />
    </div>
  </div>;
}

function Cassette(props: Props) {
  const { payload } = props;
  const get = fields(props);
  const model = choice(get("model", "Black"), ["Black", "Clear", "White", "Red"], "Black");
  const sticker = choice(get("sticker", "Stripe"), ["Stripe", "Kraft", "Rainbow", "Plain"], "Stripe");
  const side = choice(get("side", "A"), ["A", "B"], "A");
  const title = get("title", "Songs I kept");
  const tapeNote = get("note", "LOW NOISE / HIGH FEELING");
  const label = get("label", "");
  const labelColor = /^#[\da-f]{6}$/i.test(label) ? label : payload.o.color ?? "#e9c46a";
  return <div className={styles.mixtape} data-shell={model} data-label={sticker} data-side={side}
    style={{ "--label-color": labelColor } as CSSProperties}>
    <div className={styles.tape}>
      <div className={styles.sticker}>
        <b className={styles.side}>{side}</b>
        <div className={styles.stickerText}><p dir="auto">{title}</p><small dir="auto">{payload.s.a} • {payload.s.t}</small></div>
        <span className={styles.tapeLength}>{sticker === "Rainbow" ? "C-90" : "C-60"}</span>
      </div>
      <div className={styles.tapeWindow} aria-hidden="true"><i /><span /><i /></div>
      <div className={styles.tapeBottom}><p dir="auto" aria-hidden={tapeNote.length > 25 || undefined}>{tapeNote}</p><i aria-hidden="true">●　●　　　●　●</i></div>
      <div className={styles.screws} aria-hidden="true"><i /><i /><i /><i /></div>
    </div>
    <div className={styles.jcard}>
      <span className={styles.indexBand}>SIDE {side}</span>
      <div className={styles.jcardText}>
        <p className={styles.jcardTitle} dir="auto">{title}</p>
        <Lyrics payload={payload} />
        <div className={styles.tracklist}>
          <span dir="auto">{side}1&nbsp; {payload.s.t} — {payload.s.a}</span>
          {payload.s.dur !== undefined && <span className={styles.runtime}>{duration(payload.s.dur)}</span>}
        </div>
        {tapeNote.length > 25 && <p className={styles.jcardFootnote} dir="auto">{tapeNote}</p>}
        <CopyNote {...props} />
        <p className={styles.jcardTo} dir="auto">{get("to", "For the road home")} ↗</p>
        <Credit payload={payload} />
      </div>
    </div>
    <span className={styles.pencil} aria-hidden="true" />
  </div>;
}

function DesktopWindow({ title, children, player = false }: { title: string; children: ReactNode; player?: boolean }) {
  return <div className={styles.desktopWindow} data-player={player || undefined}>
    <div className={styles.titleBar}><bdi>{title}</bdi><span aria-hidden="true"><i>–</i><i>□</i><i>×</i></span></div>
    {children}
  </div>;
}

function Retro(props: Props) {
  const { payload, locale } = props;
  const { o, l } = payload;
  const get = fields(props);
  return <div className={styles.desktop}>
    <div className={styles.workspace}>
      <DesktopWindow title={get("title", "{title}.txt")}>
        <p className={styles.menu} aria-hidden="true">File &nbsp; Edit &nbsp; Search &nbsp; Help</p>
        <Lyrics payload={payload} />
      </DesktopWindow>
      <div className={styles.desktopSide}>
        <DesktopWindow title={shareCopy[locale].nowPlaying} player>
          <div className={styles.deck}>
            {o.cover && o.img !== "none" && <span className={styles.deckCover} aria-hidden="true">♫</span>}
            <div className={styles.deckControls}>
              <SongDetails payload={payload} />
              <span className={styles.seek} aria-hidden="true" />
              <span className={styles.playerKeys} aria-hidden="true">{["|◀", "▶", "▮▮", "▶|"].map((key) => <i key={key}>{key}</i>)}</span>
            </div>
          </div>
        </DesktopWindow>
        <CopyNote {...props} />
      </div>
      {o.mark !== "none" && <span className={styles.desktopMark}>{o.mark === "spotify" ? "Spotify" : "Taskitify"}</span>}
    </div>
    <div className={styles.taskbar} aria-hidden="true">
      <span>♫ &nbsp;Taskitify</span>
      {o.time && l[0].ms !== undefined && <span>{songTime(l[0].ms)}</span>}
    </div>
  </div>;
}

function Editorial(props: Props) {
  const { payload } = props;
  return <div className={styles.editorial}>
    <CopyNote {...props} />
    <span className={styles.musicGlyph} aria-hidden="true">♪</span>
    <Lyrics payload={payload} />
    <SongDetails payload={payload} />
    <Credit payload={payload} />
  </div>;
}

const renderers = { letter: Letter, cassette: Cassette, retro: Retro, editorial: Editorial };

export function Composition(props: Props) {
  const { payload } = props;
  const { o: look, l: lines } = payload;
  const kind = artKind(payload.t);
  const Renderer = renderers[kind];
  const font = look.tf !== "auto" ? look.tf : look.font;
  const dense = lines.some((line) => line.x.length > 80) || lines.reduce((size, line) => size + line.x.length, 0) > 220;
  const cssVars = {
    "--accent": look.color ?? (kind === "retro" ? "#42678a" : "#8d5840"),
    "--lyric-scale": look.scale,
    "--lyric-align": look.align === "auto" ? "left" : look.align,
  } as CSSProperties;
  return <figure className={styles.composition} data-art={kind} data-format={payload.f} data-font={font}
    data-dense={dense || undefined} style={cssVars} lang={payload.lang}>
    <Renderer {...props} />
  </figure>;
}
