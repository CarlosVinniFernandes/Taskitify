"use client";

import { ArrowUpRight } from "lucide-react";
import { useEffect, useState, type ReactNode } from "react";
import { copy, type Locale } from "@/lib/copy";
import type { Fields } from "@/lib/suggestion-fields";
import type { WallPage } from "@/lib/community-wall";
import { SongForm } from "./song-form";
import styles from "./community-wall.module.css";

export function CommunityContributions({ children, locale, initial, wallEnabled }: {
  children: ReactNode; locale: Locale; initial: Fields; wallEnabled: boolean;
}) {
  const t = copy[locale].community.wall;
  const [{ page, revision }, setView] = useState({ page: 1, revision: 0 });
  const [result, setResult] = useState<WallPage | "error" | null>(null);

  useEffect(() => {
    if (!wallEnabled) return;
    const controller = new AbortController();
    setResult(null);
    async function load() {
      try {
        const response = await fetch(`/api/community/wall?page=${page}`, { signal: controller.signal, cache: "no-store" });
        if (!response.ok) throw new Error();
        const data: WallPage = await response.json();
        if (!controller.signal.aborted) setResult(data);
      } catch {
        if (!controller.signal.aborted) setResult("error");
      }
    }
    void load();
    return () => controller.abort();
  }, [wallEnabled, page, revision]);

  const date = new Intl.DateTimeFormat(locale, { dateStyle: "medium", timeZone: "UTC" });
  const entries = result && result !== "error" ? result.entries : [];
  const hasMore = result !== null && result !== "error" && result.hasMore;
  return <>
    <section className="wrap section suggestion-section" id="suggest" aria-labelledby="suggest-title">
      {children}
      <SongForm locale={locale} initial={initial} wallEnabled={wallEnabled}
        onSaved={() => setView(({ revision }) => ({ page: 1, revision: revision + 1 }))} />
    </section>
    {wallEnabled && <section className={`wrap ${styles.wall}`} id="wall" aria-labelledby="wall-title" aria-busy={result === null}>
      <header><h2 id="wall-title">{t.title}</h2><p>{t.intro}</p></header>
      {entries.length > 0 ? <ul className={styles.tracks}>
        {entries.map((entry, index) => {
          const query = `${entry.song} ${entry.artist}`.replace(/[\uD800-\uDFFF]/gu, "�");
          return <li key={`${entry.date}-${entry.song}-${index}`}>
            <div className={styles.track}>
              <h3 dir="auto">{entry.song}</h3>
              <p dir="auto">{entry.artist}</p>
              <p className={styles.meta}>
                {entry.name && <><bdi>{entry.name}</bdi><span aria-hidden="true">·</span></>}
                <time dateTime={entry.date}>{date.format(new Date(entry.date))}</time>
                <span aria-hidden="true">·</span>
                <span className={entry.status === "added" ? styles.added : undefined}>{t[entry.status]}</span>
              </p>
            </div>
            <a className={styles.spotify} href={`https://open.spotify.com/search/${encodeURIComponent(query)}`}
              target="_blank" rel="noreferrer">{t.spotify} <ArrowUpRight size={16} aria-hidden="true" /></a>
          </li>;
        })}
      </ul> : <p className={styles.message} role="status">{result === "error" ? t.error
        : result === null ? t.loading : hasMore || page > 1 ? t.emptyPage : t.empty}</p>}
      {result === "error" && <button className={styles.pageButton} type="button"
        onClick={() => setView({ page, revision: revision + 1 })}>{t.retry}</button>}
      {(page > 1 || hasMore) && <nav aria-label={t.pagination} className={styles.pages}>
        <button className={styles.pageButton} type="button" disabled={page === 1 || result === null}
          onClick={() => setView({ page: page - 1, revision })}>{t.previous}</button>
        <button className={styles.pageButton} type="button" disabled={!hasMore}
          onClick={() => setView({ page: page + 1, revision })}>{t.next}</button>
      </nav>}
    </section>}
  </>;
}
