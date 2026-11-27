export const limits = { song: 120, artist: 120, name: 60 } as const;
export type Fields = Record<keyof typeof limits, string>;

export function prefill(params: Record<string, string | string[] | undefined>): Fields {
  function value(key: keyof Fields) {
    const item = params[key];
    const first = typeof item === "string" ? item : item?.[0];
    return first?.replace(/[\u0000-\u001F\u007F]/g, " ").trim().slice(0, limits[key]) ?? "";
  }
  return { song: value("song"), artist: value("artist"), name: value("name") };
}
