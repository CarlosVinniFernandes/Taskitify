const maxCode = 8_000;
const maxJson = 64 * 1024;
const proportions = ["story", "square", "portrait", "landscape"] as const;
const fonts = ["auto", "modern", "serif", "handwritten", "mono", "condensed"] as const;
const alignments = ["auto", "left", "center", "right"] as const;
const marks = ["spotify", "taskitify", "none"] as const;
const images = ["album", "personal", "none"] as const;
const reveals = ["direct", "surprise"] as const;
const segmenter = typeof Intl.Segmenter === "function" ? new Intl.Segmenter() : undefined;

export type SharedSong = {
  v: 1;
  t: string;
  f: typeof proportions[number];
  s: { t: string; a: string; b?: string; sp?: string; dur?: number };
  l: { x: string; ms?: number }[];
  o: {
    font: typeof fonts[number]; tf: typeof fonts[number]; scale: number;
    align: typeof alignments[number]; color?: string; cover: boolean;
    mark: typeof marks[number]; time: boolean; img: typeof images[number];
  };
  x: Readonly<Record<string, string>>;
  d?: { from?: string; to?: string; msg?: string };
  c?: string;
  r: typeof reveals[number];
  lang?: string;
};

type ShareErrorKind = "invalid" | "version" | "unsupported";

class ShareError extends Error {
  readonly kind: ShareErrorKind;

  constructor(kind: ShareErrorKind) {
    super(kind);
    this.kind = kind;
  }
}

export function shareFailure(error: unknown) {
  return error instanceof ShareError ? error.kind : "invalid";
}

function invalid(): never {
  throw new ShareError("invalid");
}

function record(value: unknown): Record<string, unknown> {
  if (!value || typeof value !== "object" || Array.isArray(value)) invalid();
  return value as Record<string, unknown>;
}

function text(value: unknown, max?: number): string {
  if (typeof value !== "string") invalid();
  if (max !== undefined && value.length > max) {
    let count = 0;
    for (const _ of segmenter?.segment(value) ?? value) {
      if (++count > max) invalid();
    }
  }
  return value;
}

function optionalText(value: unknown, max: number) {
  return value === undefined ? undefined : text(value, max);
}

function choice<T extends string>(value: unknown, allowed: readonly T[], fallback: T): T {
  if (value === undefined) return fallback;
  if (typeof value !== "string") invalid();
  return allowed.find((option) => option === value) ?? fallback;
}

function bool(value: unknown, fallback: boolean) {
  if (value === undefined) return fallback;
  if (typeof value !== "boolean") invalid();
  return value;
}

function milliseconds(value: unknown) {
  if (value === undefined) return undefined;
  if (typeof value !== "number" || !Number.isSafeInteger(value) || value < 0) invalid();
  return value;
}

function matchingText(value: unknown, pattern: RegExp) {
  if (value === undefined) return undefined;
  const result = text(value);
  if (!pattern.test(result)) invalid();
  return result;
}

function parseShare(input: unknown): SharedSong {
  const data = record(input);
  if (!Number.isInteger(data.v)) invalid();
  if (data.v !== 1) throw new ShareError("version");

  const song = record(data.s);
  if (!Array.isArray(data.l) || data.l.length < 1 || data.l.length > 6) invalid();
  const lines = data.l.map((value: unknown) => {
    const line = record(value);
    return { x: text(line.x, 200), ms: milliseconds(line.ms) };
  });

  const look = data.o === undefined ? {} : record(data.o);
  const scale = look.scale === undefined ? 1 : look.scale;
  if (typeof scale !== "number" || scale < 0.7 || scale > 1.4
      || scale !== Math.round(scale * 100) / 100) invalid();

  const fields = data.x === undefined ? [] : Object.entries(record(data.x));
  if (fields.length > 16) invalid();
  const dedication = data.d === undefined ? undefined : record(data.d);
  const date = matchingText(data.c, /^\d{4}-\d{2}-\d{2}$/);
  if (date) {
    const parsed = new Date(date);
    if (!Number.isFinite(parsed.getTime()) || parsed.toISOString().slice(0, 10) !== date) invalid();
  }
  const language = data.lang === undefined ? undefined : text(data.lang);
  if (language !== undefined) {
    try { Intl.getCanonicalLocales(language); } catch { invalid(); }
  }

  return {
    v: 1,
    t: text(data.t),
    f: choice(data.f, proportions, "story"),
    s: {
      t: text(song.t, 120), a: text(song.a, 120), b: optionalText(song.b, 120),
      sp: matchingText(song.sp, /^[A-Za-z0-9]{22}$/), dur: milliseconds(song.dur),
    },
    l: lines,
    o: {
      font: choice(look.font, fonts, "auto"), tf: choice(look.tf, fonts, "auto"), scale,
      align: choice(look.align, alignments, "auto"), color: matchingText(look.color, /^#[0-9A-F]{6}$/),
      cover: bool(look.cover, true), mark: choice(look.mark, marks, "spotify"),
      time: bool(look.time, false), img: choice(look.img, images, "album"),
    },
    x: Object.fromEntries(fields.map(([key, value]) => [key, text(value, 120)])),
    d: dedication && {
      from: optionalText(dedication.from, 40), to: optionalText(dedication.to, 40),
      msg: optionalText(dedication.msg, 280),
    },
    c: date, r: choice(data.r, reveals, "direct"), lang: language,
  };
}

export async function decodeShare(code: string): Promise<SharedSong> {
  if (!code || code.length > maxCode || code.length % 4 === 1 || !/^[A-Za-z0-9_-]+$/.test(code)) invalid();
  if (typeof DecompressionStream === "undefined") throw new ShareError("unsupported");
  const base64 = code.replace(/-/g, "+").replace(/_/g, "/");
  const bytes = Uint8Array.from(atob(base64.padEnd(Math.ceil(base64.length / 4) * 4, "=")),
    (char) => char.charCodeAt(0));
  const reader = new Blob([bytes]).stream().pipeThrough(new DecompressionStream("gzip")).getReader();
  const buffer = new Uint8Array(maxJson);
  let length = 0;
  try {
    for (;;) {
      const { value, done } = await reader.read();
      if (done) break;
      if (length + value.length > maxJson) {
        await reader.cancel();
        invalid();
      }
      buffer.set(value, length);
      length += value.length;
    }
  } finally {
    reader.releaseLock();
  }
  return parseShare(JSON.parse(new TextDecoder("utf-8", { fatal: true }).decode(buffer.subarray(0, length))));
}

export function spotifySongUrl(song: SharedSong["s"]) {
  if (song.sp) return `https://open.spotify.com/track/${song.sp}`;
  const query = `${song.t} ${song.a}`.trim().replace(/[\uD800-\uDFFF]/gu, "�");
  return `https://open.spotify.com/search/${encodeURIComponent(query)}`;
}

export function songTime(ms: number) {
  const total = Math.floor(ms / 1000);
  const seconds = String(total % 60).padStart(2, "0");
  const minutes = Math.floor(total / 60);
  if (minutes < 60) return `${minutes}:${seconds}`;
  return `${Math.floor(minutes / 60)}:${String(minutes % 60).padStart(2, "0")}:${seconds}`;
}
