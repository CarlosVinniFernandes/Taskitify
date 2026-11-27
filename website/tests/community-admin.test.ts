import assert from "node:assert/strict";
import test from "node:test";
import { issueSession, matchesKey, sessionSeconds, validSession } from "../lib/community-admin-session.ts";

test("admin sessions expire and reject altered signatures or a rotated key", () => {
  const key = "T".repeat(43);
  const now = Date.parse("2026-11-27T08:00:00Z");
  const session = issueSession(key, now);
  assert.equal(matchesKey(key, key), true);
  assert.equal(matchesKey("wrong", key), false);
  assert.equal(validSession(session, key, now), true);
  assert.equal(validSession(session.slice(0, -2) + "!!", key, now), false);
  const [expiry, nonce, mac] = session.split(".");
  assert.equal(validSession(`${expiry}.${nonce}.${mac[0] === "a" ? "b" : "a"}${mac.slice(1)}`, key, now), false);
  assert.equal(validSession(session, "R".repeat(43), now), false);
  assert.equal(validSession(session, key, now + sessionSeconds * 1000), false);
});
