import { get, list } from "@vercel/blob";
import { unstable_cache } from "next/cache";
import { createHash } from "node:crypto";
import { communityWallPageSizes, publicSuggestion, wallEntryTag, wallIndexTag, wallPathPattern, wallPrefix, type GuestbookPage, type GuestSignature, type WallEntry, type WallPage, type WallPageSize } from "./community-wall";

const pageSize = communityWallPageSizes.apiDefault;
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
  // Paths encode the inverse timestamp, so descending path order puts the earliest songs first.
  return paths.sort().reverse();
}, ["community-wall-index-v2"], { tags: [wallIndexTag], revalidate: cacheSeconds });

const readEntry = unstable_cache(async (_store: string, path: string) => {
  const token = process.env.BLOB_READ_WRITE_TOKEN;
  if (!token) throw new Error("Community storage unavailable");
  const result = await get(path, { access: "private", useCache: false, token, abortSignal: AbortSignal.timeout(6000) });
  if (result?.statusCode !== 200 || result.blob.size > 4096) return null;
  return publicSuggestion(await new Response(result.stream).json().catch(() => null));
}, ["community-wall-record-v3"], { tags: [wallEntryTag], revalidate: cacheSeconds });

function storeCacheKey() {
  const token = process.env.BLOB_READ_WRITE_TOKEN;
  if (!token) throw new Error("Community storage unavailable");
  // Keep cached projections separate when deployments use different Blob stores.
  return createHash("sha256").update(token).digest("hex");
}

export async function getWallPage(page: number, size: WallPageSize = pageSize): Promise<WallPage> {
  const store = storeCacheKey();
  const index = await wallIndex(store);
  const start = (page - 1) * size;
  const totalPages = Math.ceil(index.length / size);
  if (start >= index.length) return { entries: [], hasMore: false, totalPages };
  const rows = await Promise.all(index.slice(start, start + size).map((path) => readEntry(store, path)));
  const entries: WallEntry[] = rows.flatMap((row) => {
    if (!row) return [];
    const { artSeed, song, artist, name, date, status } = row;
    return [{ artSeed, song, artist, name, date, status }];
  });
  return { entries, hasMore: page < totalPages, totalPages };
}

export async function getGuestbookPage(offset: number): Promise<GuestbookPage> {
  const store = storeCacheKey();
  const index = await wallIndex(store);
  const signatures: GuestSignature[] = [];
  // At most three batches per request, reusing the wall's cached public projections.
  const end = Math.min(offset + 3 * pageSize, index.length);
  while (offset < end && signatures.length < 10) {
    const batch = index.slice(offset, Math.min(end, offset + pageSize));
    const entries = await Promise.all(batch.map((path) => readEntry(store, path)));
    for (const entry of entries) {
      offset++;
      if (entry?.name) signatures.push({ name: entry.name, date: entry.date, country: entry.country });
      if (signatures.length === 10) break;
    }
  }
  return { names: signatures.map(({ name }) => name), signatures, nextOffset: offset < index.length ? offset : null };
}
