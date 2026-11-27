import { Suspense } from "react";
import { Music2 } from "lucide-react";
import { copy, type Locale } from "@/lib/copy";
import { pathFor } from "@/lib/site";
import { prefill } from "@/lib/suggestion-fields";
import { communityWallEnabled, spotifySearchUrl } from "@/lib/community-wall";
import { getFeaturedWallEntry } from "@/lib/community-wall-store";
import { PlaylistLink, RecordArt, SiteShell } from "./site";
import { CommunityContributions } from "./community-contributions";
import { noteStyle } from "./community-note-style";
import styles from "./community-wall.module.css";

async function CommunityRecord({ locale }: { locale: Locale }) {
  const song = communityWallEnabled() ? await getFeaturedWallEntry().catch(() => null) : null;
  if (!song) return <RecordArt />;

  const t = copy[locale].community;
  const { style } = noteStyle(song.artSeed);
  const [beforeName, afterName] = t.featuredBy.split("{name}");
  return <div className="community-record">
    <RecordArt />
    <aside className={`community-featured ${styles.recommendation} ${styles.featuredNote}`} data-mount="1" style={style} aria-label={t.featuredTitle}>
      <p className="community-featured-label">{t.featuredTitle}</p>
      <p className="community-featured-song" dir="auto">{song.song}</p>
      <p className="community-featured-artist" dir="auto">{song.artist}</p>
      {song.name && <p className="community-featured-by">{beforeName}<bdi>{song.name}</bdi>{afterName}</p>}
      <a className={styles.spotify} href={spotifySearchUrl(song)}>{t.wall.spotify}</a>
    </aside>
  </div>;
}

export type SearchParameters = Promise<Record<string, string | string[] | undefined>>;

export async function Community({ locale, searchParams }: { locale: Locale; searchParams: SearchParameters }) {
  const t = copy[locale].community;
  const initial = prefill(await searchParams);
  return (
    <SiteShell locale={locale} page="community">
      <section className="community-intro" aria-labelledby="community-title">
        <div className="wrap soundtrack-inner">
          <Suspense fallback={<RecordArt />}>
            <CommunityRecord locale={locale} />
          </Suspense>
          <div className="soundtrack-copy">
            <p className="eyebrow"><Music2 size={16} aria-hidden="true" /> {t.name}</p>
            <h1 id="community-title">{t.intro}<br /><em>{t.split}</em></h1>
            <p>{t.description}</p>
            <div className="actions">
              <PlaylistLink locale={locale} />
              <a className="button button-light" href="#suggest">{t.suggest}</a>
            </div>
          </div>
        </div>
      </section>
      <CommunityContributions locale={locale} initial={initial} wallEnabled={communityWallEnabled()}
        privacyHref={pathFor(locale, "privacy")} />
    </SiteShell>
  );
}
