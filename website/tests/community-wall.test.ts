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

test("publishes only opted-in suggestions and omits nicknames without explicit consent", async () => {
  const anonymous = await submit({ publishSuggestion: true });
  assert.equal(anonymous.response.status, 201);
  assert.equal(anonymous.saved?.name, data.name);
  assert.deepEqual(publicSuggestion(anonymous.saved), {
    song: data.song, artist: data.artist, date: "2026-11-26", status: "suggested",
  });
  const named = (await submit({ publishSuggestion: true, publishName: true })).saved!;
  assert.equal(publicSuggestion(named)?.name, data.name);
  assert.equal(publicSuggestion({ ...named, status: "added" })?.status, "added");
  assert.equal(publicSuggestion({ ...named, status: "hidden" }), null);
  assert.equal(publicSuggestion((await submit({ publishName: true })).saved), null);
  assert.equal(publicSuggestion((await submit({ publishSuggestion: true, publishName: true }, false)).saved), null);
  assert.equal((await submit({ publishSuggestion: true, publishName: "yes" })).response.status, 400);
  assert.equal((await submit({ song: "Bad\u202eTitle" })).response.status, 400);
  assert.ok(wallPath({ ...named, submittedAt: "2026-11-26T19:05:01Z" }) < wallPath(named));
});
