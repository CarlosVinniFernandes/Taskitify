import assert from "node:assert/strict";
import test from "node:test";
import { gzipSync } from "node:zlib";
import { createShortShare, readShortShare, type ShareStore } from "../lib/short-share-api.ts";
import examples from "./lyrics-share-examples.json" with { type: "json" };

const origin = "https://taskitify.example";
const now = () => Date.parse("2026-11-27T00:04:20Z");
const address = "203.0.113.1";
const secret = "x".repeat(40);
const { code } = examples[1];

function memoryStore() {
  const data = new Map<string, { value: unknown; etag: string }>();
  let version = 0;
  const store: ShareStore = {
    async read(path) { return data.get(path) ?? null; },
    async write(path, value, etag) {
      if ((data.get(path)?.etag ?? null) !== etag) return false;
      data.set(path, { value, etag: String(++version) });
      return true;
    },
  };
  return { data, store };
}

function post(body: unknown = { code }, headers: Record<string, string> = {}) {
  return new Request(origin + "/api/share", {
    method: "POST", body: JSON.stringify(body),
    headers: { "Content-Type": "application/json", ...headers },
  });
}

test("stores the unchanged desktop code, retries ID collisions, and reads only that ID", async () => {
  const { store, data } = memoryStore();
  const occupied = "000000000000";
  const next = "K8mP2xR7nQ4a";
  const existing = "lyrics-shares/v1/links/" + occupied + ".json";
  await store.write(existing, { code: examples[0].code }, null);
  const ids = [occupied, next];
  const response = await createShortShare(post(), { store, origin, address, secret, now, newId: () => ids.shift()! });
  assert.equal(response.status, 201);
  assert.deepEqual(await response.json(), { url: origin + "/s/" + next });
  assert.deepEqual(data.get(existing)?.value, { code: examples[0].code });
  assert.deepEqual(data.get("lyrics-shares/v1/links/" + next + ".json")?.value, { code });

  const request = new Request(origin + "/api/share/" + next);
  const saved = await readShortShare(request, next, store);
  assert.equal(saved.headers.get("cache-control"), "no-store");
  assert.deepEqual(await saved.json(), { code });
  assert.equal((await readShortShare(request, "../quota", store)).status, 404);
  assert.equal((await readShortShare(request, "000000000001", store)).status, 404);
  assert.equal((await readShortShare(new Request(request.url, { headers: { "Sec-Fetch-Site": "cross-site" } }), next, store)).status, 403);
});

test("rejects invalid, oversized or cross-origin uploads before touching storage", async () => {
  const { store, data } = memoryStore();
  const gzip = (obj: unknown) => gzipSync(JSON.stringify(obj)).toString("base64url");
  const cases = [
    [post({ code, message: "unexpected" }), 400],
    [post({ code: "x".repeat(6001) }), 400],
    [post({ code: "x".repeat(6201) }), 413],
    [post({ code: code.slice(0, -8) }), 400],
    [post({ code: gzip({ ...examples[0].expected, padding: "x".repeat(65536) }) }), 400],
    [post({ code: gzip({ v: 2 }) }), 400],
    [post({ code }, { Origin: "https://another.example" }), 403],
    [post({ code }, { "Content-Type": "text/plain" }), 415],
  ] as const;
  for (const [request, status] of cases) {
    assert.equal((await createShortShare(request, { store, origin, address, secret, now })).status, status);
  }
  assert.equal(data.size, 0);
});

test("atomically reserves windows across requests and stops at the daily budget", async () => {
  const { store, data } = memoryStore();
  const submit = (ip = address, time = now) => createShortShare(post(), { store, origin, address: ip, secret, now: time });
  const results = await Promise.all([submit(), submit(), submit()]);
  const accepted = results.find((response) => response.status === 201)!;
  assert.match((await accepted.json()).url, /^https:\/\/taskitify\.example\/s\/[A-Za-z0-9]{12}$/);
  assert.deepEqual(results.map(({ status }) => status).sort(), [201, 429, 429]);
  const repeat = await submit();
  assert.equal(repeat.headers.get("retry-after"), "340");
  assert.deepEqual(await repeat.json(), { error: "rate_limit" });
  for (let i = 2; i <= 15; i++) assert.equal((await submit("203.0.113." + i)).status, 201);
  const capped = await submit("203.0.113.16");
  assert.deepEqual(await capped.json(), { error: "daily_limit" });
  assert.equal([...data.keys()].filter((path) => path.includes("/links/")).length, 15);
  assert.ok(!JSON.stringify([...data.values()]).includes(address));
  assert.equal((await submit(address, () => now() + 86_400_000)).status, 201);
});
