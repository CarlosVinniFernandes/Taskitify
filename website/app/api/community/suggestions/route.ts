import { BlobError, BlobNotFoundError, BlobServiceRateLimited, head, put } from "@vercel/blob";
import { handleSuggestion } from "@/lib/suggestions";

export const runtime = "nodejs";
export const maxDuration = 10;

export async function POST(request: Request) {
  const token = process.env.BLOB_READ_WRITE_TOKEN;
  return handleSuggestion(request, {
    address: process.env.VERCEL === "1" ? request.headers.get("x-vercel-forwarded-for") : "127.0.0.1",
    secret: process.env.SUGGESTION_RATE_SECRET,
    async create(path, suggestion) {
      if (!token) throw new Error("Private Blob storage is not configured");
      const abortSignal = AbortSignal.timeout(7000);
      try {
        await put(path, JSON.stringify(suggestion, null, 2), {
          access: "private",
          token,
          contentType: "application/json; charset=utf-8",
          allowOverwrite: false,
          addRandomSuffix: false,
          abortSignal,
        });
        return true;
      } catch (error) {
        if (!(error instanceof BlobError) || error instanceof BlobServiceRateLimited) throw error;
        // The SDK may report a collision as a generic BlobError. Confirm the slot exists.
        try {
          await head(path, { token, abortSignal });
          return false;
        } catch (lookupError) {
          throw lookupError instanceof BlobNotFoundError ? error : lookupError;
        }
      }
    },
  });
}
