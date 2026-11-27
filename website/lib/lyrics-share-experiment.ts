export function shareViewerEnabled() {
  return process.env.VERCEL_ENV !== "production"
    && (process.env.NODE_ENV === "development" || process.env.LYRICS_SHARE_EXPERIMENT === "1");
}

export function shortSharesEnabled() {
  return shareViewerEnabled() && process.env.LYRICS_SHORT_LINK_EXPERIMENT === "1";
}
