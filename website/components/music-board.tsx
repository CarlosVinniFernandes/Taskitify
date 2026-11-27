import type { CSSProperties, ReactNode } from "react";
import { copy, type Locale } from "@/lib/copy";
import { guestbookCopy } from "@/lib/guestbook-copy";
import type { WallPage } from "@/lib/community-wall";
import styles from "./community-wall.module.css";

const papers = ["#efebe0", "#e7e8df", "#efe6da", "#ede8e3", "#e9ece7", "#f0ece4"] as const;

function noteStyle(seed: number) {
  return {
    mount: (seed >>> 23) % 3,
    style: {
      "--tint": papers[(seed >>> 8) % papers.length],
      "--tilt": (((seed >>> 17) % 7 - 3) * .48) + "deg",
      "--note-width": (183 + ((seed >>> 15) % 3) * 15) + "px",
      "--note-top": (18 + (seed % 3) * 3) + "px",
    } as CSSProperties,
  };
}

export function MusicBoard({ locale, result, page = 1, full, onRetry, onWrite, retryHref, actions, children }: {
  locale: Locale;
  result: WallPage | "error" | null;
  page?: number;
  full?: boolean;
  onRetry?: () => void;
  onWrite?: () => void;
  retryHref?: string;
  actions?: ReactNode;
  children?: ReactNode;
}) {
  const TrackHeading = full ? "h2" : "h3";
  const t = copy[locale].community.wall;
  const book = guestbookCopy[locale];
  const date = new Intl.DateTimeFormat(locale, { dateStyle: "medium", timeZone: "UTC" });
  const entries = result && result !== "error" ? result.entries : [];
  const hasMore = result !== null && result !== "error" && result.hasMore;

  return <section className={`wrap ${styles.wall}${full ? " " + styles.wallFull : ""}`} id="wall" aria-labelledby="wall-title" aria-busy={result === null}>
    <header className={styles.heading}>
      {full ? <h1 id="wall-title">{t.title}</h1> : <>
        <p className={styles.kicker}>{t.title}</p>
        <h2 id="wall-title">{book.boardTitle}</h2>
        <p>{book.boardIntro}</p>
      </>}
      {actions}
    </header>
    <div className={styles.board}>
      {entries.length > 0 ? <ul className={styles.tracks}>
        {entries.map((entry, index) => {
          const query = `${entry.song} ${entry.artist}`.replace(/[\uD800-\uDFFF]/gu, "�");
          const { mount, style } = noteStyle(entry.artSeed);
          return <li className={styles.recommendation} key={`${entry.artSeed}-${index}`} data-mount={mount} style={style}>
            <div className={styles.track}>
              <TrackHeading dir="auto">{entry.song}</TrackHeading>
              <p dir="auto">{entry.artist}</p>
              {entry.name && <p className={styles.attribution}><bdi>{entry.name}</bdi></p>}
              <p className={styles.meta}>
                <time dateTime={entry.date}>{date.format(new Date(entry.date))}</time>
                {entry.status === "added" && <span className={styles.added}>{t.added}</span>}
              </p>
            </div>
            <a className={styles.spotify} href={`https://open.spotify.com/search/${encodeURIComponent(query)}`}
              target="_blank" rel="noreferrer">{t.spotify}</a>
          </li>;
        })}
      </ul> : <p className={styles.boardMessage} role="status">{result === "error" ? t.error
        : result === null ? t.loading : hasMore || page > 1 ? t.emptyPage : t.empty}</p>}
      {result === "error" && (onRetry
        ? <button className={styles.boardButton} type="button" onClick={onRetry}>{t.retry}</button>
        : <a className={styles.boardButton} href={retryHref}>{t.retry}</a>)}
      {onWrite && <div className={styles.boardFooter}>
        <a href="#suggest" className={styles.pinCta} onClick={onWrite}>+ {book.boardCta}</a>
      </div>}
    </div>
    {children}
  </section>;
}
