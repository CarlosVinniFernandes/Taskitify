"use client";

import { ArrowUpRight, Music2 } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { decodeShare, shareFailure, spotifySongUrl, type SharedSong } from "@/lib/lyrics-share";
import { shareCopy, shareLocale, type ShareLocale } from "@/lib/lyrics-share-copy";
import { Composition } from "./composition";
import styles from "./viewer.module.css";

type Screen =
  | { status: "loading" }
  | { status: "error"; locale: ShareLocale; error: ReturnType<typeof shareFailure> }
  | { status: "closed" | "open"; locale: ShareLocale; payload: SharedSong };

export function ShareViewer() {
  const [screen, setScreen] = useState<Screen>({ status: "loading" });
  const card = useRef<HTMLElement>(null);
  const focusOnOpen = useRef(false);

  useEffect(() => {
    let revision = 0;
    async function readLink() {
      const current = ++revision;
      const browser = navigator.languages?.length ? navigator.languages : [navigator.language];
      setScreen({ status: "loading" });
      try {
        const payload = await decodeShare(window.location.hash.slice(1));
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
    window.addEventListener("hashchange", readLink);
    return () => { ++revision; window.removeEventListener("hashchange", readLink); };
  }, []);

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
      <h1>{screen.error === "version" ? t.version : t.invalid}</h1>
      <p>{screen.error === "unsupported" ? t.unsupported : t.help}</p>
      <a className={styles.about} href={home}>{t.about} <ArrowUpRight size={15} aria-hidden="true" /></a>
    </section>
  </main>;

  if (screen.status === "closed") return <main className={styles.scene} lang={locale}>
    <section className={styles.invitation}>
      <div className={styles.envelope} aria-hidden="true">
        <i className={styles.envelopePaper} />
        <i className={styles.envelopeFront} />
        <span className={styles.seal}><Music2 size={23} strokeWidth={1.5} /></span>
      </div>
      <h1>{t.sent}</h1>
      <p>{t.feelings}</p>
      <button className={styles.open} onClick={() => {
        focusOnOpen.current = true;
        setScreen({ ...screen, status: "open" });
      }}>{t.open}<span aria-hidden="true">↗</span></button>
    </section>
  </main>;

  const { payload } = screen;
  const dedication = payload.d;
  const hasDedication = dedication && (dedication.to || dedication.msg || dedication.from);
  return <main className={styles.scene} lang={locale}>
    <article ref={card} tabIndex={-1} className={styles.revealed}>
      <h1 className={styles.visuallyHidden}><bdi>{payload.s.t}</bdi> · <bdi>{payload.s.a}</bdi></h1>
      {hasDedication && <section className={styles.note} aria-label={t.dedication}>
        {dedication.to && <p className={styles.address}>{t.to} <bdi lang={payload.lang}>{dedication.to}</bdi></p>}
        {dedication.msg && <p className={styles.message} lang={payload.lang} dir="auto">{dedication.msg}</p>}
        {dedication.from && <p className={styles.signature}>{t.from} <bdi lang={payload.lang}>{dedication.from}</bdi></p>}
      </section>}
      <Composition payload={payload} />
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
