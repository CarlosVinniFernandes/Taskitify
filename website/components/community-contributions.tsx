"use client";

import { ArrowUpRight, BookOpen, Heart } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { copy, type Locale } from "@/lib/copy";
import { guestbookCopy } from "@/lib/guestbook-copy";
import type { Fields } from "@/lib/suggestion-fields";
import type { GuestbookPage, WallPage } from "@/lib/community-wall";
import { SongForm } from "./song-form";
import styles from "./community-wall.module.css";

type PageResult<T> = T | "error" | null;
const firstWallPage = "/api/community/wall?page=1";

function usePage<T>(url: string | null, revision: number) {
  const [data, setData] = useState<{ url: string; revision: number; result: T | "error" } | null>(null);
  useEffect(() => {
    if (!url) return;
    const target = url;
    const controller = new AbortController();
    async function load() {
      try {
        const response = await fetch(target, { signal: controller.signal, cache: "no-store" });
        if (!response.ok) throw new Error();
        const result: T = await response.json();
        if (!controller.signal.aborted) setData({ url: target, revision, result });
      } catch {
        if (!controller.signal.aborted) setData({ url: target, revision, result: "error" });
      }
    }
    void load();
    return () => controller.abort();
  }, [url, revision]);
  return data?.url === url && data.revision === revision ? data.result : null;
}

function GuestSignatures({ locale, thanked, onWrite }: {
  locale: Locale; thanked: boolean; onWrite: () => void;
}) {
  const t = guestbookCopy[locale];
  const retry = copy[locale].community.wall.retry;
  const [offsets, setOffsets] = useState([0]);
  const [revision, setRevision] = useState(0);
  const result = usePage<GuestbookPage>(`/api/community/guestbook?offset=${offsets.at(-1)}`, revision);
  const next = result && result !== "error" ? result.nextOffset : null;
  const names = result && result !== "error" ? result.names : [];
  const heading = useRef<HTMLHeadingElement>(null);
  useEffect(() => { heading.current?.focus({ preventScroll: true }); }, []);

  return <div className={styles.signatures} aria-busy={result === null}>
    <h3 ref={heading} tabIndex={-1}>{t.names}</h3>
    {thanked && <p className={styles.thanks} role="status"><Heart size={15} aria-hidden="true" /> {t.thanks}</p>}
    {names.length > 0 ? <ul className={styles.names} aria-label={t.names}>
      {names.map((name, index) => <li key={index}><bdi>{name}</bdi></li>)}
    </ul> : <p className={styles.bookMessage} role="status">{result === "error" ? t.error
      : result === null ? t.loading : next !== null || offsets.length > 1 ? t.emptyPage : t.empty}</p>}
    {result === "error" && <button className={styles.paperLink} type="button" onClick={() => setRevision(revision + 1)}>{retry}</button>}
    {(offsets.length > 1 || next !== null) && <nav className={styles.bookNav} aria-label={t.pages}>
      <button className={styles.paperLink} type="button" disabled={offsets.length === 1 || result === null}
        onClick={() => setOffsets(offsets.slice(0, -1))}>{t.previous}</button>
      <button className={styles.paperLink} type="button" disabled={next === null}
        onClick={() => { if (next !== null) setOffsets([...offsets, next]); }}>{t.next}</button>
    </nav>}
    <button className={styles.paperLink} type="button" onClick={onWrite}>{t.write}</button>
  </div>;
}

function MusicBoard({ locale, first, reloadFirst, onWrite }: {
  locale: Locale; first: PageResult<WallPage>; reloadFirst: () => void; onWrite: () => void;
}) {
  const t = copy[locale].community.wall;
  const book = guestbookCopy[locale];
  const [page, setPage] = useState(1);
  const [retry, setRetry] = useState(0);
  const older = usePage<WallPage>(page === 1 ? null : `/api/community/wall?page=${page}`, retry);
  const result = page === 1 ? first : older;
  const date = new Intl.DateTimeFormat(locale, { dateStyle: "medium", timeZone: "UTC" });
  const entries = result && result !== "error" ? result.entries : [];
  const hasMore = result !== null && result !== "error" && result.hasMore;

  return <section className={`wrap ${styles.wall}`} id="wall" aria-labelledby="wall-title" aria-busy={result === null}>
    <header className={styles.heading}>
      <p className={styles.kicker}>{t.title}</p>
      <h2 id="wall-title">{book.boardTitle}</h2>
      <p>{book.boardIntro}</p>
    </header>
    <div className={styles.board}>
      {entries.length > 0 ? <ul className={styles.tracks}>
        {entries.map((entry, index) => {
          const query = `${entry.song} ${entry.artist}`.replace(/[\uD800-\uDFFF]/gu, "�");
          return <li className={styles.polaroid} key={`${entry.date}-${entry.song}-${index}`}>
            <div className={styles.cover} aria-hidden="true"><span className={styles.record} /></div>
            <div className={styles.track}>
              <h3 dir="auto">{entry.song}</h3>
              <p dir="auto">{entry.artist}</p>
              {entry.name && <p className={styles.attribution}>— <bdi>{entry.name}</bdi></p>}
              <p className={styles.meta}>
                <time dateTime={entry.date}>{date.format(new Date(entry.date))}</time>
                <span aria-hidden="true">·</span>
                <span className={entry.status === "added" ? styles.added : undefined}>{t[entry.status]}</span>
              </p>
            </div>
            <a className={styles.spotify} href={`https://open.spotify.com/search/${encodeURIComponent(query)}`}
              target="_blank" rel="noreferrer">{t.spotify} <ArrowUpRight size={14} aria-hidden="true" /></a>
          </li>;
        })}
      </ul> : <p className={styles.boardMessage} role="status">{result === "error" ? t.error
        : result === null ? t.loading : hasMore || page > 1 ? t.emptyPage : t.empty}</p>}
      {result === "error" && <button className={styles.boardButton} type="button"
        onClick={page === 1 ? reloadFirst : () => setRetry(retry + 1)}>{t.retry}</button>}
      <div className={styles.boardFooter}>
        <a href="#suggest" className={styles.pinCta} onClick={onWrite}>+ {book.boardCta}</a>
      </div>
    </div>
    {(page > 1 || hasMore) && <nav aria-label={t.pagination} className={styles.wallNav}>
      <button type="button" disabled={page === 1 || result === null} onClick={() => setPage(page - 1)}>{t.previous}</button>
      <button type="button" disabled={!hasMore} onClick={() => setPage(page + 1)}>{t.next}</button>
    </nav>}
  </section>;
}

