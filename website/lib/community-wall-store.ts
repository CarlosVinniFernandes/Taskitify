import { get, list } from "@vercel/blob";
import { unstable_cache } from "next/cache";
import { createHash } from "node:crypto";
import { publicSuggestion, wallEntryTag, wallIndexTag, wallPathPattern, wallPrefix, type GuestbookPage, type WallEntry, type WallPage } from "./community-wall";

const pageSize = 12;
const cacheSeconds = 3600;

const wallIndex = unstable_cache(async (_store: string) => {
  const token = process.env.BLOB_READ_WRITE_TOKEN;
  if (!token) throw new Error("Community storage unavailable");
  const paths: string[] = [];
  let cursor: string | undefined;
  do {
    const result = await list({ prefix: wallPrefix, cursor, limit: 1000, token, abortSignal: AbortSignal.timeout(6000) });
    for (const { pathname } of result.blobs) if (wallPathPattern.test(pathname)) paths.push(pathname);
    cursor = result.hasMore ? result.cursor : undefined;
  } while (cursor);
  return paths.sort();
}, ["community-wall-index-v1"], { tags: [wallIndexTag], revalidate: cacheSeconds });

const readEntry = unstable_cache(async (_store: string, path: string) => {
  const token = process.env.BLOB_READ_WRITE_TOKEN;
  if (!token) throw new Error("Community storage unavailable");
  const result = await get(path, { access: "private", useCache: false, token, abortSignal: AbortSignal.timeout(6000) });
  if (result?.statusCode !== 200 || result.blob.size > 4096) return null;
  return publicSuggestion(await new Response(result.stream).json().catch(() => null));
}, ["community-wall-record-v1"], { tags: [wallEntryTag], revalidate: cacheSeconds });

function storeCacheKey() {
  const token = process.env.BLOB_READ_WRITE_TOKEN;
  if (!token) throw new Error("Community storage unavailable");
  // Keep cached projections separate when deployments use different Blob stores.
  return createHash("sha256").update(token).digest("hex");
}

export async function getWallPage(page: number): Promise<WallPage> {
  const store = storeCacheKey();
  const index = await wallIndex(store);
  const start = (page - 1) * pageSize;
  if (start >= index.length) return { entries: [], hasMore: false };
  const rows = await Promise.all(index.slice(start, start + pageSize).map((path) => readEntry(store, path)));
  return { entries: rows.filter((row): row is WallEntry => row !== null), hasMore: start + pageSize < index.length };
}

export async function getGuestbookPage(offset: number): Promise<GuestbookPage> {
  const store = storeCacheKey();
  const index = await wallIndex(store);
  const names: string[] = [];
  // At most three batches per request, reusing the wall's cached public projections.
  const end = Math.min(offset + 3 * pageSize, index.length);
  while (offset < end && names.length < 10) {
    const batch = index.slice(offset, Math.min(end, offset + pageSize));
    const entries = await Promise.all(batch.map((path) => readEntry(store, path)));
    for (const entry of entries) {
      offset++;
      if (entry?.name) names.push(entry.name);
      if (names.length === 10) break;
    }
  }
  return { names, nextOffset: offset < index.length ? offset : null };
}
