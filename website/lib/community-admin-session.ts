import { createHash, createHmac, randomBytes, timingSafeEqual } from "node:crypto";

export const adminPath = "/community/admin";
export const adminCookie = "taskitify-wall-admin";
export const sessionSeconds = 30 * 60;

export function adminKey() {
  const value = process.env.COMMUNITY_ADMIN_KEY;
  return value && /^[A-Za-z0-9_-]{32,128}$/.test(value) ? value : undefined;
}

export function matchesKey(candidate: unknown, key: string) {
  if (typeof candidate !== "string" || candidate.length > 128) return false;
  const digest = (value: string) => createHash("sha256").update(value, "utf8").digest();
  return timingSafeEqual(digest(candidate), digest(key));
}

function signature(key: string, content: string) {
  return createHmac("sha256", key).update("taskitify/community/admin/v1:" + content).digest("base64url");
}

export function issueSession(key: string, now = Date.now()) {
  const expires = Math.floor(now / 1000) + sessionSeconds;
  const content = `${expires}.${randomBytes(16).toString("base64url")}`;
  return `${content}.${signature(key, content)}`;
}

export function validSession(value: string | undefined, key: string, now = Date.now()) {
  if (!value || !/^\d{1,12}\.[A-Za-z0-9_-]{22}\.[A-Za-z0-9_-]{43}$/.test(value)) return false;
  const [expires, nonce, mac] = value.split(".");
  const seconds = Math.floor(now / 1000);
  if (Number(expires) <= seconds || Number(expires) > seconds + sessionSeconds) return false;
  return timingSafeEqual(Buffer.from(mac), Buffer.from(signature(key, `${expires}.${nonce}`)));
}
