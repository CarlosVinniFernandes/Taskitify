import { shortSharesEnabled } from "@/lib/lyrics-share-experiment";
import { readShortShare, shareError, validShareId } from "@/lib/short-share-api";
import { shortShareStore } from "@/lib/short-share-blob";

export const runtime = "nodejs";
export const maxDuration = 10;

export async function GET(request: Request, context: { params: Promise<{ id: string }> }) {
  const { id } = await context.params;
  if (!shortSharesEnabled() || !validShareId(id)) return shareError(404, "not_found");
  const token = process.env.BLOB_READ_WRITE_TOKEN;
  if (!token) return shareError(503, "unavailable");
  return readShortShare(request, id, shortShareStore(token));
}