const emptyFields = { song: "", artist: "", name: "" };

export function CommunityContributions({ locale, initial, wallEnabled, privacyHref }: {
  locale: Locale; initial: Fields; wallEnabled: boolean; privacyHref: string;
}) {
  const book = guestbookCopy[locale];
  const t = copy[locale].community;
  const [revision, setRevision] = useState(0);
  const [refresh, setRefresh] = useState(0);
  const first = usePage<WallPage>(wallEnabled ? firstWallPage : null, revision + refresh);
  const [open, setOpen] = useState<"recent" | "form" | "names" | "signed">(
    !wallEnabled || Object.values(initial).some(Boolean) ? "form" : "recent"
  );
  const formHeading = useRef<HTMLHeadingElement>(null);
  const returning = useRef(false);
  useEffect(() => {
    if (open === "form" && returning.current) {
      returning.current = false;
      formHeading.current?.focus();
    }
  }, [open]);
  useEffect(() => {
    function openLinkedForm() {
      if (window.location.hash === "#suggest") setOpen("form");
    }
    openLinkedForm();
    window.addEventListener("hashchange", openLinkedForm);
    return () => window.removeEventListener("hashchange", openLinkedForm);
  }, []);
  const reloadFirst = () => setRefresh((current) => current + 1);
  const latest = first && first !== "error" ? first.entries.slice(0, 5) : [];

  function openForm() {
    returning.current = true;
    setOpen("form");
  }
  return <>
    <section className={`wrap ${styles.guestbook}`} id="suggest" aria-labelledby="suggest-title">
      <div className={styles.desk}>
        <div className={styles.book}>
          <div className={`${styles.paper} ${styles.leftPage}`}>
            <header className={styles.letterHeader}>
              <p className={styles.noteCaption}>{book.label}</p>
              <h2 id="suggest-title">{book.title}</h2>
            </header>
            <p>{book.letter}</p>
            <p>{book.invitation}</p>
            <p className={styles.signature}>{book.signature} <span>Carlos</span></p>
            <div className={styles.letterActions}>
              {open !== "form" && <button className={styles.writeButton} type="button" onClick={openForm}>{book.write}</button>}
              <a className={styles.paperLink} href={privacyHref}>{t.privacy}</a>
            </div>
          </div>
          <div className={`${styles.paper} ${styles.rightPage}`}>
            {open === "recent" ? <div className={styles.recent} aria-busy={first === null}>
              <h3>{book.recent}</h3>
              {latest.length > 0 ? <ul className={styles.notes}>
                {latest.map((entry, index) => <li key={index}>
                  <strong dir="auto">{entry.song}</strong>
                  <span dir="auto">{entry.artist}{entry.name && <> · {book.by} <bdi>{entry.name}</bdi></>}</span>
                </li>)}
              </ul> : <p className={styles.bookMessage} role="status">{first === null ? t.wall.loading
                : first === "error" ? t.wall.error : book.noSongs}</p>}
              {first === "error" && <button className={styles.paperLink} type="button" onClick={reloadFirst}>{t.wall.retry}</button>}
              <button className={styles.paperLink} type="button" onClick={() => setOpen("names")}>
                <BookOpen size={15} aria-hidden="true" /> {book.open}
              </button>
            </div> : open === "form" ? <>
              <h3 ref={formHeading} tabIndex={-1}>{book.contribution}</h3>
              <SongForm locale={locale} initial={revision ? emptyFields : initial} wallEnabled={wallEnabled}
                onSaved={() => {
                  if (!wallEnabled) return;
                  setRevision(revision + 1);
                  setOpen("signed");
                }} />
              {wallEnabled && <button className={styles.paperLink} type="button" onClick={() => setOpen("names")}>
                <BookOpen size={15} aria-hidden="true" /> {book.open}
              </button>}
            </> : <GuestSignatures locale={locale} thanked={open === "signed"} onWrite={openForm} />}
          </div>
        </div>
        <i className={styles.pencil} aria-hidden="true" />
      </div>
    </section>
    {wallEnabled && <MusicBoard key={revision} locale={locale} first={first} reloadFirst={reloadFirst} onWrite={openForm} />}
  </>;
}
