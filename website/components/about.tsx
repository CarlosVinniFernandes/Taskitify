import Link from "next/link";
import { copy, type Locale } from "@/lib/copy";
import { pathFor } from "@/lib/site";
import { ContactLink, SiteShell } from "./site";

export function About({ locale }: { locale: Locale }) {
  const t = copy[locale].about;
  const spotifyLocale = locale === "pt-BR" ? "br-pt" : "us";
  const lyricsSource = `https://support.spotify.com/${spotifyLocale}/artists/article/managing-your-lyrics-on-spotify/`;

  return <SiteShell locale={locale} page="about">
    <article className="wrap privacy-page about-page">
      <header>
        <hgroup>
          <p>{t.title}</p>
          <h1>{t.heading}</h1>
        </hgroup>
        <p className="about-letter">{t.letter}</p>
        <p>{t.signature}</p>
      </header>
      <section>
        <h2>{t.independence.title}</h2>
        <p>{t.independence.text}</p>
      </section>
      <section>
        <h2>{t.lyrics.title}</h2>
        <p>{t.lyrics.text}</p>
        <p><a href={lyricsSource}>{t.lyrics.source}</a></p>
      </section>
      <section>
        <h2>{t.privacy.title}</h2>
        <p>{t.privacy.text}</p>
        <p><Link href={pathFor(locale, "privacy")}>{t.privacy.link}</Link></p>
      </section>
      <section>
        <h2>{t.contact.title}</h2>
        <p>{t.contact.text}</p>
        <ContactLink locale={locale} />
      </section>
    </article>
  </SiteShell>;
}
