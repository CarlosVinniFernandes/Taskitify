import { get, list, put } from "@vercel/blob";
import { limits } from "./suggestion-fields";
import { publicSuggestion, wallPath, wallPathPattern, wallPrefix } from "./community-wall";
import type { Suggestion } from "./suggestions";

export const adminStatuses = ["suggested", "added", "hidden"] as const;
export type AdminStatus = typeof adminStatuses[number];

function token() {
  const value = process.env.BLOB_READ_WRITE_TOKEN;
  if (!value) throw new Error("Community storage unavailable");
  return value;
}

async function readRecord(path: string, blobToken: string) {
  if (!wallPathPattern.test(path)) return null;
  const blob = await get(path, { access: "private", useCache: false, token: blobToken, abortSignal: AbortSignal.timeout(6000) });
  if (blob?.statusCode !== 200 || !blob.blob.etag || blob.blob.size > 4096) return null;
  const record: Suggestion = await new Response(blob.stream).json();
  if (!record || !["suggested", "added", "hidden"].includes(record.status)
      || !publicSuggestion({ ...record, status: "suggested" }) || wallPath(record) !== path) return null;
  return { record, etag: blob.blob.etag };
}

export async function getAdminWall(cursor?: string) {
  const blobToken = token();
  const page = await list({ prefix: wallPrefix, limit: 12, cursor, token: blobToken, abortSignal: AbortSignal.timeout(6000) });
  const entries = await Promise.all(page.blobs.map(async ({ pathname }) => {
    const entry = await readRecord(pathname, blobToken);
    if (!entry) return null;
    const { record, etag } = entry;
    return { pathname, etag, song: record.song, artist: record.artist, status: record.status as AdminStatus };
  }));
  return { entries: entries.filter((entry) => entry !== null), next: page.hasMore ? page.cursor : undefined };
}

function title(value: FormDataEntryValue | null, max: number) {
  if (typeof value !== "string" || value.length > max || /[\p{Cc}\u202a-\u202e\u2066-\u2069]/u.test(value)) return null;
  const normalized = value.normalize("NFC").trim();
  return normalized && normalized.length <= max ? normalized : null;
}

export async function updateAdminWall(data: FormData) {
  const path = data.get("path");
  const etag = data.get("etag");
  const song = title(data.get("song"), limits.song);
  const artist = title(data.get("artist"), limits.artist);
  const status = adminStatuses.find((value) => value === data.get("status"));
  if (typeof path !== "string" || !wallPathPattern.test(path) || typeof etag !== "string" || !song || !artist || !status) {
    return "invalid";
  }
  const blobToken = token();
  const entry = await readRecord(path, blobToken);
  if (!entry || entry.etag !== etag) return "changed";
  const { record } = entry;
  if (song !== record.song || artist !== record.artist || status !== record.status) {
    await put(path, JSON.stringify({ ...record, song, artist, status }), {
      access: "private", token: blobToken, contentType: "application/json",
      addRandomSuffix: false, allowOverwrite: true, ifMatch: entry.etag, abortSignal: AbortSignal.timeout(6000),
    });
  }
  return "saved";
}
