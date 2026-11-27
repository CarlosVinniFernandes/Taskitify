"use client";

import { BookOpen, Heart } from "lucide-react";
import Link from "next/link";
import { useEffect, useRef, useState } from "react";
import { copy, type Locale } from "@/lib/copy";
import { guestbookCopy } from "@/lib/guestbook-copy";
import type { Fields } from "@/lib/suggestion-fields";
import { communityWallPageSizes, type GuestbookPage, type WallPage } from "@/lib/community-wall";
import { SongForm } from "./song-form";
import { MusicBoard } from "./music-board";
import { WallPagination } from "./wall-pagination";
import { pathFor } from "@/lib/site";
import styles from "./community-wall.module.css";

type PageResult<T> = T | "error" | null;

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
  const signatures = result && result !== "error" ? result.signatures ?? [] : [];
  const date = new Intl.DateTimeFormat(locale, { dateStyle: "medium", timeZone: "UTC" });
  const heading = useRef<HTMLHeadingElement>(null);
  useEffect(() => { heading.current?.focus({ preventScroll: true }); }, []);

  return <div className={styles.signatures} aria-busy={result === null}>
    <h3 ref={heading} tabIndex={-1}>{t.names}</h3>
    {thanked && <p className={styles.thanks} role="status"><Heart size={15} aria-hidden="true" /> {t.thanks}</p>}
    {signatures.length > 0 ? <ul className={styles.names} aria-label={t.names}>
      {signatures.map((signature, index) => <li key={index}>
        <bdi className={styles.visitorName}>{signature.name}</bdi>
        <span className={styles.signatureDetails}>
          <time dateTime={signature.date}>{date.format(new Date(signature.date))}</time>
          {signature.country && <bdi>{signature.country}</bdi>}
        </span>
      </li>)}
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

const emptyFields = { song: "", artist: "", name: "" };

export function CommunityContributions({ locale, initial, wallEnabled, privacyHref }: {
  locale: Locale; initial: Fields; wallEnabled: boolean; privacyHref: string;
}) {
  const book = guestbookCopy[locale];
  const t = copy[locale].community;
  const [revision, setRevision] = useState(0);
  const [refresh, setRefresh] = useState(0);
  const [page, setPage] = useState(1);
  const wall = usePage<WallPage>(wallEnabled ? `/api/community/wall?page=${page}&size=${communityWallPageSizes.preview}` : null, revision + refresh);
  const [totalPages, setTotalPages] = useState(1);
  useEffect(() => {
    if (wall && wall !== "error") {
      const count = Math.max(1, wall.totalPages);
      setTotalPages(count);
      if (page > count) setPage(count);
    }
  }, [wall, page]);

  function selectPage(selected: number) {
    setPage(selected);
    document.getElementById("wall-title")?.focus();
  }
  const [open, setOpen] = useState<"form" | "names" | "signed">("form");
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
    window.addEventListener("hashchange", openLinkedForm);
    return () => window.removeEventListener("hashchange", openLinkedForm);
  }, []);
  const reloadFirst = () => setRefresh((current) => current + 1);
  function openForm() {
    returning.current = true;
    setOpen("form");
  }

  return <>
    <section className={`wrap ${styles.guestbook}`} id="suggest" aria-labelledby="suggest-title">
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
          {open === "form" ? <>
            <h3 ref={formHeading} tabIndex={-1}>{book.contribution}</h3>
            <SongForm locale={locale} initial={revision ? emptyFields : initial} wallEnabled={wallEnabled}
              onSaved={() => {
                if (!wallEnabled) return;
                setPage(1);
                setRevision(revision + 1);
                setOpen("signed");
              }} />
            {wallEnabled && <button className={styles.paperLink} type="button" onClick={() => setOpen("names")}>
              <BookOpen size={15} aria-hidden="true" /> {book.open}
            </button>}
          </> : <GuestSignatures locale={locale} thanked={open === "signed"} onWrite={openForm} />}
        </div>
      </div>
    </section>
    {wallEnabled && <MusicBoard locale={locale} result={wall} page={page} onRetry={reloadFirst} onWrite={openForm}>
      <WallPagination locale={locale} page={page} totalPages={totalPages} onPage={selectPage} />
      <div className={styles.explore}>
        <Link href={pathFor(locale, "wall")} prefetch={false} className="button button-outline">{t.wall.explore}</Link>
      </div>
    </MusicBoard>}
  </>;
}
