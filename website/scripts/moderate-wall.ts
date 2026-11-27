import { get, put } from "@vercel/blob";
import { wallPathPattern } from "../lib/community-wall.ts";

const [path, status] = process.argv.slice(2);
const token = process.env.BLOB_READ_WRITE_TOKEN;
if (!token || !path || !wallPathPattern.test(path) || !["suggested", "added", "hidden"].includes(status)) {
  throw new Error("Usage: node --env-file=.env.local scripts/moderate-wall.ts <wall pathname> <suggested|added|hidden>");
}
const blob = await get(path, { access: "private", token, useCache: false });
if (blob?.statusCode !== 200 || !blob.blob.etag) throw new Error("Contribution not found");
const record = await new Response(blob.stream).json();
if (!["suggested", "added", "hidden"].includes(record.status)) throw new Error("Invalid contribution record");
await put(path, JSON.stringify({ ...record, status }), {
  access: "private", token, contentType: "application/json",
  addRandomSuffix: false, allowOverwrite: true, ifMatch: blob.blob.etag,
});
process.stdout.write("Wall status saved. Clear the wall cache tags to show this change immediately.\n");
