import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { copy, type Locale } from "@/lib/copy";
import { communityWallEnabled, communityWallPageSizes, type WallPage } from "@/lib/community-wall";
import { getWallPage } from "@/lib/community-wall-store";
import { pathFor } from "@/lib/site";
import { MusicBoard } from "./music-board";
import { WallPagination } from "./wall-pagination";
import { SiteShell } from "./site";
import styles from "./community-wall.module.css";

type WallSearch = Promise<{ page?: string | string[] }>;

export async function FullCommunityWall({ locale, searchParams }: { locale: Locale; searchParams: WallSearch }) {
  if (!communityWallEnabled()) notFound();
  const t = copy[locale].community.wall;
  const community = pathFor(locale, "community");
  const base = pathFor(locale, "wall");
  const href = (number: number) => number === 1 ? base : `${base}?page=${number}`;
  const requested = (await searchParams).page;
  const page = requested === undefined ? 1 : Number(requested);
  if (requested !== undefined && (typeof requested !== "string" || !/^[1-9]\d*$/.test(requested) || !Number.isSafeInteger(page))) {
    redirect(base);
  }
  let result: WallPage | "error";
  try {
    result = await getWallPage(page, communityWallPageSizes.full);
  } catch {
    result = "error";
  }
  const totalPages = result === "error" ? 0 : result.totalPages;
  if (result !== "error" && page > Math.max(1, totalPages)) redirect(href(Math.max(1, totalPages)));
  return <SiteShell locale={locale} page="wall">
    <MusicBoard locale={locale} result={result} page={page} full retryHref={href(page)} actions={
      <div className={styles.wallActions}>
        <Link className="button button-outline" href={community}>{t.back}</Link>
        <Link className="button button-light" href={`${community}#suggest`}>{t.contribute}</Link>
      </div>
    }>
      <WallPagination locale={locale} page={page} totalPages={Math.max(1, totalPages)} href={href} jumpTo={base} />
    </MusicBoard>
  </SiteShell>;
}
