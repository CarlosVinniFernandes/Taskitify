import { shortSharesEnabled } from "@/lib/lyrics-share-experiment";
import { createShortShare, shareError } from "@/lib/short-share-api";
import { shortShareStore } from "@/lib/short-share-blob";
import { siteOrigin } from "@/lib/site";

export const runtime = "nodejs";
export const maxDuration = 10;

export function POST(request: Request) {
  if (!shortSharesEnabled()) return shareError(404, "not_found");
  const token = process.env.BLOB_READ_WRITE_TOKEN;
  const secret = process.env.SHARE_CODE_RATE_SECRET;
  const configuredOrigin = siteOrigin();
  const local = new URL(request.url);
  const origin = configuredOrigin?.origin ?? (process.env.NODE_ENV === "development"
    && ["localhost", "127.0.0.1", "[::1]"].includes(local.hostname) ? local.origin : null);
  if (!token || !secret || !origin) return shareError(503, "unavailable");
  return createShortShare(request, {
    origin,
    address: process.env.VERCEL === "1" ? request.headers.get("x-vercel-forwarded-for")?.trim() ?? "" : "127.0.0.1",
    secret,
    store: shortShareStore(token),
  });
}
