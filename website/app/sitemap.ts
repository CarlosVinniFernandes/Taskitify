import type { MetadataRoute } from "next";
import { pathFor, siteOrigin, type Page } from "@/lib/site";
import { communityWallEnabled } from "@/lib/community-wall";
import type { Locale } from "@/lib/copy";

export default function sitemap(): MetadataRoute.Sitemap {
  const origin = siteOrigin();
  if (!origin) return [];
  const locales: Locale[] = ["en", "pt-BR"];
  const pages: Page[] = ["home", "community", "privacy"];
  if (communityWallEnabled()) pages.push("wall");
  return pages.flatMap((page) => locales.map((locale) => ({
    url: new URL(pathFor(locale, page), origin).href,
    alternates: {
      languages: Object.fromEntries(locales.map((language) => [language, new URL(pathFor(language, page), origin).href])),
    },
  })));
}
