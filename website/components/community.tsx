import { copy, type Locale } from "@/lib/copy";
import { pathFor } from "@/lib/site";
import { prefill } from "@/lib/suggestion-fields";
import { communityWallEnabled } from "@/lib/community-wall";
import { PlaylistLink, SiteShell } from "./site";
import styles from "./community-wall.module.css";
import { CommunityContributions } from "./community-contributions";

export type SearchParameters = Promise<Record<string, string | string[] | undefined>>;

export async function Community({ locale, searchParams }: { locale: Locale; searchParams: SearchParameters }) {
  const t = copy[locale].community;
  const initial = prefill(await searchParams);
  return (
    <SiteShell locale={locale} page="community">
      <section className={`wrap ${styles.communityTop}`} aria-labelledby="community-title">
        <div>
          <h1 id="community-title">{t.name}</h1>
          <p>{t.description}</p>
        </div>
        <PlaylistLink locale={locale} />
      </section>
      <CommunityContributions locale={locale} initial={initial} wallEnabled={communityWallEnabled()}
        privacyHref={pathFor(locale, "privacy")} />
    </SiteShell>
  );
}
