import type { Metadata } from "next";
import { copy, type Locale } from "./copy";

export const repository = "https://github.com/CarlosVinniFernandes/Taskitify";
export const releases = `${repository}/releases/latest`;
export const store = "https://apps.microsoft.com/detail/9nc2227dj01d";
export const issues = `${repository}/issues/new`;
export const communityPlaylist = "https://open.spotify.com/playlist/0CMy9wXwlq6FRRdrIvvcWD";
export type Page = "home" | "community" | "privacy" | "wall";

export function pathFor(locale: Locale, page: Page = "home") {
  const prefix = locale === "pt-BR" ? "/pt-br" : "";
  const segment = page === "wall" ? "community/wall" : page;
  return (page === "home" ? prefix : `${prefix}/${segment}`) || "/";
}

export function siteOrigin() {
  const value = process.env.SITE_URL || (process.env.VERCEL_PROJECT_PRODUCTION_URL
    ? `https://${process.env.VERCEL_PROJECT_PRODUCTION_URL}` : "");
  try {
    const url = new URL(value);
    return url.protocol === "https:" || url.protocol === "http:" ? new URL(url.origin) : undefined;
  } catch {
    return undefined;
  }
}

export function contactUrl() {
  const address = process.env.CONTACT_EMAIL?.trim();
  return address && /^[^\s@/?#&]+@[^\s@/?#&]+\.[^\s@/?#&]+$/.test(address)
    ? `mailto:${address}?subject=Taskitify`
    : undefined;
}

export function pageMetadata(locale: Locale, page: Page): Metadata {
  const t = copy[locale];
  const origin = siteOrigin();
  const path = pathFor(locale, page);
  const title = page === "wall" ? `${t.community.wall.title} · Taskitify` : page === "home" ? t.meta.title : `${page === "community" ? t.community.name : t.privacy.title} · Taskitify`;
  const description = page === "wall" ? t.community.wall.fullDescription : page === "home" ? t.meta.description : t[page].description;
  const image = { url: "/social.jpg", width: 1200, height: 630, alt: t.hero.art };
  return {
    title,
    description,
    metadataBase: origin,
    alternates: origin ? {
      canonical: path,
      languages: { en: pathFor("en", page), "pt-BR": pathFor("pt-BR", page), "x-default": pathFor("en", page) },
    } : undefined,
    openGraph: {
      type: "website",
      siteName: "Taskitify",
      locale: locale === "en" ? "en_US" : "pt_BR",
      alternateLocale: locale === "en" ? "pt_BR" : "en_US",
      title,
      description,
      url: origin ? new URL(path, origin) : undefined,
      images: origin ? [image] : undefined,
    },
    twitter: { card: "summary_large_image", title, description, images: origin ? [image.url] : undefined },
  };
}
