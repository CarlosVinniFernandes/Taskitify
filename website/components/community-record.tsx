import { copy, type Locale } from "@/lib/copy";
import { communityWallEnabled, spotifySearchUrl } from "@/lib/community-wall";
import { getFeaturedWallEntry } from "@/lib/community-wall-store";
import { RecordArt } from "./site";
import { noteStyle } from "./community-note-style";
import styles from "./community-wall.module.css";

export async function CommunityRecord({ locale }: { locale: Locale }) {
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
