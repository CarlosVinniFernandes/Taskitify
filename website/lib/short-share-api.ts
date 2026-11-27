import { createHmac, randomInt } from "node:crypto";
import { isIP } from "node:net";
import { decodeShare, shareFailure } from "./lyrics-share.ts";

const prefix = "lyrics-shares/v1";
const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
const maxCode = 6000;
export const maxBody = 6200;
const dailyLimit = 15;
const ipWindowMs = 600_000;
const dayMs = 86_400_000;

export type ShareStore = {
  read: (path: string, consistent: boolean) => Promise<{ value: unknown; etag: string } | null>;
  // A null ETag creates a new object only. A known ETag replaces exactly that version.
  write: (path: string, value: unknown, etag: string | null) => Promise<boolean>;
};

export function validShareId(id: string) {
  return /^[A-Za-z0-9]{12}$/.test(id);
}

function createId() {
  return Array.from({ length: 12 }, () => alphabet[randomInt(alphabet.length)]).join("");
}

export function shareError(status: number, error: string, retryAfter?: number) {
  const headers = { "Cache-Control": "no-store", "X-Robots-Tag": "noindex, nofollow" };
  return Response.json({ error }, {
    status,
    headers: retryAfter ? { ...headers, "Retry-After": String(retryAfter) } : headers,
  });
}

function success(value: object, status: number) {
  return Response.json(value, { status, headers: { "Cache-Control": "no-store", "X-Robots-Tag": "noindex, nofollow" } });
}

export async function boundedJson(body: ReadableStream<Uint8Array> | null) {
  if (!body) return null;
  const reader = body.getReader();
  const bytes = new Uint8Array(maxBody);
  let size = 0;
  try {
    for (;;) {
      const { value, done } = await reader.read();
      if (done) break;
      if (size + value.length > maxBody) {
        void reader.cancel().catch(() => {});
        throw new RangeError("Body too large");
      }
      bytes.set(value, size);
      size += value.length;
    }
  } finally {
    reader.releaseLock();
  }
  return JSON.parse(new TextDecoder("utf-8", { fatal: true }).decode(bytes.subarray(0, size))) as unknown;
}

function shareCode(value: unknown) {
  if (!value || typeof value !== "object" || !Object.hasOwn(value, "code") || Object.keys(value).length !== 1) return null;
  const { code } = value as { code: unknown };
  return typeof code === "string" && code.length <= maxCode && /^[A-Za-z0-9_-]+$/.test(code) ? code : null;
}

function addressBucket(address: string) {
  const type = isIP(address);
  if (!type || address.includes("%")) return null;
  return type === 6 ? new URL(`http://[${address}]/`).hostname : address;
}

function fromSameSite(request: Request) {
  const site = request.headers.get("sec-fetch-site");
  if (site && site !== "same-origin" && site !== "none") return false;
  const origin = request.headers.get("origin");
  if (!origin) return true;
  const url = new URL(request.url);
  return origin === url.origin || origin === `https://${request.headers.get("host")}`;
}

type CreateOptions = {
  store: ShareStore;
  origin: string;
  address: string;
  secret: string;
  now?: () => number;
  newId?: () => string;
};

export async function createShortShare(request: Request, options: CreateOptions): Promise<Response> {
  if (!fromSameSite(request)) return shareError(403, "origin");
  if (new URL(request.url).search) return shareError(400, "invalid_code");
  if (request.headers.get("content-type")?.split(";")[0].trim().toLowerCase() !== "application/json") {
    return shareError(415, "content_type");
  }
  if (Number(request.headers.get("content-length")) > maxBody) return shareError(413, "too_large");
  let code: string | null;
  try {
    code = shareCode(await boundedJson(request.body));
  } catch (error) {
    return error instanceof RangeError ? shareError(413, "too_large") : shareError(400, "invalid_code");
  }
  if (!code) return shareError(400, "invalid_code");
  try {
    await decodeShare(code);
  } catch (error) {
    return shareError(400, shareFailure(error) === "version" ? "unsupported_version" : "invalid_code");
  }

  const address = addressBucket(options.address);
  if (!address || options.secret.length < 32) return shareError(503, "unavailable");
  const now = options.now?.() ?? Date.now();
  const day = Math.floor(now / dayMs);
  const window = Math.floor(now / ipWindowMs);
  const key = createHmac("sha256", options.secret).update(`${window}|${address}`).digest("hex");
  const quotaPath = `${prefix}/quota/${new Date(now).toISOString().slice(0, 10)}.json`;
  try {
    const quota = await options.store.read(quotaPath, true);
    const slots = quota ? quota.value : [];
    if (!Array.isArray(slots) || !slots.every((slot) => typeof slot === "string" && /^[a-f0-9]{64}$/.test(slot))) {
      return shareError(503, "unavailable");
    }
    if (slots.length >= dailyLimit) return shareError(429, "daily_limit", Math.ceil(((day + 1) * dayMs - now) / 1000));
    if (slots.includes(key)) return shareError(429, "rate_limit", Math.ceil(((window + 1) * ipWindowMs - now) / 1000));
    if (!await options.store.write(quotaPath, [...slots, key], quota?.etag ?? null)) {
      return shareError(429, "busy", 2);
    }
    for (let attempt = 0; attempt < 3; attempt++) {
      const id = options.newId?.() ?? createId();
      if (await options.store.write(`${prefix}/links/${id}.json`, { code }, null)) {
        return success({ url: new URL(`/s/${id}`, options.origin).href }, 201);
      }
    }
  } catch {
    return shareError(503, "unavailable");
  }
  return shareError(503, "unavailable");
}

export async function readShortShare(request: Request, id: string, store: ShareStore): Promise<Response> {
  if (!validShareId(id)) return shareError(404, "not_found");
  if (!fromSameSite(request)) return shareError(403, "origin");
  if (new URL(request.url).search) return shareError(400, "invalid_request");
  try {
    const record = await store.read(`${prefix}/links/${id}.json`, false);
    if (!record) return shareError(404, "not_found");
    const code = shareCode(record.value);
    if (code) return success({ code }, 200);
  } catch {
    return shareError(503, "unavailable");
  }
  return shareError(503, "unavailable");
}
