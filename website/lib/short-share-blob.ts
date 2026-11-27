import { BlobError, BlobNotFoundError, BlobPreconditionFailedError, get, head, put } from "@vercel/blob";
import { boundedJson, type ShareStore } from "./short-share-api.ts";

export function shortShareStore(token: string): ShareStore {
  const abortSignal = AbortSignal.timeout(7000);
  return {
    async read(path, consistent) {
      const blob = await get(path, { access: "private", token, useCache: !consistent, abortSignal });
      if (!blob || blob.statusCode !== 200) return null;
      return { etag: blob.blob.etag, value: await boundedJson(blob.stream) };
    },
    async write(path, value, etag) {
      try {
        await put(path, JSON.stringify(value), {
          access: "private", token, abortSignal, contentType: "application/json",
          addRandomSuffix: false, allowOverwrite: etag !== null, ifMatch: etag ?? undefined,
        });
        return true;
      } catch (error) {
        if (error instanceof BlobPreconditionFailedError) return false;
        if (etag !== null || !(error instanceof BlobError) || abortSignal.aborted) throw error;
        // Some create-only collisions surface as a generic Blob error.
        try {
          await head(path, { token, abortSignal });
          return false;
        } catch (lookupError) {
          throw lookupError instanceof BlobNotFoundError ? error : lookupError;
        }
      }
    },
  };
}
