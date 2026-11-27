import type { ReactNode } from "react";
import Image from "next/image";
import Link from "next/link";
import { Github, Headphones, Mail, Music2, Grid2X2 } from "lucide-react";
import { copy, type Locale } from "@/lib/copy";
import { contactUrl, issues, pathFor, playlistUrl, repository, store, type Page } from "@/lib/site";

export function SiteShell({ locale, page, children }: { locale: Locale; page: Page; children: ReactNode }) {
  const t = copy[locale];
  const home = pathFor(locale);
  const alternate = locale === "en" ? "pt-BR" : "en";
  const contact = contactUrl();

  return <>
    <a className="skip-link" href="#main">{t.nav.skip}</a>
    <header className="site-header">
      <div className="wrap header-inner">
        <Link className="wordmark" href={home} aria-label="Taskitify">
          <Image src="/media/logo.webp" alt="" width={36} height={36} priority />
          <span>Taskitify</span>
        </Link>
        <nav className="header-nav" aria-label={t.nav.navigation}>
          <Link href={home} aria-current={page === "home" ? "page" : undefined}>{t.nav.home}</Link>
          <Link href={`${home}#features`}>{t.nav.features}</Link>
          <Link href={pathFor(locale, "community")} aria-current={page === "community" ? "page" : undefined}>{t.nav.community}</Link>
          <a href={repository}><Github size={16} aria-hidden="true" /> {t.nav.github}</a>
        </nav>
        <div className="header-actions">
          <Link className="locale-link" href={pathFor(alternate, page)} hrefLang={alternate} lang={alternate}
                aria-label={alternate === "en" ? "Read in English" : "Ler em português"}>
            {alternate === "en" ? "EN" : "PT"}
          </Link>
          <a className="button button-small button-light" href={store}><Grid2X2 size={16} aria-hidden="true" /> {t.nav.download}</a>
        </div>
      </div>
    </header>
    <main id="main" tabIndex={-1}>{children}</main>
    <footer className="site-footer">
      <div className="wrap footer-inner">
        <div>
          <Link className="wordmark" href={home}>
            <Image src="/media/logo.webp" alt="" width={28} height={28} />
            <span>Taskitify</span>
          </Link>
          <p>{t.footer.made}</p>
        </div>
        <nav aria-label={t.footer.legal}>
          <Link href={pathFor(locale, "privacy")}>{t.footer.privacy}</Link>
          <a href={contact || issues}>{contact ? t.footer.contact : t.favor.githubContact}</a>
          <a href={`${repository}/blob/main/LICENSE`}>{t.footer.legal}</a>
          <Link href={pathFor(alternate, page)} hrefLang={alternate} lang={alternate}>{alternate === "en" ? "English" : "Português"}</Link>
        </nav>
      </div>
    </footer>
  </>;
}

export function ContactLink({ locale }: { locale: Locale }) {
  const t = copy[locale].favor;
  const url = contactUrl();
  return <a className="button button-outline" href={url || issues}>
    {url ? <Mail size={17} aria-hidden="true" /> : <Github size={17} aria-hidden="true" />}
    {url ? t.contact : t.githubContact}
  </a>;
}

export function PlaylistLink({ locale }: { locale: Locale }) {
  const t = copy[locale].community;
  const url = playlistUrl();
  return url ? (
    <a className="button button-spotify" href={url}><Headphones size={18} aria-hidden="true" /> {t.listen}</a>
  ) : (
    <button className="button button-outline" type="button" disabled aria-describedby="playlist-status">
      <Headphones size={18} aria-hidden="true" /> {t.listen}
    </button>
  );
}

export function RecordArt() {
  return <div className="record-art" aria-hidden="true">
    <div className="vinyl" />
    <div className="record-sleeve">
      <Image src="/media/cover.webp" alt="" fill sizes="(max-width: 760px) 176px, 280px" />
      <Music2 size={38} strokeWidth={1.4} />
      <div className="sleeve-title"><span>Taskitify</span><strong>Community<br />Soundtrack</strong></div>
    </div>
  </div>;
}
