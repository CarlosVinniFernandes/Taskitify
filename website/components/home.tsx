import { Suspense } from "react";
import Image from "next/image";
import Link from "next/link";
import { Check, Github, Heart, Music2, ShieldCheck, Star, Grid2X2 } from "lucide-react";
import { copy, type Locale } from "@/lib/copy";
import { pathFor, releases, repository, store } from "@/lib/site";
import { CommunityRecord } from "./community-record";
import { ContactLink, PlaylistLink, RecordArt, SiteShell } from "./site";

export function Home({ locale }: { locale: Locale }) {
  const t = copy[locale];
  const community = pathFor(locale, "community");
  return (
    <SiteShell locale={locale} page="home">
      <section className="hero" aria-labelledby="hero-title">
        <div className="wrap hero-copy">
          <p className="eyebrow"><Grid2X2 size={15} aria-hidden="true" /> {t.hero.platform}</p>
          <h1 id="hero-title">{t.hero.first}<br /><em>{t.hero.second}</em></h1>
          <p className="hero-description">{t.hero.description}</p>
          <div className="actions center">
            <a className="button button-light" href={store}><Grid2X2 size={18} aria-hidden="true" /> {t.hero.store}</a>
            <a className="button button-outline" href={releases}><Github size={18} aria-hidden="true" /> {t.hero.github}</a>
          </div>
        </div>
        <figure className="wrap hero-figure">
          <div className="hero-stage">
            <Image className="hero-browser" src="/media/hero-browser.webp" alt="" width={1140} height={646} priority />
            <Image className="hero-mini" src="/media/mini-wide.webp" alt={t.hero.art} width={1250} height={888} priority />
            <div className="hero-dock"><Image src="/media/hero-bar.webp" alt="" width={1536} height={115} priority /></div>
          </div>
          <figcaption>{t.hero.demo}</figcaption>
        </figure>
        <ul className="wrap promise" aria-label={t.philosophy.title}>
          {[Check, Github, ShieldCheck].map((Icon, i) => <li key={t.promise[i]}><Icon size={16} aria-hidden="true" /> {t.promise[i]}</li>)}
        </ul>
      </section>

      <section className="section wrap" id="features" aria-labelledby="everyday-title">
        <div className="section-heading">
          <h2 id="everyday-title">{t.everyday.title}</h2>
          <p>{t.everyday.description}</p>
        </div>
        <div className="everyday-shots">
          <figure>
            <Image src="/media/browsing.webp" alt={t.everyday.browseAlt} width={1000} height={501} />
            <figcaption>{t.everyday.browse}</figcaption>
          </figure>
          <figure>
            <Image src="/media/working.webp" alt={t.everyday.workAlt} width={1000} height={501} />
            <figcaption>{t.everyday.work}</figcaption>
          </figure>
        </div>
      </section>

      <section className="soundtrack-section" aria-labelledby="soundtrack-title">
        <div className="wrap soundtrack-inner">
          <Suspense fallback={<RecordArt />}>
            <CommunityRecord locale={locale} />
          </Suspense>
          <div className="soundtrack-copy">
            <p className="eyebrow"><Music2 size={16} aria-hidden="true" /> {t.community.name}</p>
            <h2 id="soundtrack-title">{t.community.intro}<br /><em>{t.community.split}</em></h2>
            <p>{t.community.description}</p>
            <div className="actions">
              <PlaylistLink locale={locale} />
              <Link className="button button-light" href={`${community}#suggest`}>{t.community.suggest}</Link>
            </div>
          </div>
        </div>
      </section>

      <section className="section wrap feature-split taskbar-section" aria-labelledby="taskbar-title">
        <div className="feature-copy">
          <p className="eyebrow">{t.taskbar.label}</p>
          <h2 id="taskbar-title">{t.taskbar.title}</h2>
          <p>{t.taskbar.description}</p>
        </div>
        <div className="bar-samples">
          {([["full", 950, 127], ["lyrics", 874, 140], ["compact", 514, 130]] as const).map(([key, width, height], i) => (
            <figure key={key}>
              <Image src={`/media/bar-${key}.webp`} alt={t.taskbar.examples[i]} width={width} height={height} />
              <figcaption>{t.taskbar.examples[i]}</figcaption>
            </figure>
          ))}
        </div>
      </section>

      <section className="player-section" aria-labelledby="player-title">
        <div className="section wrap">
          <div className="section-heading">
            <p className="eyebrow">{t.player.label}</p>
            <h2 id="player-title">{t.player.title}</h2>
            <p>{t.player.description}</p>
          </div>
          <div className="player-shots">
            <figure className="player-wide">
              <Image src="/media/mini-golden.webp" alt={t.player.wideAlt} width={960} height={682} />
              <figcaption>{t.player.wide}</figcaption>
            </figure>
            <figure className="player-vertical">
              <Image src="/media/mini-vertical.webp" alt={t.player.verticalAlt} width={660} height={682} />
              <figcaption>{t.player.vertical}</figcaption>
            </figure>
          </div>
        </div>
      </section>

      <section className="section wrap feature-split minimal-section" aria-labelledby="minimal-title">
        <div className="feature-copy minimal-copy">
          <p className="eyebrow">{t.minimal.label}</p>
          <h2 id="minimal-title">{t.minimal.title}</h2>
          <p>{t.minimal.description}</p>
          <ul className="minimal-points">
            <li><Check size={17} aria-hidden="true" />{t.minimal.move}</li>
            <li><Check size={17} aria-hidden="true" />{t.minimal.clicks}</li>
          </ul>
        </div>
        <figure className="minimal-scene">
          <Image className="minimal-wallpaper" src="/media/fuji.webp" alt="" fill sizes="(max-width: 680px) 100vw, 60vw" />
          <Image className="minimal-lyrics" src="/media/minimal-mode.webp" alt={t.minimal.alt} width={1280} height={400} />
        </figure>
      </section>

      <section className="section wrap feature-split studio-section" aria-labelledby="studio-title">
        <div className="feature-copy">
          <p className="eyebrow">{t.studio.label}</p>
          <h2 id="studio-title">{t.studio.title}</h2>
          <p>{t.studio.description}</p>
        </div>
        <figure className="studio-figure">
          <div className="studio-prints">
            {["cassette", "ds", "paint"].map((key, i) => (
              <Image key={key} src={`/media/card-${key}.webp`} alt={`${t.studio.exported} · ${t.studio.examples[i]}`}
                     width={540} height={960} />
            ))}
          </div>
          <figcaption>{t.studio.exported}</figcaption>
        </figure>
      </section>

      <section className="philosophy-section" aria-labelledby="philosophy-title">
        <div className="wrap section feature-split">
          <div className="feature-copy">
            <Heart className="feature-icon" size={25} aria-hidden="true" />
            <h2 id="philosophy-title">{t.philosophy.title}</h2>
            <p className="philosophy-lead">{t.philosophy.lead}</p>
            <p>{t.philosophy.description}</p>
          </div>
          <ul className="philosophy-list">
            {t.philosophy.points.map((point) => <li key={point}><Check size={17} aria-hidden="true" />{point}</li>)}
          </ul>
        </div>
      </section>

      <section className="section wrap favor" aria-labelledby="favor-title">
        <Star className="feature-icon" size={30} aria-hidden="true" />
        <h2 id="favor-title">{t.favor.title}</h2>
        <p>{t.favor.description}</p>
        <div className="actions center">
          <a className="button button-light" href={repository}><Star size={17} aria-hidden="true" /> {t.favor.star}</a>
          <ContactLink locale={locale} />
        </div>
        <p>{t.favor.thanks}</p>
        <p className="signature">{t.favor.signature}</p>
      </section>
    </SiteShell>
  );
}
