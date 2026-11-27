import assert from "node:assert/strict";
import { mkdir, mkdtemp, readFile, readdir, rmdir, unlink, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import test from "node:test";
import { prefill } from "../lib/suggestion-fields.ts";
import { handleSuggestion, type Suggestion } from "../lib/suggestions.ts";

const url = "https://taskitify.example/api/community/suggestions";
const now = new Date("2026-08-23T01:02:12.000Z");
const valid = { song: " Águas de Março ", artist: " Elis Regina & Tom Jobim " };
const options = { address: "203.0.113.1", secret: "x".repeat(32), now: () => now };

function request(input: unknown, headers: Record<string, string> = {}) {
  return new Request(url, {
    method: "POST",
    headers: { Origin: "https://taskitify.example", "Content-Type": "application/json", ...headers },
    body: JSON.stringify(input),
  });
}

test("creates independent JSON records and rate-limits a concurrent submission", async () => {
  const root = await mkdtemp(join(tmpdir(), "taskitify-suggestions-"));
  const folder = join(root, "suggestions", "2026-08");
  async function create(path: string, suggestion: Suggestion) {
    const file = join(root, path);
    await mkdir(dirname(file), { recursive: true });
    try {
      await writeFile(file, JSON.stringify(suggestion), { flag: "wx" });
      return true;
    } catch (error) {
      if ((error as NodeJS.ErrnoException).code === "EEXIST") return false;
      throw error;
    }
  }
  try {
    const submit = () => handleSuggestion(request(valid), { ...options, create });
    const responses = await Promise.all([submit(), submit()]);
    const success = responses.find((response) => response.status === 201);
    const limited = responses.find((response) => response.status === 429);
    assert.deepEqual(await success?.json(), { ok: true });
    assert.equal(limited?.headers.get("retry-after"), "48");
    assert.deepEqual(await limited?.json(), { ok: false, code: "rate" });

    const files = await readdir(folder);
    assert.equal(files.length, 1);
    const saved = JSON.parse(await readFile(join(folder, files[0]), "utf8"));
    assert.match(saved.id, /^[0-9a-f-]{36}$/);
    assert.deepEqual(saved, {
      id: saved.id, song: "Águas de Março", artist: "Elis Regina & Tom Jobim",
      name: null, submittedAt: now.toISOString(), status: "pending",
    });
    assert.equal((await handleSuggestion(request({ ...valid, name: "C" }), {
      ...options, now: () => new Date(now.getTime() + 60_000), create,
    })).status, 201);
    assert.equal((await readdir(folder)).length, 2);
  } finally {
    for (const file of await readdir(folder).catch(() => [])) await unlink(join(folder, file));
    await rmdir(folder).catch(() => {});
    await rmdir(dirname(folder)).catch(() => {});
    await rmdir(root);
  }
});

test("rejects invalid input before writing", async () => {
  const create = async () => { throw new Error("unexpected write"); };
  const examples = [
    [request({ ...valid, song: " " }), 400],
    [request({ ...valid, name: "x".repeat(61) }), 400],
    [request({ ...valid, website: "https://example.org" }), 400],
    [request({ ...valid, song: "x".repeat(4096) }), 413],
    [request(valid, { Origin: "https://example.org" }), 403],
    [request(valid, { "Content-Type": "text/plain" }), 415],
  ] as const;
  for (const [input, status] of examples) {
    assert.equal((await handleSuggestion(input, { ...options, create })).status, status);
  }
});

test("reports unavailable storage and keeps URL prefills bounded", async () => {
  const create = async () => { throw new Error("storage unavailable"); };
  assert.equal((await handleSuggestion(request(valid), { ...options, create })).status, 503);
  assert.deepEqual(prefill({ song: ["  Track  ", "unused"], artist: "x".repeat(200) }), {
    song: "Track", artist: "x".repeat(120), name: "",
  });
});
