"use client";

import { ArrowUpRight, Music2 } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { decodeShare, shareFailure, spotifySongUrl, type SharedSong } from "@/lib/lyrics-share";
import { shareCopy, shareLocale, type ShareLocale } from "@/lib/lyrics-share-copy";
import { artKind, Composition } from "./composition";
import styles from "./viewer.module.css";

type Screen =
  | { status: "loading" }
  | { status: "error"; locale: ShareLocale; error: ReturnType<typeof shareFailure> | "missing" | "unavailable" }
  | { status: "closed" | "opening" | "open"; locale: ShareLocale; payload: SharedSong };

export function ShareViewer({ shortId }: { shortId?: string }) {
  const [screen, setScreen] = useState<Screen>({ status: "loading" });
  const card = useRef<HTMLElement>(null);
  const focusOnOpen = useRef(false);

  useEffect(() => {
    if (screen.status !== "opening") return;
    const timer = window.setTimeout(() => {
      setScreen((current) => current === screen ? { ...screen, status: "open" } : current);
    }, 490);
    return () => window.clearTimeout(timer);
  }, [screen]);

  useEffect(() => {
    let revision = 0;
    const controller = new AbortController();
    async function readLink() {
      const current = ++revision;
      const browser = navigator.languages?.length ? navigator.languages : [navigator.language];
      focusOnOpen.current = false;
      setScreen({ status: "loading" });
      let code = window.location.hash.slice(1);
      if (shortId !== undefined) {
        let failure: "missing" | "unavailable" = "unavailable";
        try {
          const response = await fetch(`/api/share/${encodeURIComponent(shortId)}`, {
            signal: controller.signal, referrerPolicy: "no-referrer", cache: "no-store",
          });
          if (response.status === 404) failure = "missing";
          if (!response.ok) throw new Error();
          const data = await response.json();
          if (typeof data?.code !== "string") throw new Error();
          code = data.code;
        } catch {
          if (current === revision) setScreen({ status: "error", error: failure, locale: shareLocale(browser) });
          return;
        }
      }
      try {
        const payload = await decodeShare(code);
        if (current === revision) setScreen({
          status: payload.r === "surprise" ? "closed" : "open",
          locale: shareLocale(browser, payload.lang),
          payload,
        });
      } catch (error) {
        if (current === revision) setScreen({
          status: "error", error: shareFailure(error), locale: shareLocale(browser),
        });
      }
    }
    void readLink();
    if (shortId === undefined) window.addEventListener("hashchange", readLink);
    return () => {
      ++revision;
      controller.abort();
      window.removeEventListener("hashchange", readLink);
    };
  }, [shortId]);

  useEffect(() => {
    if (screen.status === "open" && focusOnOpen.current) {
      focusOnOpen.current = false;
      card.current?.focus();
    }
  }, [screen.status]);

  if (screen.status === "loading") return <main className={styles.scene} aria-busy="true" />;
  const { locale } = screen;
  const t = shareCopy[locale];
  const home = locale === "pt-BR" ? "/pt-br" : "/";

  if (screen.status === "error") return <main className={styles.scene} lang={locale}>
    <section className={styles.error}>
      <h1>{screen.error === "unsupported" ? t.invalid : t[screen.error]}</h1>
      <p>{screen.error === "unsupported" ? t.unsupported : screen.error === "unavailable" ? t.tryAgain : t.help}</p>
      <a className={styles.about} href={home}>{t.about} <ArrowUpRight size={15} aria-hidden="true" /></a>
    </section>
  </main>;

  if (screen.status === "closed" || screen.status === "opening") return <main className={styles.scene} lang={locale}>
    <section className={styles.invitation} data-opening={screen.status === "opening" || undefined}>
      <div className={styles.envelope} aria-hidden="true">
        <i className={styles.envelopeBack} />
        <i className={styles.envelopePaper} />
        <i className={styles.envelopeFront} />
        <i className={styles.envelopeFlap} />
        <span className={styles.seal}>♪</span>
      </div>
      <h1>{t.sent}</h1>
      <p>{t.feelings}</p>
      <button className={styles.open} aria-disabled={screen.status === "opening"} onClick={() => {
        if (screen.status === "opening") return;
        focusOnOpen.current = true;
        const reduced = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
        setScreen({ ...screen, status: reduced ? "open" : "opening" });
      }}>{t.open}<span aria-hidden="true">→</span></button>
    </section>
  </main>;

  const { payload } = screen;
  return <main className={styles.scene} data-desk={artKind(payload.t)} lang={locale}>
    <article ref={card} tabIndex={-1} className={styles.revealed} data-unwrapped={payload.r === "surprise" || undefined}>
      <h1 className={styles.visuallyHidden} lang={payload.lang}><bdi>{payload.s.t}</bdi> · <bdi>{payload.s.a}</bdi></h1>
      <Composition payload={payload} locale={locale} />
      <a className={styles.listen} href={spotifySongUrl(payload.s)} target="_blank" rel="noreferrer">
        <Music2 size={18} aria-hidden="true" /> {payload.s.sp ? t.listen : t.search} <ArrowUpRight size={15} aria-hidden="true" />
      </a>
      <footer className={styles.credit}>
        <p>{t.made}</p>
        <a className={styles.about} href={home}>{t.about} <ArrowUpRight size={15} aria-hidden="true" /></a>
      </footer>
    </article>
  </main>;
}
