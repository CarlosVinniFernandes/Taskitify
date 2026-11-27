import Link from "next/link";
import { Music2 } from "lucide-react";
import { copy, type Locale } from "@/lib/copy";
import { pathFor } from "@/lib/site";
import { prefill } from "@/lib/suggestion-fields";
import { communityWallEnabled } from "@/lib/community-wall";
import { PlaylistLink, RecordArt, SiteShell } from "./site";
import { CommunityContributions } from "./community-contributions";

export type SearchParameters = Promise<Record<string, string | string[] | undefined>>;

export async function Community({ locale, searchParams }: { locale: Locale; searchParams: SearchParameters }) {
  const t = copy[locale].community;
  const initial = prefill(await searchParams);
  return (
    <SiteShell locale={locale} page="community">
      <section className="community-intro" aria-labelledby="community-title">
        <div className="wrap soundtrack-inner">
          <RecordArt />
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
      <CommunityContributions locale={locale} initial={initial} wallEnabled={communityWallEnabled()}>
        <div>
          <h2 id="suggest-title">{t.suggest}</h2>
          <p>{t.formIntro}</p>
          <Link className="text-link" href={pathFor(locale, "privacy")}>{t.privacy}</Link>
        </div>
      </CommunityContributions>
    </SiteShell>
  );
}
