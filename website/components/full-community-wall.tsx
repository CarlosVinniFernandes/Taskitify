import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { copy, type Locale } from "@/lib/copy";
import { communityWallEnabled, communityWallPageSizes, type WallPage } from "@/lib/community-wall";
import { getWallPage } from "@/lib/community-wall-store";
import { pathFor } from "@/lib/site";
import { MusicBoard } from "./music-board";
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
  const numbers = totalPages <= 7
    ? Array.from({ length: totalPages }, (_, index) => index + 1)
    : [...new Set([1, page - 1, page, page + 1, totalPages])].filter((n) => n >= 1 && n <= totalPages)
      .sort((a, b) => a - b);

  return <SiteShell locale={locale} page="wall">
    <MusicBoard locale={locale} result={result} page={page} full retryHref={href(page)} actions={
      <div className={styles.wallActions}>
        <Link className="button button-outline" href={community}>{t.back}</Link>
        <Link className="button button-light" href={`${community}#suggest`}>{t.contribute}</Link>
      </div>
    }>
      {totalPages > 1 && <nav aria-label={t.pagination} className={styles.fullPagination}>
        <ul>
          {page > 1 && <li><Link href={href(page - 1)} rel="prev" prefetch={false}>{t.previous}</Link></li>}
          {numbers.map((number, index) => <li key={number}>
            {index > 0 && number - numbers[index - 1] > 1 && <span className={styles.paginationGap} aria-hidden="true">…</span>}
            {number === page ? <span className={styles.currentPage} aria-current="page" aria-label={`${t.page} ${number}`}>{number}</span>
              : <Link href={href(number)} prefetch={false} aria-label={`${t.page} ${number}`}>{number}</Link>}
          </li>)}
          {page < totalPages && <li><Link href={href(page + 1)} rel="next" prefetch={false}>{t.next}</Link></li>}
        </ul>
        {totalPages > 7 && <form action={base}>
          <label>{t.page} <input name="page" type="number" min={1} max={totalPages} step={1} required defaultValue={page} /></label>
          <button type="submit">{t.go}</button>
        </form>}
      </nav>}
    </MusicBoard>
  </SiteShell>;
}
