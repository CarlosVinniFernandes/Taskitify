"use client";

import { useSyncExternalStore, type CSSProperties, type ReactNode } from "react";
import { copy, type Locale } from "@/lib/copy";
import { guestbookCopy } from "@/lib/guestbook-copy";
import { pathFor } from "@/lib/site";
import { communityWallPageSizes, type WallEntry, type WallPage } from "@/lib/community-wall";
import styles from "./community-wall.module.css";

const receiptKey = "taskitify-community-contribution";
let currentReceipt: string | undefined;

export function rememberContribution(entry: WallEntry) {
  currentReceipt = JSON.stringify(entry);
  try { sessionStorage.setItem(receiptKey, currentReceipt); } catch { /* The current tab can still show it in memory. */ }
  window.dispatchEvent(new Event(receiptKey));
}

function subscribe(onChange: () => void) {
  window.addEventListener(receiptKey, onChange);
  return () => window.removeEventListener(receiptKey, onChange);
}

function getReceipt() {
  if (currentReceipt !== undefined) return currentReceipt;
  try { return sessionStorage.getItem(receiptKey); } catch { return null; }
}

function personalEntry(receipt: string | null): WallEntry | null {
  if (!receipt) return null;
  try {
    const entry: WallEntry = JSON.parse(receipt);
    return Number.isInteger(entry.artSeed) && typeof entry.song === "string"
      && typeof entry.artist === "string" && typeof entry.name === "string"
      && (entry.country === undefined || typeof entry.country === "string")
      && /^\d{4}-\d{2}-\d{2}$/.test(entry.date) && Number.isFinite(Date.parse(entry.date)) ? entry : null;
  } catch {
    return null;
  }
}

const papers = ["#efebe0", "#e7e8df", "#efe6da", "#ede8e3", "#e9ece7", "#f0ece4"] as const;

function noteStyle(seed: number) {
  return {
    mount: (seed >>> 23) % 3,
    style: {
      "--tint": papers[(seed >>> 8) % papers.length],
      "--tilt": (((seed >>> 17) % 7 - 3) * .48) + "deg",
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
  const publicEntries = result && result !== "error" ? result.entries : [];
  const receipt = useSyncExternalStore(subscribe, getReceipt, () => null);
  const ownSong = page === 1 ? personalEntry(receipt) : null;
  const matchesOwn = (entry: WallEntry) => ownSong?.artSeed === entry.artSeed && ownSong.date === entry.date;
  const pinned = ownSong && (publicEntries.find(matchesOwn) ?? ownSong);
  const entries = pinned ? [pinned, ...publicEntries.filter((entry) => !matchesOwn(entry))] : publicEntries;
  const hasMore = result !== null && result !== "error" && result.hasMore;
  const capacity = full ? communityWallPageSizes.full : communityWallPageSizes.preview;
  const packed = entries.length >= capacity;
  const vacancies = Math.max(0, capacity - entries.length - 1);
  const writeHref = full ? `${pathFor(locale, "community")}#suggest` : "#suggest";
  const invite = <a href={writeHref} className={styles.pinCta} onClick={onWrite}>
    <span aria-hidden="true">+</span> {book.boardCta}
  </a>;

  return <section className={`wrap ${styles.wall}${full ? " " + styles.wallFull : ""}`} id="wall" aria-labelledby="wall-title" aria-busy={result === null}>
    <header className={styles.heading}>
      {full ? <h1 id="wall-title">{t.title}</h1> : <>
        <p className={styles.kicker}>{t.title}</p>
        <h2 id="wall-title" tabIndex={-1}>{book.boardTitle}</h2>
        <p>{book.boardIntro}</p>
      </>}
      {actions}
    </header>
    <div className={styles.board}>
      {publicEntries.length === 0 && !pinned && <p className={styles.boardMessage} role="status">{result === "error" ? t.error
        : result === null ? t.loading : hasMore || page > 1 ? t.emptyPage : t.empty}</p>}
      <ul className={styles.tracks} data-packed={packed || undefined}>
        <li className={styles.openSpace}>{invite}</li>
        {entries.map((entry, index) => {
          const query = `${entry.song} ${entry.artist}`.replace(/[\uD800-\uDFFF]/gu, "�");
          const { mount, style } = noteStyle(entry.artSeed);
          return <li className={styles.recommendation} key={`${entry.artSeed}-${index}`} data-mount={mount} style={style}>
            <div className={styles.track}>
              {pinned && index === 0 && <p className={styles.personalLabel}>{book.yourSong}</p>}
              <TrackHeading dir="auto">{entry.song}</TrackHeading>
              <p dir="auto">{entry.artist}</p>
              {entry.name && <p className={styles.attribution}><bdi>{entry.name}</bdi></p>}
              <p className={styles.meta}>
                <time dateTime={entry.date}>{date.format(new Date(entry.date))}</time>
                {entry.country && <bdi>{entry.country}</bdi>}
                {entry.status === "added" && <span className={styles.added}>{t.added}</span>}
              </p>
            </div>
            <a className={styles.spotify} href={`https://open.spotify.com/search/${encodeURIComponent(query)}`}
              target="_blank" rel="noreferrer">{t.spotify}</a>
          </li>;
        })}
        {Array.from({ length: result === "error" ? 0 : vacancies }, (_, index) => <li key={index} aria-hidden="true" className={styles.vacantSlot} />)}
      </ul>
      {result === "error" && (onRetry
        ? <button className={styles.boardButton} type="button" onClick={onRetry}>{t.retry}</button>
        : <a className={styles.boardButton} href={retryHref}>{t.retry}</a>)}
    </div>
    {children}
  </section>;
}
