import { countryLimit, limits } from "./suggestion-fields.ts";
import type { Suggestion } from "./suggestions.ts";

export const wallPrefix = "suggestions/wall/";
export const wallIndexTag = "community-wall-index";
export const wallEntryTag = "community-wall-entry";
export const wallPathPattern = /^suggestions\/wall\/\d{13}-[a-f0-9-]{36}\.json$/;
const newestFirst = 9_999_999_999_999;

export function communityWallEnabled() {
  return process.env.COMMUNITY_WALL_ENABLED !== "0";
}

export function wallPath(suggestion: Suggestion) {
  const timestamp = String(newestFirst - Date.parse(suggestion.submittedAt)).padStart(13, "0");
  return `${wallPrefix}${timestamp}-${suggestion.id}.json`;
}

export type WallEntry = {
  artSeed: number;
  song: string;
  artist: string;
  name?: string;
  country?: string;
  date: string;
  status: "suggested" | "added";
};

export type GuestSignature = { name: string; date: string; country?: string };
export type GuestbookPage = { names: string[]; signatures: GuestSignature[]; nextOffset: number | null };

export const communityWallPageSizes = { apiDefault: 12, legacyFull: 30, preview: 25, full: 48 } as const;
export type WallPageSize = typeof communityWallPageSizes[keyof typeof communityWallPageSizes];

export type WallPage = { entries: WallEntry[]; hasMore: boolean; totalPages: number };

export function publicSuggestion(value: unknown): WallEntry | null {
  if (!value || typeof value !== "object") return null;
  const { id, song, artist, name, submittedAt, status, publishName, country } = value as Partial<Suggestion>;
  if (status !== "suggested" && status !== "added") return null;
  if (typeof id !== "string" || !/^[a-f0-9-]{36}$/.test(id)
      || typeof submittedAt !== "string" || !Number.isFinite(Date.parse(submittedAt))) return null;
  const validText = (text: unknown, limit: number): text is string => typeof text === "string"
    && Boolean(text.trim()) && text.length <= limit && !/[\p{Cc}\u202a-\u202e\u2066-\u2069]/u.test(text);
  if (!validText(song, limits.song) || !validText(artist, limits.artist)) return null;
  let seed = 2166136261;
  for (const char of id) seed = Math.imul(seed ^ char.charCodeAt(0), 16777619);
  return {
    artSeed: seed >>> 0, song, artist, date: new Date(submittedAt).toISOString().slice(0, 10), status,
    ...(publishName === true && validText(name, limits.name) ? { name, ...(validText(country, countryLimit) ? { country } : {}) } : {}),
  };
}
