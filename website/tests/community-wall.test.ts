import assert from "node:assert/strict";
import test from "node:test";
import { publicSuggestion, wallPath } from "../lib/community-wall.ts";
import { handleSuggestion, type Suggestion } from "../lib/suggestions.ts";

const data = { song: "One song", artist: "An artist", name: "Nickname" };

async function submit(flags: Record<string, unknown>, wallEnabled = true) {
  let saved: Suggestion | undefined;
  const response = await handleSuggestion(new Request("https://taskitify.test/api/community/suggestions", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ ...data, ...flags }),
  }), {
    address: "203.0.113.5",
    secret: "a".repeat(32),
    wallEnabled,
    now: () => new Date("2026-11-26T19:05:00Z"),
    async create(_path, record) { saved = record; return true; },
  });
  return { response, saved };
}

test("requires signatures on new posts and preserves earlier anonymity", async () => {
  const named = (await submit({ publishSuggestion: true })).saved!;
  const publicRow = publicSuggestion(named);
  assert.equal(publicRow?.name, data.name);
  assert.equal(publicSuggestion({ ...named, status: "added" })?.artSeed, publicRow?.artSeed);
  assert.equal(publicSuggestion({ ...named, song: "Corrected title" })?.artSeed, publicRow?.artSeed);
  assert.equal(publicSuggestion({ ...named, publishName: false })?.name, undefined);
  assert.equal(publicSuggestion({ ...named, status: "hidden" }), null);
  assert.equal(publicSuggestion((await submit({}, false)).saved), null);
  for (const flags of [{ name: undefined }, { name: " " }, { publishName: false }, { publishName: "yes" }]) {
    assert.equal((await submit({ publishSuggestion: true, ...flags })).response.status, 400);
  }
  assert.ok(wallPath({ ...named, submittedAt: "2026-11-26T19:05:01Z" }) < wallPath(named));
});

test("stores an optional country with the public signature", async () => {
  const { response, saved } = await submit({ publishSuggestion: true, country: "  Brazil  " });
  assert.equal(response.status, 201);
  assert.equal(saved?.country, "Brazil");
  assert.equal(publicSuggestion(saved)?.country, "Brazil");
  assert.deepEqual(await response.json(), { ok: true, entry: publicSuggestion(saved) });
  assert.equal(publicSuggestion({ ...saved!, publishName: false })?.country, undefined);
  assert.equal((await submit({ publishSuggestion: true, country: "" })).saved?.country, undefined);
  for (const country of [42, "x".repeat(61), "a\nb"]) {
    assert.equal((await submit({ publishSuggestion: true, country })).response.status, 400);
  }
});
