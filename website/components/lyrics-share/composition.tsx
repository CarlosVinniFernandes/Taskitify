import { Music2 } from "lucide-react";
import type { CSSProperties } from "react";
import { songTime, type SharedSong } from "@/lib/lyrics-share";
import styles from "./viewer.module.css";

export function Composition({ payload }: { payload: SharedSong }) {
  const { s: song, l: lines, o: look, f: format, lang } = payload;
  const font = look.tf !== "auto" ? look.tf : look.font;
  const timestamp = look.time ? lines[0].ms : undefined;
  const color = look.color ?? "#65BAAC";
  const dense = lines.some((line) => line.x.length > 80) || lines.reduce((size, line) => size + line.x.length, 0) > 220;
  const cssVars = {
    "--share-ink": color,
    "--lyric-scale": look.scale,
    "--lyric-align": look.align === "auto" ? "center" : look.align,
  } as CSSProperties;

  return <figure className={styles.composition} data-format={format} data-font={font}
    data-dense={dense || undefined} style={cssVars} lang={lang}>
    <div className={styles.cardContents}>
      <div className={styles.cardIntro} aria-hidden="true">
        {look.cover && look.img !== "none" && <span className={styles.recordMark}><Music2 size={18} /></span>}
        {timestamp !== undefined && <span className={styles.timestamp}>{songTime(timestamp)}</span>}
      </div>
      <blockquote className={styles.lyrics}>
        {lines.map((line, index) => <p key={index} dir="auto">{line.x}</p>)}
      </blockquote>
      <p className={styles.song}>
        <strong dir="auto">{song.t}</strong>
        <span dir="auto">{song.a}</span>
        {look.mark !== "none" && <small>{look.mark === "spotify" ? "Spotify" : "Taskitify"}</small>}
      </p>
    </div>
  </figure>;
}
