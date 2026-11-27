import assert from "node:assert/strict";
import test from "node:test";
import { gzipSync } from "node:zlib";
import { decodeShare, shareFailure, spotifySongUrl } from "../lib/lyrics-share.ts";
import { shareLocale } from "../lib/lyrics-share-copy.ts";
import examples from "./lyrics-share-examples.json" with { type: "json" };

const legacy = {
  v: 1, t: "Classic", s: { t: "After the Rain", a: "Demo Artist" }, l: [{ x: "Somewhere in time" }],
};
const encode = (obj: unknown) => gzipSync(JSON.stringify(obj)).toString("base64url");

test("opens each example exported by the desktop serializer at bfbb19b", async () => {
  for (const { name, code, expected } of examples) {
    const card = await decodeShare(code);
    const present = JSON.parse(JSON.stringify(card));
    assert.equal(card.t, expected.t, name);
    assert.deepEqual(present.s, expected.s, name);
    assert.deepEqual(present.l, expected.l, name);
    assert.deepEqual(present.d, expected.d, name);
    assert.deepEqual(present.x, expected.x ?? {}, name);
    assert.equal(card.f, expected.f ?? "story", name);
    assert.equal(card.r, expected.r ?? "direct", name);
    assert.equal(card.c, expected.c, name);
    assert.equal(card.lang, expected.lang, name);
    for (const [key, value] of Object.entries(expected.o ?? {})) {
      assert.equal(card.o[key as keyof typeof card.o], value, name);
    }
  }
  const receipt = await decodeShare(examples[1].code);
  assert.equal(spotifySongUrl(receipt.s), "https://open.spotify.com/track/1a2B3c4D5e6F7g8H9i0J1k");
  const unicode = await decodeShare(examples[2].code);
  assert.equal(spotifySongUrl(unicode.s),
    "https://open.spotify.com/search/Can%C3%A7%C3%A3o%20do%20Caminho%20Artista%20de%20Demonstra%C3%A7%C3%A3o");
});

test("keeps old and extensible v1 links readable", async () => {
  const old = await decodeShare(encode(legacy));
  assert.equal(old.r, "direct");
  assert.equal(old.lang, undefined);
  assert.equal(old.s.dur, undefined);
  const future = await decodeShare(encode({
    ...legacy, t: "FutureStyle", r: "future", f: "future", o: { font: "future", more: 42 },
    x: { futureField: "a new text field" }, ignored: true,
  }));
  assert.equal(future.t, "FutureStyle");
  assert.equal(future.r, "direct");
  assert.equal(future.f, "story");
  assert.equal(future.o.font, "auto");
  assert.equal(future.x.futureField, "a new text field");
});

test("rejects malformed, oversized and incompatible links", async () => {
  await assert.rejects(decodeShare(encode({ ...legacy, v: 2 })), (error) => shareFailure(error) === "version");
  for (const code of [
    "", "invalid%20", examples[0].code + "=", "A".repeat(8001), examples[0].code.slice(0, -6),
    gzipSync(Buffer.from([0xff, 0xff])).toString("base64url"),
    encode({ ...legacy, extra: "x".repeat(64 * 1024) }),
    encode({ ...legacy, l: [] }),
    encode({ ...legacy, l: [{ x: "x".repeat(201) }] }),
    encode({ ...legacy, d: { from: "x".repeat(41) } }),
    encode({ ...legacy, s: { ...legacy.s, sp: "https://example.com" } }),
    encode({ ...legacy, s: { ...legacy.s, dur: "1000" } }),
    encode({ ...legacy, o: { color: "url(https://example.com)" } }),
    encode({ ...legacy, o: { scale: 1.5 } }),
    encode({ ...legacy, c: "2026-02-29" }),
    encode({ ...legacy, x: Object.fromEntries(Array.from({ length: 17 }, (_, i) => [i, "x"])) }),
  ]) {
    await assert.rejects(decodeShare(code), (error) => shareFailure(error) === "invalid");
  }
});

test("chooses recipient browser language, then sender, then English", () => {
  for (const locale of ["en", "pt-BR", "es", "fr", "de", "it"] as const) {
    assert.equal(shareLocale(["ja", locale], "ja"), locale);
  }
  assert.equal(shareLocale(["en-GB", "pt-BR"], "fr"), "en");
  assert.equal(shareLocale(["pt-PT"], "en"), "pt-BR");
  assert.equal(shareLocale(["ja"], "it"), "it");
  assert.equal(shareLocale(["ja"], "ko"), "en");
});
