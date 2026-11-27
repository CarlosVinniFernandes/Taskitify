import Link from "next/link";
import type { FormEvent } from "react";
import { copy, type Locale } from "@/lib/copy";
import styles from "./community-wall.module.css";

type Navigation =
  | { href: (page: number) => string; jumpTo: string; onPage?: never }
  | { onPage: (page: number) => void; href?: never; jumpTo?: never };

export function WallPagination({ locale, page, totalPages, href, onPage, jumpTo }: {
  locale: Locale; page: number; totalPages: number;
} & Navigation) {
  const t = copy[locale].community.wall;
  const start = Math.min(Math.max(page - 2, 1), Math.max(totalPages - 4, 1));
  const window = Array.from({ length: Math.min(5, totalPages) }, (_, index) => start + index);
  const numbers = [...new Set([1, ...window, totalPages])];

  function control(number: number, label: string, text: string | number, rel?: "prev" | "next") {
    if (number < 1 || number > totalPages) return <button type="button" aria-label={label} disabled>{text}</button>;
    return href
      ? <Link href={href(number)} prefetch={false} aria-label={label} rel={rel}>{text}</Link>
      : <button type="button" aria-label={label} onClick={() => onPage(number)}>{text}</button>;
  }

  function jump(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const number = Number(new FormData(event.currentTarget).get("page"));
    if (Number.isInteger(number) && number >= 1 && number <= totalPages) onPage?.(number);
  }

  return <nav aria-label={t.pagination} className={styles.fullPagination}>
    <ul>
      <li>{control(page - 1, t.previous, "‹", "prev")}</li>
      {numbers.map((number, index) => <li key={number}>
        {index > 0 && number - numbers[index - 1] > 1 && <span className={styles.paginationGap} aria-hidden="true">…</span>}
        {number === page ? <span className={styles.currentPage} aria-current="page" aria-label={`${t.page} ${number}`}>{number}</span>
          : control(number, `${t.page} ${number}`, number)}
      </li>)}
      <li>{control(page + 1, t.next, "›", "next")}</li>
    </ul>
    {totalPages > 7 && <form action={jumpTo} onSubmit={onPage ? jump : undefined}>
      <label>{t.page} <input key={page} name="page" type="number" min={1} max={totalPages} step={1} required defaultValue={page} /></label>
      <button type="submit">{t.go}</button>
    </form>}
  </nav>;
}
