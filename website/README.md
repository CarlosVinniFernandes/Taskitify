# Taskitify website

The Next.js site lives in this directory. English is the default. Brazilian Portuguese is available at `/pt-br`.

## Run locally

Use Node.js 24. Run `npm ci`, copy `.env.example` to `.env.local`, and start with `npm run dev`. Use `npm test` and `npm run build` to verify changes. The pages work with empty configuration. Submitting a valid suggestion requires a private Blob store and a rate-limit secret.

## Deploy on Vercel Hobby

1. Import the GitHub repository into your personal Hobby account. Select **Next.js**, set **Root Directory** to `website`, and select the Git branch you want to deploy.
2. In **Storage**, create a **private** Blob store and connect it to the project. Vercel injects `BLOB_READ_WRITE_TOKEN`. Use a separate store if you want to test suggestions in preview or development.
3. Set `SUGGESTION_RATE_SECRET` for each environment. Generate a stable value with `node -e "console.log(require('node:crypto').randomBytes(32).toString('hex'))"`.
4. Set `SITE_URL` to the canonical HTTPS origin and `CONTACT_EMAIL` to the confirmed public address. The site offers GitHub Issues for contact while the address is empty. Redeploy after changing these settings. The official Community Soundtrack URL is set in `lib/site.ts`.

The [Hobby plan](https://vercel.com/docs/plans/hobby) is for personal, noncommercial projects. The [Blob allowance](https://vercel.com/docs/vercel-blob/usage-and-pricing) includes 1 GB-month of storage, 10,000 simple operations, 2,000 advanced operations, and 10 GB of transfer per month. Stay on Hobby to use the included quotas without paid overages.

## Moderate suggestions

Open **Storage → your private store → Browse** in Vercel. Community Wall submissions are in `suggestions/wall/`. Older submissions and submissions made while the wall is disabled remain private under `suggestions/YYYY-MM/`. The monthly folders may also contain rate-limit slots with no submission data. Add selected tracks to Spotify manually.

New wall submissions are visible immediately as `suggested`. The public endpoint `GET /api/community/wall?page=1` projects only the song, artist, date, status and public signatures, plus pagination metadata and a styling seed. Earlier anonymous contributions remain anonymous. It pages through 12 records at a time, omitting hidden and removed entries. Older pages may contain fewer visible rows.

To hide a contribution or mark it as added, copy its exact Blob pathname and run `node --env-file=.env.local scripts/moderate-wall.ts <pathname> hidden` (or `added`, or `suggested` to restore it). Deletion is also available in the Blob dashboard. The private Blob records stay private. Listing metadata and public projections are cached for one hour to limit Blob operations. For immediate moderation, clear the two derived cache tags from the linked Vercel project with `vercel cache dangerously-delete --tag community-wall-entry,community-wall-index`. Publish with `COMMUNITY_WALL_ENABLED=0` to pause the wall and accept private suggestions only.

The API reserves one suggestion per address per minute using an atomic Blob write. The path uses a rotating keyed hash and no raw IP is stored in the suggestion. For future app integration, `/community?song=Song&artist=Artist&name=Nickname#suggest` pre-fills the editable form. Sending still requires an explicit submit, and requires a name or nickname.

Product images come from the [Store campaign and onboarding assets](../marketing/microsoft-store/SOURCES.md). Font license notices are in `public/fonts/`.

For browser moderation, set the server-only `COMMUNITY_ADMIN_KEY` in Vercel and redeploy. Generate a random key with `node -e "console.log(require('node:crypto').randomBytes(32).toString('base64url'))"` and put it in the password field at `/community/admin`. The admin page is disabled without a key. It edits the title, artist, and status of existing wall entries and refreshes the wall after each save. The session lasts 30 minutes. Changing the key invalidates existing sessions.
