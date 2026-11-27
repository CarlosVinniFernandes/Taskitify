# Send a Song short links · API v1

Status: experimental. The public production deployment keeps the viewer and API disabled while lyric-sharing rights are reviewed.

## Create a link

The desktop sends the `CODIGO` it already generates for `/s#CODIGO`. Use the unchanged [composition v1 contract](https://github.com/CarlosVinniFernandes/Taskitify/blob/bfbb19b/shared/lyrics-share-v1.md): UTF-8 JSON → gzip → unpadded Base64URL. Send the code alone, without the host, path, or `#`.

```http
POST /api/share
Content-Type: application/json

{"code":"<CODIGO>"}
```

Success is HTTP **201** with one field:

```json
{"url":"https://taskitify.vercel.app/s/K8mP2xR7nQ4a"}
```

The URL shown is illustrative. Use the actual returned URL. For integration tests, use one of the original contract's sample codes with the experimental origin. An individual ID is case-sensitive and contains exactly 12 characters from `A–Z`, `a–z`, and `0–9`. The server generates it with a cryptographically secure RNG, creates the object atomically and retries a collision with a new ID (up to three attempts). Repeating a successful POST is not idempotent. The desktop should use the original long link as a fallback if creation fails.

The request body may contain only `code`. The HTTP body limit is 6,200 bytes, the stored code limit is 6,000 characters, and decoded JSON is limited to 64 KiB. The upload validates the actual composition and version before reserving capacity or writing. Web requests from another origin are refused. Native desktop requests can omit `Origin`.

## Read a link

`GET /s/{id}` serves the same viewer used by `/s#CODIGO`. Client-side code reads `GET /api/share/{id}`, which returns HTTP **200** with `{"code":"<CODIGO>"}`. The viewer validates and decodes it again and retains the short URL. Open Graph metadata is generic and contains no lyrics, names, title, or message. Links with an incomplete or unknown ID show an error in the recipient's language.

The Blob store is private. The API returns only the record for the exact ID. There is no list endpoint, and responses containing compositions use `Cache-Control: no-store`.

## Errors

API errors are JSON objects with one `error` field, except for platform-generated errors such as the WAF or a method Next.js does not support.

| HTTP | Error | Meaning |
| --- | --- | --- |
| 400 | `invalid_code` | Invalid JSON, code, size, encoding or v1 fields |
| 400 | `unsupported_version` | The composition uses an unsupported version |
| 400 | `invalid_request` | Query parameters on the read endpoint |
| 403 | `origin` | A cross-origin request was refused |
| 404 | `not_found` | Link missing, invalid ID, or experiment disabled |
| 413 | `too_large` | HTTP body exceeds 6,200 bytes or the server's configured code limit |
| 415 | `content_type` | Expected `application/json` |
| 429 | `rate_limit`, `daily_limit`, `busy` | Per-address window, daily capacity or concurrent reservation; see `Retry-After` |
| 503 | `unavailable` | Store/configuration unavailable or an object could not be saved |

## Storage, quota and retention

- The application writes one JSON object per link, with only `code`, under `lyrics-shares/v1/links/` in Vercel Blob. This code is compressed, not encrypted. Anyone holding the link can recover the lyrics, song details, and any names or dedication. Do not include private information that should not travel with a forwarded link.
- Links have no automatic expiry. The maintainer can remove an individual object by ID on request or when required by moderation, rights, or policy. A removed link returns 404 once storage caches clear. Browser history and copies recipients already made are outside this policy.
- By default, at most 15 slots are reserved each UTC day and one per address per fixed 10-minute window within that day. A single conditional Blob update enforces both limits across instances; quota is never refunded on a failed write. Quota objects under `lyrics-shares/v1/quota/` contain only keyed time-window identifiers, never raw IPs. They stop being used after their UTC day and can be removed during routine maintenance.
- Server operators can set `SHARE_CODE_DAILY_LIMIT` (1–30), `SHARE_CODE_IP_WINDOW_SECONDS` (60–86,400) and `SHARE_CODE_MAX_LENGTH` (1–6,000). A lower upload limit does not affect existing links.
- On [Vercel Hobby](https://vercel.com/docs/vercel-blob/usage-and-pricing), the quota leaves room in the shared 2,000 monthly advanced operations: up to 930 normal writes per 31 days from this feature, plus failed attempts. Repeated reads, rejected writes, and other projects can still consume shared usage. On a hosted experiment, configure the [Hobby WAF rate-limit rule](https://vercel.com/docs/vercel-firewall/vercel-waf/rate-limiting) for `/api/share` and `/api/share/*` (both methods, for example 10 requests per minute per IP), monitor usage, and restrict the preview distribution. Turning off the experiment stops both creation and retrieval without deleting any saved links.
