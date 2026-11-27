import { createHmac, randomUUID } from "node:crypto";
import { isIP } from "node:net";
import { countryLimit, limits, type Fields } from "./suggestion-fields.ts";

const payloadLimit = 4096;
const windowMs = 60_000;

export type Suggestion = {
  id: string;
  song: string;
  artist: string;
  name: string | null;
  submittedAt: string;
  status: "pending" | "suggested" | "added" | "hidden";
  publishName?: boolean;
  country?: string;
};

type Options = {
  address: string | null;
  secret: string | undefined;
  create: (path: string, suggestion: Suggestion) => Promise<boolean>;
  wallEnabled?: boolean;
  now?: () => Date;
};

function reply(status: number, code: string, retryAfter?: number) {
  const headers = new Headers({ "Cache-Control": "no-store" });
  if (retryAfter) headers.set("Retry-After", String(retryAfter));
  return Response.json(status === 201 ? { ok: true } : { ok: false, code }, { status, headers });
}

function sameOrigin(request: Request) {
  const site = request.headers.get("sec-fetch-site");
  if (site && site !== "same-origin" && site !== "none") return false;
  const origin = request.headers.get("origin");
  if (!origin) return true;
  try {
    const url = new URL(request.url);
    const host = request.headers.get("host") || url.host;
    const protocol = request.headers.get("x-forwarded-proto") || url.protocol.slice(0, -1);
    return new URL(origin).origin === `${protocol}://${host}`;
  } catch {
    return false;
  }
}

async function readPayload(request: Request) {
  if (Number(request.headers.get("content-length")) > payloadLimit) {
    return { tooLarge: true } as const;
  }
  if (!request.body) return { data: null };
  const reader = request.body.getReader();
  const data = new Uint8Array(payloadLimit);
  let length = 0;
  try {
    for (;;) {
      const chunk = await reader.read();
      if (chunk.done) break;
      if (length + chunk.value.length > payloadLimit) {
        await reader.cancel();
        return { tooLarge: true } as const;
      }
      data.set(chunk.value, length);
      length += chunk.value.length;
    }
    return { data: JSON.parse(new TextDecoder("utf-8", { fatal: true }).decode(data.subarray(0, length))) as unknown };
  } catch {
    return { data: null };
  } finally {
    reader.releaseLock();
  }
}

function validate(data: unknown): (Fields & { country?: string; publishSuggestion: boolean; publishName: boolean }) | null {
  if (!data || typeof data !== "object" || Array.isArray(data)) return null;
  const fields = data as Record<string, unknown>;
  if (Object.keys(fields).some((key) => !["song", "artist", "name", "country", "website", "publishSuggestion", "publishName"].includes(key))) return null;
  if (fields.website !== undefined && fields.website !== "") return null;
  if ([fields.publishSuggestion, fields.publishName].some((value) => value !== undefined && typeof value !== "boolean")) return null;

  if (fields.country !== undefined && (typeof fields.country !== "string"
    || fields.country.length > countryLimit || /[\p{Cc}\u202a-\u202e\u2066-\u2069]/u.test(fields.country))) return null;
  const country = typeof fields.country === "string" ? fields.country.normalize("NFC").trim() : "";
  if (country.length > countryLimit) return null;

  const result: Fields = { song: "", artist: "", name: "" };
  for (const key of Object.keys(limits) as (keyof Fields)[]) {
    const raw = fields[key];
    if (typeof raw !== "string" || raw.length > limits[key] || /[\p{Cc}\u202a-\u202e\u2066-\u2069]/u.test(raw)) return null;
    const value = raw.normalize("NFC").trim();
    if (value.length > limits[key] || !value) return null;
    result[key] = value;
  }
  return { ...result, ...(country ? { country } : {}), publishSuggestion: fields.publishSuggestion === true, publishName: fields.publishName !== false };
}

export async function handleSuggestion(request: Request, options: Options): Promise<Response> {
  if (!sameOrigin(request)) return reply(403, "origin");
  if (request.headers.get("content-type")?.split(";")[0].trim().toLowerCase() !== "application/json") {
    return reply(415, "content-type");
  }
  const payload = await readPayload(request);
  if ("tooLarge" in payload) return reply(413, "size");
  const fields = validate(payload.data);
  if (!fields || (options.wallEnabled && fields.publishSuggestion && fields.publishName === false)) return reply(400, "fields");

  const address = options.address?.trim() ?? "";
  const addressVersion = isIP(address);
  if (!options.secret || options.secret.length < 32 || !addressVersion || address.includes("%")) return reply(503, "unavailable");

  const now = options.now?.() ?? new Date();
  const window = Math.floor(now.getTime() / windowMs);
  // One atomic private write per IP per minute, across all function instances.
  // Each window has a different keyed path. No raw address is saved.
  const client = addressVersion === 6 ? new URL(`http://[${address}]/`).hostname : address;
  const key = createHmac("sha256", options.secret).update(`${window}|${client}`).digest("hex");
  const path = `suggestions/${new Date(window * windowMs).toISOString().slice(0, 7)}/${key}.json`;
  const publish = options.wallEnabled === true && fields.publishSuggestion;
  const suggestion: Suggestion = {
    id: randomUUID(),
    song: fields.song,
    artist: fields.artist,
    name: fields.name,
    ...(fields.country ? { country: fields.country } : {}),
    submittedAt: now.toISOString(),
    status: publish ? "suggested" : "pending",
    ...(publish ? { publishName: true } : {}),
  };

  try {
    if (await options.create(path, suggestion)) return reply(201, "saved");
    return reply(429, "rate", Math.ceil(((window + 1) * windowMs - now.getTime()) / 1000));
  } catch {
    return reply(503, "unavailable");
  }
}
